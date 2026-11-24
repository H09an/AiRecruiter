using AiRecruiter.Analyzers;

namespace AiRecruiter.Tests;

/// <summary>
/// Валидация сводки кандидата на старте: без непустой строки программа
/// останавливается до парсинга, анализа и отправки.
/// </summary>
public class CandidateSummaryValidatorTests
{
    [Fact]
    public void Validate_Null_ReturnsNullWithError()
    {
        var result = CandidateSummaryValidator.Validate(null, out var error);

        Assert.Null(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Validate_Empty_ReturnsNullWithError()
    {
        var result = CandidateSummaryValidator.Validate(string.Empty, out var error);

        Assert.Null(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Validate_Whitespace_ReturnsNullWithError()
    {
        var result = CandidateSummaryValidator.Validate("   ", out var error);

        Assert.Null(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Validate_LinesAndTabs_ReturnsNullWithError()
    {
        var result = CandidateSummaryValidator.Validate("\n\t", out var error);

        Assert.Null(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Validate_NonEmpty_ReturnsSameValueWithoutError()
    {
        const string summary = "QA, Python, SQL";

        var result = CandidateSummaryValidator.Validate(summary, out var error);

        Assert.Equal(summary, result);
        Assert.Equal(string.Empty, error);
    }
}
