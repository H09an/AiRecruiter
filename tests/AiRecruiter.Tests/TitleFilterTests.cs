using AiRecruiter.Filters;

namespace AiRecruiter.Tests;

/// <summary>
/// Поиск стоп-слов в заголовке вакансии: совпадение по границам слова,
/// без учёта регистра, первое слово по порядку списка.
/// </summary>
public class TitleFilterTests
{
    private static readonly string[] SingleSenior = { "senior" };
    private static readonly string[] SingleLead = { "lead" };
    private static readonly string[] SingleInternship = { "стажировка" };
    private static readonly string[] SingleOneC = { "1с" };
    private static readonly string[] SingleTimlid = { "тимлид" };
    private static readonly string[] EmptyWords = Array.Empty<string>();
    private static readonly string[] WhitespaceWords = { " ", "   ", "lead" };
    private static readonly string[] InternBeforeJunior = { "trainee", "intern", "junior" };
    private static readonly string[] StopWords = { "senior", "lead", "intern" };

    [Fact]
    public void ExactMatch_ReturnsWord()
    {
        Assert.Equal("senior", TitleFilter.FindExcludedWord("Senior QA engineer", SingleSenior));
    }

    [Fact]
    public void CaseInsensitive_ReturnsWord()
    {
        Assert.Equal("тимлид", TitleFilter.FindExcludedWord("Тимлид команды тестирования", SingleTimlid));
    }

    [Fact]
    public void WordBoundary_SeniorityNotMatched()
    {
        Assert.Null(TitleFilter.FindExcludedWord("Seniority check title", SingleSenior));
    }

    [Fact]
    public void WordBoundary_LeaderNotMatched()
    {
        Assert.Null(TitleFilter.FindExcludedWord("Team Leader of QA", SingleLead));
    }

    [Fact]
    public void CyrillicSuffix_DigitsMatched()
    {
        Assert.Equal("1с", TitleFilter.FindExcludedWord("Разработчик 1С", SingleOneC));
    }

    [Fact]
    public void CyrillicWord_Matched()
    {
        Assert.Equal("стажировка", TitleFilter.FindExcludedWord("Стажировка в отделе QA", SingleInternship));
    }

    [Fact]
    public void EmptyWords_ReturnsNull()
    {
        Assert.Null(TitleFilter.FindExcludedWord("Senior QA", EmptyWords));
    }

    [Fact]
    public void NullTitle_ReturnsNull()
    {
        Assert.Null(TitleFilter.FindExcludedWord(null, SingleSenior));
    }

    [Fact]
    public void EmptyTitle_ReturnsNull()
    {
        Assert.Null(TitleFilter.FindExcludedWord(string.Empty, SingleSenior));
    }

    [Fact]
    public void NullOrWhitespaceWords_Skipped()
    {
        Assert.Equal("lead", TitleFilter.FindExcludedWord("QA lead", WhitespaceWords));
    }

    [Fact]
    public void MultipleWords_ReturnsFirstByListOrder()
    {
        Assert.Equal("intern", TitleFilter.FindExcludedWord("Intern / Junior QA", InternBeforeJunior));
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        Assert.Null(TitleFilter.FindExcludedWord("Middle QA engineer", StopWords));
    }
}
