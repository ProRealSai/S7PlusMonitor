using S7CommPlusDriver;
using S7CommPlusDriver.ClientApi;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Services;

/// <summary>基于 S7CommPlusDriver 的 <see cref="IPlcService"/> 实现；负责 Auto 回退（驱动内/应用两次连接）与 800 分批读，单操作加锁。</summary>
public sealed class S7CommPlusPlcService : IPlcService
{
    private readonly SemaphoreSlim _plcOperationLock = new(1, 1);
    private S7CommPlusClient? _client;
    private CancellationTokenSource? _connectionCts;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(PlcConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _plcOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);

            var password = NormalizeOptional(settings.Password);
            var username = NormalizeOptional(settings.Username);

            if (settings.SecurityMode == PlcSecurityMode.Auto
                && settings.AutoFallbackMode == AutoSecurityFallbackMode.Application)
            {
                await ConnectWithApplicationAutoFallbackAsync(settings, password, username, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await ConnectWithModeAsync(settings, settings.SecurityMode, password, username, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _plcOperationLock.Release();
        }
    }

    private async Task ConnectWithApplicationAutoFallbackAsync(
        PlcConnectionSettings settings,
        string? password,
        string? username,
        CancellationToken cancellationToken)
    {
        Exception? tlsError = null;
        try
        {
            await ConnectWithModeAsync(settings, PlcSecurityMode.Tls, password, username, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            tlsError = ex;
        }

        if (_client?.IsConnected == true)
        {
            return;
        }

        await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ConnectWithModeAsync(settings, PlcSecurityMode.LegacyChallenge, password, username, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception legacyEx)
        {
            var tlsMessage = ExceptionFormatter.Format(tlsError);
            var legacyMessage = ExceptionFormatter.Format(legacyEx);
            throw new InvalidOperationException(
                $"Auto（应用两次连接）失败：TLS 未成功（{tlsMessage}）；Legacy Challenge 也未成功（{legacyMessage}）。",
                legacyEx);
        }
    }

    private async Task ConnectWithModeAsync(
        PlcConnectionSettings settings,
        PlcSecurityMode securityMode,
        string? password,
        string? username,
        CancellationToken cancellationToken)
    {
        _client = CreateClient(settings, securityMode, password);
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await AuthenticateIfNeededAsync(username, password, cancellationToken).ConfigureAwait(false);
        ResetConnectionCancellation();
    }

    private void ResetConnectionCancellation()
    {
        _connectionCts?.Dispose();
        _connectionCts = new CancellationTokenSource();
    }

    private void CancelConnectionOperations()
    {
        if (_connectionCts == null)
        {
            return;
        }

        try
        {
            _connectionCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static S7CommPlusClient CreateClient(
        PlcConnectionSettings settings,
        PlcSecurityMode securityMode,
        string? password)
    {
        var userTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
        var driverMode = PlcSecurityModeMapper.ToDriverMode(securityMode);
        var disconnectTimeout = TimeSpan.FromSeconds(1);

        // Driver Auto runs TLS → TryDisconnect → Legacy inside one Connect(), with outer budget 2×ConnectTimeout
        // (S7CommPlusClient.ConnectCoreAsync). Shorter DisconnectTimeout and slightly longer ConnectTimeout
        // leave room for Legacy after a slow TLS attempt.
        var connectTimeout = driverMode == S7CommPlusSecurityMode.Auto
            ? userTimeout + disconnectTimeout
            : userTimeout;

        return new(new S7CommPlusClientOptions
        {
            Address = settings.Host.Trim(),
            Port = settings.Port,
            ConnectTimeout = connectTimeout,
            RequestTimeout = userTimeout,
            DisconnectTimeout = disconnectTimeout,
            BrowseTimeout = TimeSpan.FromMinutes(Math.Max(1, settings.BrowseTimeoutMinutes)),
            SecurityMode = driverMode,
            TlsBackend = S7CommPlusTlsBackend.BouncyCastle,
            LegacyPublicKeyFallbackEnabled = true,
            AutoReconnect = false,
            Password = password ?? string.Empty,
            WriteEnabled = true
        });
    }

    private async Task AuthenticateIfNeededAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        if (username == null || _client == null)
        {
            return;
        }

        await _client.LegitimateAsync(
            password ?? string.Empty,
            username: username,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _plcOperationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync(CancellationToken.None, swallowAllErrors: true).ConfigureAwait(false);
        }
        finally
        {
            _plcOperationLock.Release();
        }
    }

    public async Task DisconnectForShutdownAsync()
    {
        try
        {
            await _plcOperationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await DisconnectCoreAsync(CancellationToken.None, swallowAllErrors: true).ConfigureAwait(false);
            }
            finally
            {
                _plcOperationLock.Release();
            }
        }
        catch
        {
            // 退出阶段尽力断开
        }
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken, bool swallowAllErrors = false)
    {
        CancelConnectionOperations();

        if (_client == null)
        {
            DisposeConnectionCancellation();
            return;
        }

        var client = _client;
        _client = null;

        try
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (swallowAllErrors || IsExpectedShutdownException(ex))
                {
                    // 停止轮询/断开/退出时驱动 TLS 读线程常取消
                }
            }
        }
        finally
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (swallowAllErrors || IsExpectedShutdownException(ex))
            {
                // 同上
            }

            DisposeConnectionCancellation();
        }
    }

    private void DisposeConnectionCancellation()
    {
        _connectionCts?.Dispose();
        _connectionCts = null;
    }

    private static bool IsExpectedShutdownException(Exception ex) =>
        ex is OperationCanceledException or TimeoutException;

    public async Task<IReadOnlyList<VariableInfo>> BrowseAllVariablesAsync(
        IProgress<PlcBrowseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _connectionCts?.Token ?? CancellationToken.None);
        var operationToken = linkedCts.Token;

        await _plcOperationLock.WaitAsync(operationToken).ConfigureAwait(false);
        try
        {
            var client = RequireConnectedClient();

            progress?.Report(new PlcBrowseProgress { Stage = "正在浏览符号变量..." });

            var variables = await client.BrowseAsync(cancellationToken: operationToken).ConfigureAwait(false)
                ?? [];

            progress?.Report(new PlcBrowseProgress { Stage = $"已获取 {variables.Count} 个变量" });

            return variables
                .Where(static variable => variable != null)
                .Select(MapVariable)
                .Where(static variable => !string.IsNullOrWhiteSpace(variable.Symbol))
                .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _plcOperationLock.Release();
        }
    }

    public async Task<IReadOnlyList<VariableReadResult>> ReadVariablesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _connectionCts?.Token ?? CancellationToken.None);
        var operationToken = linkedCts.Token;

        try
        {
            await _plcOperationLock.WaitAsync(operationToken).ConfigureAwait(false);
            try
            {
                return await ReadVariablesCoreAsync(symbols, operationToken).ConfigureAwait(false);
            }
            finally
            {
                _plcOperationLock.Release();
            }
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private const int PlcReadBatchSize = 800;

    private async Task<IReadOnlyList<VariableReadResult>> ReadVariablesCoreAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
    {
        var client = RequireConnectedClient();
        var symbolList = symbols
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (symbolList.Count == 0)
        {
            return [];
        }

        // 分批读取：避免 50k 单次 GetTags/Read 产生 LOH 大字典/大列表（单批 800，50k≈63批）
        var results = new List<VariableReadResult>(symbolList.Count);
        for (var offset = 0; offset < symbolList.Count; offset += PlcReadBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(PlcReadBatchSize, symbolList.Count - offset);
            var batchSymbols = symbolList.GetRange(offset, count);

            var tagsBySymbol = await client.GetTagsBySymbolsAsync(batchSymbols, cancellationToken)
                .ConfigureAwait(false);
            // 统一大小写不敏感：驱动返回的字典可能为 Ordinal，包一层避免大小写重复符号漏匹配
            var tagsBySymbolLookup = tagsBySymbol.ToDictionary(
                kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, S7CommPlusTagReadResult> readItems;
            var tags = tagsBySymbol.Values.ToList();
            if (tags.Count == 0)
            {
                readItems = new Dictionary<string, S7CommPlusTagReadResult>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var batch = await client.ReadAsync(tags, cancellationToken).ConfigureAwait(false);
                readItems = batch.Items
                    .Where(static item => item.Tag != null && !string.IsNullOrWhiteSpace(item.Tag.Name))
                    .ToDictionary(item => item.Tag!.Name, item => item, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var symbol in batchSymbols)
            {
                if (!tagsBySymbolLookup.TryGetValue(symbol, out var tag))
                {
                    results.Add(new VariableReadResult
                    {
                        Symbol = symbol,
                        Quality = VariableReadQuality.Bad,
                        ErrorMessage = "符号未找到"
                    });
                    continue;
                }

                if (!readItems.TryGetValue(symbol, out var item))
                {
                    results.Add(new VariableReadResult
                    {
                        Symbol = symbol,
                        Quality = VariableReadQuality.Bad,
                        ErrorMessage = "读取结果缺失"
                    });
                    continue;
                }

                if (!item.IsSuccess)
                {
                    results.Add(new VariableReadResult
                    {
                        Symbol = symbol,
                        Quality = VariableReadQuality.Bad,
                        ErrorMessage = item.ItemError == 0
                            ? "读取失败"
                            : $"读取失败（错误码 0x{item.ItemError:X8}）"
                    });
                    continue;
                }

                var value = PlcTagValueHelper.GetValue(tag);
                results.Add(new VariableReadResult
                {
                    Symbol = symbol,
                    Value = value,
                    DisplayValue = FormatDisplayValue(value),
                    Quality = VariableReadQuality.Good
                });
            }
        }

        return results;
    }

    public async Task<VariableWriteResult> WriteVariableAsync(
        VariableWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _plcOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await WriteVariableCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _plcOperationLock.Release();
        }
    }

    private async Task<VariableWriteResult> WriteVariableCoreAsync(
        VariableWriteRequest request,
        CancellationToken cancellationToken)
    {
        var client = RequireConnectedClient();
        var symbol = request.Symbol.Trim();

        try
        {
            var tag = await client.GetTagBySymbolAsync(symbol, cancellationToken).ConfigureAwait(false);
            PlcTagValueHelper.SetValue(tag, request.Value);
            var writeResult = await client.WriteAsync([tag], cancellationToken).ConfigureAwait(false);
            var item = writeResult.Items.Count > 0
                ? writeResult.Items[0]
                : default;

            return new VariableWriteResult
            {
                Symbol = symbol,
                IsSuccess = item.IsSuccess,
                ErrorMessage = item.IsSuccess
                    ? null
                    : item.ItemError == 0
                        ? "写入失败"
                        : $"写入失败（错误码 0x{item.ItemError:X8}）"
            };
        }
        catch (Exception ex)
        {
            return new VariableWriteResult
            {
                Symbol = symbol,
                IsSuccess = false,
                ErrorMessage = ExceptionFormatter.Format(ex)
            };
        }
    }

    public Task<VariableWriteResult> ToggleBoolAsync(
        string symbol,
        bool currentValue,
        CancellationToken cancellationToken = default) =>
        WriteVariableAsync(
            new VariableWriteRequest
            {
                Symbol = symbol,
                Value = !currentValue
            },
            cancellationToken);

    public async ValueTask DisposeAsync() =>
        await DisconnectForShutdownAsync().ConfigureAwait(false);

    private S7CommPlusClient RequireConnectedClient()
    {
        if (_client == null || !_client.IsConnected)
        {
            throw new InvalidOperationException("未连接到 PLC。");
        }

        return _client;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatDisplayValue(object? value) =>
        value switch
        {
            null => null,
            bool boolean => VariableDisplayFormatter.FormatBool(boolean),
            _ => value.ToString()
        };

    private static VariableInfo MapVariable(VarInfo variable) =>
        new()
        {
            Symbol = variable.Name ?? string.Empty,
            DataType = SoftDataTypeFormatter.ToEnglishName(variable.Softdatatype),
            Address = variable.AccessSequence ?? string.Empty,
            SymbolCrc = variable.SymbolCrc,
            SoftdatatypeId = variable.Softdatatype,
            OptAddress = variable.OptAddress,
            OptBitoffset = variable.OptBitoffset,
            NonOptAddress = variable.NonOptAddress,
            NonOptBitoffset = variable.NonOptBitoffset,
            HmiVisible = variable.HmiVisible,
            HmiAccessible = variable.HmiAccessible,
            ArrayElementCount = variable.ArrayElementCount,
            ArrayDimensions = MapArrayDimensions(variable.ArrayDimensions)
        };

    private static IReadOnlyList<ArrayDimensionInfo> MapArrayDimensions(
        IReadOnlyList<S7CommPlusArrayDimension>? dimensions)
    {
        if (dimensions == null || dimensions.Count == 0)
        {
            return [];
        }

        return dimensions
            .Where(static dimension => dimension != null)
            .Select(dimension => new ArrayDimensionInfo
            {
                LowerBound = dimension.LowerBound,
                ElementCount = dimension.ElementCount,
                UpperBound = dimension.UpperBound
            })
            .ToList();
    }
}
