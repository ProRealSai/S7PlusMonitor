using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S7PlusMonitor.Core;
using S7PlusMonitor.Core.Data;
using S7PlusMonitor.Core.Export;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;
using S7PlusMonitor.Core.Services;
using S7PlusMonitor.Services;

namespace S7PlusMonitor.ViewModels;

public partial class BrowseViewModel : ObservableObject, IAppShutdownParticipant
{
    private const int FilterDebounceMs = 350;

    private readonly AppSessionViewModel _session;
    private readonly IPlcService _plcService;
    private readonly IMonitorSheetRepository _monitorSheetRepository;
    private readonly IPlcConnectionRepository _connectionRepository;
    private readonly IAppSettingsRepository _appSettings;
    private readonly IVariableListExcelExporter _excelExporter;
    private List<VariableInfo> _cachedBrowseResults = [];
    private List<VariableListItemViewModel> _cachedVmItems = [];
    private CancellationTokenSource? _filterDebounceCts;

    public BrowseViewModel(
        AppSessionViewModel session,
        IPlcService plcService,
        IMonitorSheetRepository monitorSheetRepository,
        IPlcConnectionRepository connectionRepository,
        IAppSettingsRepository appSettings,
        IVariableListExcelExporter excelExporter)
    {
        _session = session;
        _plcService = plcService;
        _monitorSheetRepository = monitorSheetRepository;
        _connectionRepository = connectionRepository;
        _appSettings = appSettings;
        _excelExporter = excelExporter;
        _session.Disconnected += OnSessionDisconnected;
        _session.SelectedConnectionApplied += OnSessionSelectedConnectionApplied;
        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    private void OnSessionDisconnected(object? sender, EventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        ClearVariables();
    }

    private void OnSessionSelectedConnectionApplied(object? sender, EventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _ = OnConnectionChangedAsync();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (e.PropertyName is nameof(AppSessionViewModel.IsBusy)
            or nameof(AppSessionViewModel.IsConnected)
            or nameof(AppSessionViewModel.HasActiveSession)
            or nameof(AppSessionViewModel.ActiveConnectionProfileName)
            or nameof(AppSessionViewModel.SelectedConnection))
        {
            NotifyDerivedProperties();
        }
    }

    public ObservableCollection<VariablePropertyItemViewModel> SelectedVariableProperties { get; } = [];

    [ObservableProperty]
    private ObservableCollection<VariableListItemViewModel> _filteredVariables = [];

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private int _variableCount;

    [ObservableProperty]
    private VariableListItemViewModel? _selectedVariable;

    [ObservableProperty]
    private bool _hasSelectedVariable;

    [ObservableProperty]
    private string _selectedVariableName = string.Empty;

    public bool HasNoSelectedVariable => !HasSelectedVariable;

    public bool IsBusy => _session.IsBusy;

    public bool IsConnected => _session.IsConnected;

    public bool CanBrowse => IsConnected && !IsBusy;

    public int TotalVariableCount => _cachedBrowseResults.Count;

    public bool CanExportExcel => IsConnected && TotalVariableCount > 0 && !IsBusy;

    public string ExportExcelToolTip => CanExportExcel
        ? "导出全部 PLC 变量到 Excel（含连接参数与密码明文）"
        : TotalVariableCount == 0
            ? "请先点击「浏览」加载变量列表"
            : "请先连接 PLC";

    public string PanelTitle => _session.HasActiveSession
        ? $"变量列表 · {_session.ActiveConnectionProfileName}"
        : "变量列表";

    partial void OnSelectedVariableChanged(VariableListItemViewModel? value) =>
        ApplySelectedVariable(value?.Source);

    partial void OnHasSelectedVariableChanged(bool value) =>
        OnPropertyChanged(nameof(HasNoSelectedVariable));

    partial void OnFilterTextChanged(string value) => ScheduleFilterRebuild();

    public Task BrowseVariablesAsync() => BrowseAsync();

    [RelayCommand(CanExecute = nameof(CanExportExcel))]
    private async Task ExportExcelAsync()
    {
        if (_session.IsShuttingDown || !CanExportExcel)
        {
            return;
        }

        var connectionId = await _session.GetActiveConnectionIdAsync().ConfigureAwait(true);
        if (connectionId is not int id || id <= 0)
        {
            _session.StatusMessage = "请先选择连接档案。";
            return;
        }

        var connection = await _connectionRepository.GetAsync(id).ConfigureAwait(true);
        if (connection == null)
        {
            _session.StatusMessage = "连接档案不存在。";
            return;
        }

        var confirmed = await ConfirmExcelExportAsync(_cachedBrowseResults.Count).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var safeName = SanitizeFileName(connection.Name);
        var suggestedFileName = $"S7PlusMonitor_{safeName}_变量列表_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var outputPath = await ExportFileSaveService.PickSavePathAsync(suggestedFileName).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        _session.IsBusy = true;
        _session.StatusMessage = "正在导出 Excel...";

        try
        {
            var password = await _connectionRepository.GetPasswordAsync(id).ConfigureAwait(true) ?? string.Empty;
            var pollIntervalMs = PollIntervalHelper.ParseOrDefault(
                await _appSettings.GetAsync(AppConstants.PollIntervalMsKey).ConfigureAwait(true));

            await _excelExporter.ExportAsync(
                connection,
                password,
                pollIntervalMs,
                _cachedBrowseResults,
                outputPath).ConfigureAwait(true);

            _session.StatusMessage = $"已导出 {_cachedBrowseResults.Count} 个变量至 {outputPath}";
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"导出失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            _session.IsBusy = false;
            NotifyDerivedProperties();
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        _session.IsBusy = true;
        _session.SuspendMonitoring();
        _session.StatusMessage = "正在浏览变量...";

        try
        {
            await LoadVariablesCoreAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_session.IsShuttingDown)
        {
            // 退出阶段取消，不提示
            return;
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsShuttingDown)
            {
                _session.StatusMessage = "浏览已取消";
            }
        }
        catch (Exception ex)
        {
            if (!_session.IsShuttingDown)
            {
                _session.StatusMessage = $"浏览失败：{ExceptionFormatter.Format(ex)}";
            }
        }
        finally
        {
            _session.IsBusy = false;
            if (!_session.IsShuttingDown)
            {
                await _session.ResumeMonitoringAsync().ConfigureAwait(true);
                NotifyDerivedProperties();
            }
        }
    }

    internal async Task AddVariableToMonitorSheetAsync(VariableInfo variable)
    {
        if (!IsConnected)
        {
            _session.StatusMessage = "请先连接 PLC。";
            return;
        }

        var connectionId = await _session.GetActiveConnectionIdAsync().ConfigureAwait(true);
        if (connectionId is not int id || id <= 0)
        {
            _session.StatusMessage = "请先在连接 Tab 保存并选择连接档案。";
            return;
        }

        _session.IsBusy = true;
        try
        {
            var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(connectionId.Value).ConfigureAwait(true);
            var items = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(true);
            if (items.Any(item => string.Equals(item.Symbol, variable.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                _session.StatusMessage = $"变量已在监视表中：{variable.Symbol}";
                return;
            }

            await _monitorSheetRepository.AddItemAsync(sheet.Id, variable.Symbol, variable.DataType)
                .ConfigureAwait(true);
            _session.StatusMessage = $"已添加到监视表：{variable.Symbol}";
            _session.NotifyMonitorSheetChanged();
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"添加失败：{ExceptionFormatter.Format(ex)}";
        }
        finally
        {
            _session.IsBusy = false;
            NotifyDerivedProperties();
        }
    }

    public async Task RefreshIfConnectedAsync()
    {
        if (IsConnected && _cachedBrowseResults.Count == 0)
        {
            await BrowseAsync().ConfigureAwait(false);
        }
    }

    private async Task OnConnectionChangedAsync()
    {
        ClearVariables();
        await RefreshIfConnectedAsync().ConfigureAwait(false);
    }

    private async Task LoadVariablesCoreAsync()
    {
        var progress = new Progress<PlcBrowseProgress>(report => _session.StatusMessage = report.Stage);
        _cachedBrowseResults = (await _plcService.BrowseAllVariablesAsync(progress).ConfigureAwait(true)).ToList();
        // 预构建 VM 并缓存 SearchText，避免过滤时重复 FormatHex/FormatArrayDimensions
        _cachedVmItems = new List<VariableListItemViewModel>(_cachedBrowseResults.Count);
        for (var i = 0; i < _cachedBrowseResults.Count; i++)
        {
            var variable = _cachedBrowseResults[i];
            _cachedVmItems.Add(new VariableListItemViewModel(i + 1, variable)
            {
                AddToMonitorHandler = () => AddVariableToMonitorSheetAsync(variable)
            });
        }
        _session.SetBrowsedSymbols(_cachedBrowseResults.Select(variable => variable.Symbol));
        await RebuildVariableListAsync().ConfigureAwait(true);
        _session.StatusMessage = $"浏览完成，共 {_cachedBrowseResults.Count} 个变量";
        await ValidateMonitorSheetSymbolsAsync().ConfigureAwait(true);
    }

    private async Task ValidateMonitorSheetSymbolsAsync()
    {
        if (_session.IsShuttingDown || !_session.IsConnectionLocked)
        {
            return;
        }

        if (_session.ConnectedConnectionId is not int connectionId || connectionId <= 0 || _cachedBrowseResults.Count == 0)
        {
            return;
        }

        var sheet = await _monitorSheetRepository.GetOrCreateSheetAsync(connectionId).ConfigureAwait(true);
        var items = await _monitorSheetRepository.GetItemsAsync(sheet.Id).ConfigureAwait(true);
        if (items.Count == 0)
        {
            return;
        }

        var browsed = new HashSet<string>(_cachedBrowseResults.Select(variable => variable.Symbol), StringComparer.OrdinalIgnoreCase);
        var stale = items.Where(item => !browsed.Contains(item.Symbol)).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        _session.StatusMessage = $"监视表中有 {stale.Count} 个变量在当前 PLC 中不存在";
        _session.NotifyMonitorSheetChanged();

        var preview = string.Join("\n", stale.Take(5).Select(item => item.Symbol));
        if (stale.Count > 5)
        {
            preview += $"\n…等共 {stale.Count} 个";
        }

        var remove = await UiDialog.ConfirmAsync(
            "监视变量不可用",
            $"以下变量在当前 PLC 中未找到，可能已更换设备或变量已删除：\n\n{preview}\n\n是否从监视表移除这些变量？",
            "移除",
            "保留").ConfigureAwait(true);

        if (!remove)
        {
            return;
        }

        foreach (var item in stale)
        {
            await _monitorSheetRepository.RemoveItemAsync(item.Id).ConfigureAwait(true);
        }

        _session.NotifyMonitorSheetChanged();
        _session.StatusMessage = $"已移除 {stale.Count} 个不可用变量";
    }

    private void ScheduleFilterRebuild()
    {
        _filterDebounceCts?.Cancel();
        _filterDebounceCts?.Dispose();
        _filterDebounceCts = new CancellationTokenSource();
        var token = _filterDebounceCts.Token;
        _ = RebuildVariableListDebouncedAsync(token);
    }

    private async Task RebuildVariableListDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(FilterDebounceMs, token).ConfigureAwait(false);
            await RebuildVariableListAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 防抖取消
        }
    }

    private async Task RebuildVariableListAsync()
    {
        if (_session.IsShuttingDown)
        {
            return;
        }

        if (_cachedBrowseResults.Count == 0)
        {
            VariableCount = 0;
            FilteredVariables = [];
            SelectedVariable = null;
            NotifyExportProperties();
            return;
        }

        var keyword = FilterText.Trim();
        var lowerKeyword = keyword.ToLowerInvariant();
        var matches = await Task.Run(() =>
        {
            if (keyword.Length == 0)
            {
                return _cachedVmItems;
            }

            var list = new List<VariableListItemViewModel>();
            foreach (var vm in _cachedVmItems)
            {
                if (vm.SearchText.Contains(lowerKeyword, StringComparison.Ordinal))
                {
                    list.Add(vm);
                }
            }

            return list;
        }).ConfigureAwait(false);

        // 重新编号并重建 Handler（避免闭包捕获旧索引）
        var items = new List<VariableListItemViewModel>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var source = matches[i].Source;
            var vm = new VariableListItemViewModel(i + 1, source)
            {
                AddToMonitorHandler = () => AddVariableToMonitorSheetAsync(source)
            };
            items.Add(vm);
        }

        await AppUiDispatcher.InvokeSafeAwaitAsync(() =>
        {
            if (_session.IsShuttingDown)
            {
                return;
            }

            SelectedVariable = null;
            VariableCount = matches.Count;
            FilteredVariables = items.Count == 0
                ? []
                : new ObservableCollection<VariableListItemViewModel>(items);

            _session.StatusMessage = keyword.Length == 0
                ? $"变量列表就绪，共 {matches.Count} 个"
                : $"匹配 {matches.Count} 个变量";
            NotifyExportProperties();
        }).ConfigureAwait(false);
    }

    private void ClearVariables()
    {
        var hadLargeList = _cachedBrowseResults.Count > 10_000;
        _filterDebounceCts?.Cancel();
        _cachedBrowseResults = [];
        _cachedVmItems = [];
        _session.ClearBrowsedSymbols();
        FilteredVariables = [];
        SelectedVariable = null;
        VariableCount = 0;
        NotifyExportProperties();
        if (hadLargeList || GC.GetTotalMemory(false) > 500_000_000)
        {
            // 仅大表或高内存时回收，避免频繁 STW
            GC.Collect(2, GCCollectionMode.Optimized, true, true);
        }
    }

    /// <summary>应用退出时取消未完成的过滤防抖任务。</summary>
    public void CancelPendingWork()
    {
        _filterDebounceCts?.Cancel();
        _filterDebounceCts?.Dispose();
        _filterDebounceCts = null;
    }

    public void PrepareForShutdown()
    {
        CancelPendingWork();
        _session.Disconnected -= OnSessionDisconnected;
        _session.SelectedConnectionApplied -= OnSessionSelectedConnectionApplied;
        _session.PropertyChanged -= OnSessionPropertyChanged;
    }

    private void ApplySelectedVariable(VariableInfo? variable)
    {
        SelectedVariableProperties.Clear();

        if (variable == null)
        {
            HasSelectedVariable = false;
            SelectedVariableName = string.Empty;
            return;
        }

        foreach (var property in VariablePropertyItemViewModel.FromVariable(variable))
        {
            SelectedVariableProperties.Add(property);
        }

        HasSelectedVariable = true;
        SelectedVariableName = variable.Symbol;
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(PanelTitle));
        NotifyExportProperties();
        BrowseCommand.NotifyCanExecuteChanged();
    }

    private void NotifyExportProperties()
    {
        OnPropertyChanged(nameof(TotalVariableCount));
        OnPropertyChanged(nameof(CanExportExcel));
        OnPropertyChanged(nameof(ExportExcelToolTip));
        ExportExcelCommand.NotifyCanExecuteChanged();
    }

    private static Task<bool> ConfirmExcelExportAsync(int variableCount) =>
        UiDialog.ConfirmAsync(
            "导出变量列表",
            $"将导出以下内容：\n· 连接名称、IP、端口、用户名\n· 密码（明文）\n· PLC 全部变量（{variableCount} 个）及符号属性\n\n⚠ 文件含敏感信息，请妥善保存。",
            "选择路径并导出",
            "取消");

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "连接" : sanitized;
    }

    private static bool MatchesFilter(VariableInfo variable, string keyword) =>
        ContainsKeyword(variable.Symbol, keyword)
        || ContainsKeyword(variable.DataType, keyword)
        || ContainsKeyword(variable.Address, keyword)
        || ContainsKeyword(VariableDisplayFormatter.FormatHex(variable.SymbolCrc), keyword)
        || ContainsKeyword(VariableDisplayFormatter.FormatArrayDimensions(variable.ArrayDimensions), keyword);

    private static bool ContainsKeyword(string? text, string keyword) =>
        (text ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
