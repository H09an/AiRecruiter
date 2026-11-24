using AiRecruiter.Storage;

namespace AiRecruiter.Tests;

/// <summary>
/// Список статусов — полный и в одном месте; seen — legacy.
/// </summary>
public sealed class VacancyStatusTests
{
    [Fact]
    public void All_ContainsEveryStatusExactlyOnce()
    {
        var expected = new[]
        {
            VacancyStatus.New,
            VacancyStatus.Seen,
            VacancyStatus.FilteredStopword,
            VacancyStatus.FilteredSalary,
            VacancyStatus.AnalyzedLow,
            VacancyStatus.AnalyzedOk,
            VacancyStatus.Sent,
            VacancyStatus.SendFailed,
            VacancyStatus.AnalysisFailed,
            VacancyStatus.AnalysisAbandoned,
        };

        Assert.Equal(expected, VacancyStatus.All);
        Assert.Equal(expected.Length, expected.Distinct().Count());
    }

    [Fact]
    public void Abandoned_IsTerminalValue()
    {
        Assert.Equal("analysis_abandoned", VacancyStatus.AnalysisAbandoned);
    }
}
