using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>采样映射：VariableReadResult → TrendSampleEntity（含 Quality 判定）。</summary>

public static class TrendSampleMapper
{
    public static TrendSampleEntity? TryCreate(
        int plcConnectionId,
        string symbol,
        int? itemId,
        DateTimeOffset timestamp,
        VariableReadResult result)
    {
        if (result.Value == null || string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var entity = new TrendSampleEntity
        {
            PlcConnectionId = plcConnectionId,
            Symbol = symbol.Trim(),
            ItemId = itemId,
            TimestampUtc = FormatUtc(timestamp),
            Quality = (int)result.Quality
        };

        if (result.Value is bool boolean)
        {
            entity.BoolValue = boolean;
            return entity;
        }

        if (result.Value is string text)
        {
            if (bool.TryParse(text, out var parsedBool)
                || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                || text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                entity.BoolValue = parsedBool || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                return entity;
            }
        }

        if (!TrendNumericExtractor.TryExtract(result, out var numeric))
        {
            return null;
        }

        entity.NumericValue = numeric;
        return entity;
    }

    public static TrendSamplePoint? TryToPoint(TrendSampleEntity entity)
    {
        if (!DateTimeOffset.TryParse(entity.TimestampUtc, out var timestamp))
        {
            return null;
        }

        timestamp = timestamp.ToUniversalTime();
        double value;
        if (entity.BoolValue is bool boolean)
        {
            value = boolean ? 1d : 0d;
        }
        else if (entity.NumericValue is double numeric)
        {
            value = numeric;
        }
        else
        {
            return null;
        }

        return new TrendSamplePoint
        {
            Timestamp = timestamp,
            Value = value,
            Quality = (VariableReadQuality)entity.Quality
        };
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");
}
