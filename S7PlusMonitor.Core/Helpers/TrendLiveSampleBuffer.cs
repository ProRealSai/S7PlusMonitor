using S7PlusMonitor.Core.Data.Entities;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>趋势图实时视图内存缓冲：按轮询频率累积，滚动窗口裁剪。</summary>
public sealed class TrendLiveSampleBuffer
{
    private readonly object _bufferLock = new();
    private readonly Dictionary<int, List<TrendSampleEntity>> _samplesByItemId = new();

    public void Clear()
    {
        lock (_bufferLock)
        {
            _samplesByItemId.Clear();
        }
    }

    public void RemoveItem(int itemId)
    {
        lock (_bufferLock)
        {
            _samplesByItemId.Remove(itemId);
        }
    }

    public void Append(int itemId, TrendSampleEntity sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_bufferLock)
        {
            if (!_samplesByItemId.TryGetValue(itemId, out var samples))
            {
                samples = [];
                _samplesByItemId[itemId] = samples;
            }

            if (samples.Count > 0
                && string.CompareOrdinal(sample.TimestampUtc, samples[^1].TimestampUtc) <= 0)
            {
                return;
            }

            samples.Add(sample);
            // 硬上限：防止滚动窗口外 + 极端频率下无限增长（500ms * 300s = 600）
            var overflow = samples.Count - AppConstants.MaxLiveSamplesPerItem;
            if (overflow > 0)
            {
                samples.RemoveRange(0, overflow);
            }
        }
    }

    public int TrimOlderThan(DateTimeOffset windowStartUtc)
    {
        var startText = FormatUtc(windowStartUtc);
        lock (_bufferLock)
        {
            var removed = 0;
            foreach (var samples in _samplesByItemId.Values)
            {
                removed += samples.RemoveAll(sample =>
                    string.CompareOrdinal(sample.TimestampUtc, startText) < 0);
                // 二次硬上限裁剪：窗口外已删仍超 600 则截头
                var overflow = samples.Count - AppConstants.MaxLiveSamplesPerItem;
                if (overflow > 0)
                {
                    samples.RemoveRange(0, overflow);
                    removed += overflow;
                }
            }

            return removed;
        }
    }

    public IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> CreateSnapshot()
    {
        lock (_bufferLock)
        {
            return _samplesByItemId.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TrendSampleEntity>)pair.Value.ToList());
        }
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");
}
