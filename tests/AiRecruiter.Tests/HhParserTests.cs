using AiRecruiter.Parsers;

namespace AiRecruiter.Tests;

/// <summary>
/// Регрессия парсера на синтетических фикстурах с разметкой hh.ru.
/// Реальные страницы hh.ru в репозиторий не входят — проверяйте их локально
/// через --parse-samples и обновляйте ожидания при смене вёрстки.
/// </summary>
public class HhParserTests
{
    [Theory]
    [InlineData("full.html", 135, 142, 150, 542, 3, 34, "от 100 000")]
    [InlineData("no_salary.html", 135, 76, 0, 326, 2, 0)]
    [InlineData("no_skills.html", 111, 114, 110, 448, 0, 0, "150 000")]
    [InlineData("only_requirements.html", 112, 0, 0, 227, 0, 0)]
    [InlineData("plain_description.html", 222, 0, 0, 222, 0, 0)]
    [InlineData("conditions_only.html", 224, 0, 110, 224, 0, 0)]
    public async Task ParseVacancyHtml_ExtractsCorrectFields(
        string file, int requirements, int responsibilities, int conditions,
        int descriptionLength = 0, int keySkillsCount = 0, int techStackLength = 0,
        string? expectedSalary = null)
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
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
    public async Task FixtureFull_TechStackContainsPytestAndDocker()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "full.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "full.html");

        Assert.Equal(34, vacancy.TechStack.Length);
        Assert.Contains("pytest", vacancy.TechStack);
        Assert.Contains("Docker", vacancy.TechStack);
    }

    [Fact]
    public async Task FixturesWithoutTechStack_TechStackIsEmpty()
    {
        var htmlNoSalary = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "no_salary.html"));
        var vacancyNoSalary = await HhParser.ParseVacancyHtmlAsync(htmlNoSalary, "no_salary.html");

        var htmlOnlyRequirements = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "only_requirements.html"));
        var vacancyOnlyRequirements = await HhParser.ParseVacancyHtmlAsync(htmlOnlyRequirements, "only_requirements.html");

        Assert.Equal(string.Empty, vacancyNoSalary.TechStack);
        Assert.Equal(string.Empty, vacancyOnlyRequirements.TechStack);
    }

    [Fact]
    public async Task FixtureFull_SalaryIsCorrect()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "full.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "full.html");

        Assert.Equal("от 100 000 ₽ за месяц на руки", vacancy.Salary);
    }

    [Fact]
    public async Task FixtureNoSalary_SalaryIsNotSpecified()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "no_salary.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "no_salary.html");

        Assert.Equal("не указана", vacancy.Salary);
    }

    [Fact]
    public async Task FixtureNoSkills_SalaryStartsWithExpected()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "no_skills.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "no_skills.html");

        Assert.StartsWith("150 000", vacancy.Salary);
    }

    [Fact]
    public async Task FixtureConditionsOnly_ResponsibilitiesEmptyConditionsNotEmpty()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "conditions_only.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "conditions_only.html");

        Assert.Equal(0, vacancy.Responsibilities.Length);
        Assert.False(string.IsNullOrWhiteSpace(vacancy.Conditions));
    }

    [Fact]
    public async Task FixtureFull_KeySkillsContainsSqlAndPython()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "full.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "full.html");

        Assert.Contains("SQL", vacancy.KeySkills);
        Assert.Contains("Python", vacancy.KeySkills);
    }
}
