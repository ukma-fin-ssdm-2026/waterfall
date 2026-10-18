namespace waterfall.Services.Common;

public record SalaryRange(decimal? Min, decimal? Max);

public static class SalaryRangeParser
{
    public static SalaryRange? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parts = raw.Split('-');
        if (parts.Length == 2)
        {
            var minStr = parts[0].Replace("$", "").Trim();
            var maxStr = parts[1].Replace("$", "").Trim();

            if (decimal.TryParse(minStr, out var min) && decimal.TryParse(maxStr, out var max))
            {
                return new SalaryRange(min, max);
            }
        }

        return null;
    }
}
