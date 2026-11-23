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

    [Fact]
    public async Task FixtureEmployerRelative_ExtractsIdFromRelativeLink()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "employer_relative.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "employer_relative.html");

        Assert.Equal("1234567", vacancy.EmployerId);
    }

    [Fact]
    public async Task FixtureEmployerAbsolute_ExtractsIdFromAbsoluteLink()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "employer_absolute.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "employer_absolute.html");

        Assert.Equal("7654321", vacancy.EmployerId);
    }

    [Fact]
    public async Task FixtureEmployerDecoy_CompanyWithoutLink_KeepsEmployerIdEmpty()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "employer_decoy.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "employer_decoy.html");

        Assert.Equal(string.Empty, vacancy.EmployerId);
    }

    [Fact]
    public async Task FixtureOnlyRequirements_ExperienceIsEmpty()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "only_requirements.html"));
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "only_requirements.html");

        Assert.Equal(string.Empty, vacancy.Experience);
    }

    [Theory]
    [InlineData("Опыт работы: 1-3 года", "1–3 года")]
    [InlineData("не требуется", "не требуется")]
    [InlineData("Без опыта", "без опыта")]
    [InlineData("более 6 лет", "более 6 лет")]
    [InlineData("от 5 лет", "от 5 лет")]
    [InlineData("3—6 лет", "3–6 лет")]
    public async Task ParseVacancyHtml_ExperienceWhitelist_AcceptsValidForms(
        string source, string expected)
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="ru"><head><meta charset="utf-8"></head><body><main>
            <h1 data-qa="vacancy-title">Тестировщик</h1>
            <p data-qa="vacancy-experience">{source}</p>
            <div data-qa="vacancy-description">
              <p>Синтетическое описание достаточной длины для проверки белого
              списка опыта; данные вымышлены и нужны только для тестов.</p>
            </div>
            </main></body></html>
            """;
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "inline_experience.html");

        Assert.Equal(expected, vacancy.Experience);
    }

    [Fact]
    public async Task ParseVacancyHtml_ExperienceWhitelist_RejectsGarbage()
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="ru"><head><meta charset="utf-8"></head><body><main>
            <h1 data-qa="vacancy-title">Тестировщик</h1>
            <p data-qa="vacancy-experience">с системами управления тестированием.</p>
            <div data-qa="vacancy-description">
              <p>Синтетическое описание достаточной длины для проверки белого
              списка опыта; данные вымышлены и нужны только для тестов.</p>
            </div>
            </main></body></html>
            """;
        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, "inline_experience_garbage.html");

        Assert.Equal(string.Empty, vacancy.Experience);
    }
}
