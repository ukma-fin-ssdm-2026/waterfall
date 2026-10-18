namespace waterfall.Services.Common;

public record SalaryRange(decimal? Min, decimal? Max);

public static class SalaryRangeParser
{
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

        return null;
    }

    private static bool TryCleanDecimal(string input, out decimal result)
    {
        var cleaned = input.Replace("$", "").Trim();
        return decimal.TryParse(cleaned, out result);
    }
}
