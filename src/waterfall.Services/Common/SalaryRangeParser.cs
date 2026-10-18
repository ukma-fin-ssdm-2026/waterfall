namespace waterfall.Services.Common;

public record SalaryRange(decimal? Min, decimal? Max);

public static class SalaryRangeParser
{
    private const string PrefixDo = "до";

    public static SalaryRange? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parts = raw.Split('-');
        if (parts.Length == 2 &&
            TryCleanDecimal(parts[0], out var min) &&
            TryCleanDecimal(parts[1], out var max))
        {
            return new SalaryRange(min, max);
        }

        var trimmed = raw.Trim();
        if (trimmed.EndsWith('+') && TryCleanDecimal(trimmed.TrimEnd('+'), out var plusMin))
        {
            return new SalaryRange(plusMin, null);
        }

        if (trimmed.StartsWith(PrefixDo, StringComparison.OrdinalIgnoreCase) &&
            TryCleanDecimal(trimmed[PrefixDo.Length..], out var maxOnly))
        {
            return new SalaryRange(null, maxOnly);
        }

        return null;
    }

    private static bool TryCleanDecimal(string input, out decimal result)
    {
        var cleaned = input.Replace("$", "").Trim();
        return decimal.TryParse(cleaned, out result);
    }
}
