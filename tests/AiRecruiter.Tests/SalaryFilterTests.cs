using AiRecruiter.Filters;

namespace AiRecruiter.Tests;

/// <summary>
/// Разбор строки зарплаты hh.ru и решение предфильтра по минимальной зарплате.
/// </summary>
public class SalaryFilterTests
{
    [Fact]
    public void TryParse_SimpleLowerBound_ReturnsValue()
    {
        var salary = "70 000 ₽ за месяц, на руки";

        Assert.Equal(70000, SalaryFilter.TryParseLowerBoundRub(salary));
        Assert.True(SalaryFilter.ShouldSkip(salary, 70001));
        Assert.False(SalaryFilter.ShouldSkip(salary, 70000));
    }

    [Fact]
    public void TryParse_WithPrefixFrom_ReturnsValue()
    {
        Assert.Equal(60000, SalaryFilter.TryParseLowerBoundRub("от 60 000 ₽ за месяц на руки"));
    }

    [Fact]
    public void TryParse_Range_ReturnsLowerBound()
    {
        Assert.Equal(60000, SalaryFilter.TryParseLowerBoundRub("60 000 – 90 000 ₽"));
    }

    [Fact]
    public void TryParse_UpTo_ReturnsNull()
    {
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("до 100 000 ₽"));
        Assert.False(SalaryFilter.ShouldSkip("до 100 000 ₽", 70000));
    }

    [Fact]
    public void TryParse_NotSpecified_ReturnsNull()
    {
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("не указана"));
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("Не указана"));
    }

    [Fact]
    public void TryParse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(SalaryFilter.TryParseLowerBoundRub(null));
        Assert.Null(SalaryFilter.TryParseLowerBoundRub(string.Empty));
    }

    [Fact]
    public void TryParse_NbspAndNarrowSpace_ReturnsValue()
    {
        Assert.Equal(70000, SalaryFilter.TryParseLowerBoundRub("70\u00A0000\u202F₽"));
    }

    [Fact]
    public void TryParse_ForeignCurrency_ReturnsNull()
    {
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("$100 000"));
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("100 000 EUR"));
    }

    [Fact]
    public void TryParse_HourlyOrShift_ReturnsNull()
    {
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("1 000 ₽ в час"));
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("2 000 ₽ за смену"));
    }

    [Fact]
    public void ShouldSkip_DisabledFilter_ReturnsFalse()
    {
        Assert.False(SalaryFilter.ShouldSkip("70 000 ₽", 0));
    }

    [Fact]
    public void ShouldSkip_Boundary_ComparisonIsStrict()
    {
        Assert.Equal(69999, SalaryFilter.TryParseLowerBoundRub("69 999 ₽"));
        Assert.True(SalaryFilter.ShouldSkip("69 999 ₽", 70000));
        Assert.False(SalaryFilter.ShouldSkip("70 000 ₽", 70000));
    }

    [Fact]
    public void TryParse_WordContainingUpTo_ReturnsValue()
    {
        Assert.Equal(100000, SalaryFilter.TryParseLowerBoundRub("доход 100 000 ₽"));
    }

    [Fact]
    public void TryParse_GrossSalary_ReturnsValue()
    {
        Assert.Equal(150000, SalaryFilter.TryParseLowerBoundRub("150 000 ₽ за месяц до вычета налогов"));
    }

    [Fact]
    public void TryParse_Overflow_ReturnsNull()
    {
        Assert.Null(SalaryFilter.TryParseLowerBoundRub("2 500 000 000 ₽"));
    }

    [Fact]
    public void TryParse_DoesNotGlueAdjacentNumbers()
    {
        Assert.Equal(100000, SalaryFilter.TryParseLowerBoundRub("100 000 2 раза в неделю, оплата в рублях"));
        Assert.Equal(70000, SalaryFilter.TryParseLowerBoundRub("70000 ₽"));
        Assert.Equal(1000000, SalaryFilter.TryParseLowerBoundRub("1 000 000 ₽"));
    }
}
