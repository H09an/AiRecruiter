using AiRecruiter.Parsers;

namespace AiRecruiter.Tests;

/// <summary>
/// Регрессия парсера на сохранённых страницах hh.ru (замена ручного --parse-samples).
/// Если hh.ru сменит вёрстку — обновите sample и ожидания.
/// </summary>
public class HhParserTests
{
    [Theory]
    [InlineData("sample_5_qa_sursk_xoxwe.html", 348, 409, 249, 1485, 7, 0, "70 000 ₽ за месяц, на руки")]
    [InlineData("sample_9_qa_itl_consulting.html", 2119, 687, 470, 3992, 2, 0, "не указана")]
    [InlineData("sample_10_qa_gear_games.html", 766, 0, 662, 2407, 8, 212, "от 60 000")]
    [InlineData("sample_11_qa_middle_senior_moscow.html", 418, 557, 563, 0, 7, 0)]
    [InlineData("sample_12_manual_qa_moscow.html", 516, 397, 0)]
    [InlineData("sample_13_qa_intern_modulbank.html", 335, 0, 490)]
    public async Task ParseVacancyHtml_ExtractsCorrectFields(
        string file, int requirements, int responsibilities, int conditions,
        int descriptionLength = 0, int keySkillsCount = 0, int techStackLength = 0,
        string? expectedSalary = null)
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", file));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, file);

        Assert.Equal(requirements, vacancy.Requirements.Length);
        Assert.Equal(responsibilities, vacancy.Responsibilities.Length);
        Assert.Equal(conditions, vacancy.Conditions.Length);

        if (descriptionLength > 0)
        {
            Assert.Equal(descriptionLength, vacancy.Description.Length);
        }

        Assert.Equal(keySkillsCount, vacancy.KeySkills.Count);
        Assert.Equal(techStackLength, vacancy.TechStack.Length);

        if (!string.IsNullOrEmpty(expectedSalary))
        {
            Assert.StartsWith(expectedSalary, vacancy.Salary);
        }
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample10_TechStackContainsCucumberAndUnity()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_10_qa_gear_games.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "sample_10_qa_gear_games.html");

        Assert.Equal(212, vacancy.TechStack.Length);
        Assert.Contains("Cucumber", vacancy.TechStack);
        Assert.Contains("Unity", vacancy.TechStack);
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample5And9_TechStackIsEmpty()
    {
        var html5 = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_5_qa_sursk_xoxwe.html"));
        var vacancy5 = await HhParser.ParseVacancyHtmlAsync(html5, "sample_5_qa_sursk_xoxwe.html");

        var html9 = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_9_qa_itl_consulting.html"));
        var vacancy9 = await HhParser.ParseVacancyHtmlAsync(html9, "sample_9_qa_itl_consulting.html");

        Assert.Equal(string.Empty, vacancy5.TechStack);
        Assert.Equal(string.Empty, vacancy9.TechStack);
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample5_SalaryIsCorrect()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_5_qa_sursk_xoxwe.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "sample_5_qa_sursk_xoxwe.html");

        Assert.Equal("70 000 ₽ за месяц, на руки", vacancy.Salary);
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample9_SalaryIsCorrect()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_9_qa_itl_consulting.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "sample_9_qa_itl_consulting.html");

        Assert.Equal("не указана", vacancy.Salary);
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample10_SalaryStartsWithExpected()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_10_qa_gear_games.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "sample_10_qa_gear_games.html");

        Assert.StartsWith("от 60 000", vacancy.Salary);
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample10_ResponsibilitiesEmptyConditionsNotEmpty()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_10_qa_gear_games.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "sample_10_qa_gear_games.html");

        Assert.Equal(0, vacancy.Responsibilities.Length);
        Assert.False(string.IsNullOrWhiteSpace(vacancy.Conditions));
    }

    [Fact]
    public async Task ParseVacancyHtml_Sample10_KeySkillsContainsPythonAndCSharp()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample_10_qa_gear_games.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "sample_10_qa_gear_games.html");

        Assert.Contains("Python", vacancy.KeySkills);
        Assert.Contains("C#", vacancy.KeySkills);
    }
}
