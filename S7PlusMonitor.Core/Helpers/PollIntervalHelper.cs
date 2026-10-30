namespace S7PlusMonitor.Core.Helpers;

/// <summary>轮询间隔校验与钳制（0–60000ms，有效性与 EffectiveMin 区分）。</summary>
public static class PollIntervalHelper
{
    /// <summary>解析文本为间隔，非法时回退到 <see cref="AppConstants.DefaultPollIntervalMs"/>。</summary>
    public static int ParseOrDefault(string? text) =>
        int.TryParse(text, out var intervalMs) && IsValid(intervalMs)
            ? intervalMs
            : AppConstants.DefaultPollIntervalMs;

    /// <summary>判断间隔是否在 [Min, Max] 合法区间。</summary>
    public static bool IsValid(int intervalMs) =>
        intervalMs is >= AppConstants.MinPollIntervalMs and <= AppConstants.MaxPollIntervalMs;

    /// <summary>钳制到 [Min, Max]。</summary>
    public static int Clamp(int intervalMs) =>
        Math.Clamp(intervalMs, AppConstants.MinPollIntervalMs, AppConstants.MaxPollIntervalMs);

    /// <summary>
    /// 引擎实际生效间隔（单一 10ms 下限）：配置 0（连续轮询）→ 10ms；配置值 &lt;10ms → 抬升到 10ms。
    /// </summary>
    public static int ResolveEffectiveIntervalMs(int configuredIntervalMs) =>
        Math.Max(AppConstants.EffectiveMinPollIntervalMs, configuredIntervalMs);
}
