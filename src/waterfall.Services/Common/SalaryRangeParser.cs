namespace waterfall.Services.Common;

public record SalaryRange(decimal? Min, decimal? Max);

public static class SalaryRangeParser
{
    public static SalaryRange? Parse(string? raw)
    {
        throw new NotImplementedException();
    }
}
