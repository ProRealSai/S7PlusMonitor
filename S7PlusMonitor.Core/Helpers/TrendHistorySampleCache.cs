using S7PlusMonitor.Core.Data.Entities;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>趋势图历史视图专用：暂停时从 SQLite 增量读库缓存。</summary>
public class TrendHistorySampleCache
{
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, List<TrendSampleEntity>> _samplesByItemId = new();
    private string? _settingsFingerprint;

    public void Clear()
    {
        lock (_cacheLock)
        {
            _samplesByItemId.Clear();
            _settingsFingerprint = null;
        }
    }

    public bool MatchesSettings(TrendViewEntity settings)
    {
        lock (_cacheLock)
        {
            return _settingsFingerprint == BuildSettingsFingerprint(settings);
        }
    }

    public IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> CreateSnapshot()
    {
        lock (_cacheLock)
        {
            return _samplesByItemId.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TrendSampleEntity>)pair.Value.ToList());
        }
    }

    public void ReplaceFull(
        IReadOnlyDictionary<int, IReadOnlyList<TrendSampleEntity>> samplesByItemId,
        TrendViewEntity settings)
    {
        lock (_cacheLock)
        {
            _settingsFingerprint = BuildSettingsFingerprint(settings);
            _samplesByItemId.Clear();
            foreach (var (itemId, samples) in samplesByItemId)
            {
                _samplesByItemId[itemId] = samples.ToList();
            }
        }
    }

    public string? GetMaxTimestampText(int itemId)
    {
        lock (_cacheLock)
        {
            if (!_samplesByItemId.TryGetValue(itemId, out var samples) || samples.Count == 0)
            {
                return null;
            }

            return samples[^1].TimestampUtc;
        }
    }

    public int TrimOlderThan(DateTimeOffset windowStartUtc)
    {
        var startText = FormatUtc(windowStartUtc);
        lock (_cacheLock)
        {
            var removed = 0;
            foreach (var samples in _samplesByItemId.Values)
            {
                removed += samples.RemoveAll(sample => string.CompareOrdinal(sample.TimestampUtc, startText) < 0);
            }

            return removed;
        }
    }

    public bool MergeNewSamples(int itemId, IReadOnlyList<TrendSampleEntity> newSamples)
    {
        if (newSamples.Count == 0)
        {
            return false;
        }

        lock (_cacheLock)
        {
            if (!_samplesByItemId.TryGetValue(itemId, out var samples))
            {
                samples = [];
                _samplesByItemId[itemId] = samples;
            }

            var lastText = samples.Count > 0 ? samples[^1].TimestampUtc : null;
            var appended = 0;
            foreach (var sample in newSamples)
            {
                if (lastText != null && string.CompareOrdinal(sample.TimestampUtc, lastText) <= 0)
                {
                    continue;
                }

                samples.Add(sample);
                lastText = sample.TimestampUtc;
                appended++;
            }

            return appended > 0;
        }
    }

    public static string BuildSettingsFingerprint(TrendViewEntity settings) =>
        string.Join('|',
            settings.Id,
            settings.XRangeMode,
            settings.LiveRollingWindowSeconds,
            settings.HistoryRollingWindowSeconds,
            settings.XRangeStartUtc ?? string.Empty,
            settings.XRangeEndUtc ?? string.Empty,
            settings.XRangeEndFollowsNow,
            settings.SampleIntervalMs,
            settings.LiveYAutoScale,
            settings.LiveYMin,
            settings.LiveYMax,
            settings.HistoryYAutoScale,
            settings.HistoryYMin,
            settings.HistoryYMax);

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");
}
