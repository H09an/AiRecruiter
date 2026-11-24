using AiRecruiter.Analyzers;
using AiRecruiter.Models;

namespace AiRecruiter.Tests;

/// <summary>
/// Контракт разбора ответа GigaChat: валидный ответ принимается, мусор отклоняется без исключений.
/// </summary>
public class JobAnalyzerDtoTests
{
    private const string Comment = "Подходит по стеку, но нет опыта нагрузочного тестирования.";

    [Theory]
    [InlineData("{\"score\": 7, \"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": \"8\", \"comment\": \"" + Comment + "\"}")]
    [InlineData("```json\n{\"score\": 7, \"comment\": \"" + Comment + "\"}\n```")]
    [InlineData("{\"score\": 7, \"comment\": \"" + Comment + "\",}")]
    [InlineData("﻿Вот оценка: {\"score\": 7, \"comment\": \"" + Comment + "\"} — готово")]
    [InlineData("{\"note\": \"черновик\"} {\"score\": 7, \"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": 7, \"comment\": \"" + Comment + "\", \"must_have_met\": [\"SQL\"], \"must_have_missing\": [], \"nice_to_have_missing\": [], \"english_required\": null, \"red_flags\": [], \"assumptions\": []}")]
    public void TryParseAndValidateDto_AcceptsValidResponse(string raw)
    {
        var ok = JobAnalyzer.TryParseAndValidateDto(raw, out var dto, out var reason);

        Assert.True(ok, reason);
        Assert.NotNull(dto);
        Assert.Equal(Comment, dto.Comment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("нет json")]
    [InlineData("{\"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": 0, \"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": 11, \"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": \"семь\", \"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": 7, \"comment\": \"коротко\"}")]
    [InlineData("{\"score\": 7.5, \"comment\": \"" + Comment + "\"}")]
    [InlineData("{\"score\": 7, \"comment\": \"" + Comment)]
    public void TryParseAndValidateDto_RejectsInvalidResponse(string raw)
    {
        var ok = JobAnalyzer.TryParseAndValidateDto(raw, out var dto, out var reason);

        Assert.False(ok);
        Assert.Null(dto);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("Oracle — будет плюсом", "Oracle")]
    [InlineData("Будет плюсом: Oracle", "Oracle")]
    public void ReclassifyDesirableMissing_MovesToNiceToHave(string requirements, string missingItem)
    {
        var vacancy = new Vacancy { Requirements = requirements };
        var result = new AnalysisResult { MustHaveMissing = new List<string> { missingItem } };

        JobAnalyzer.ReclassifyDesirableMissing(result, vacancy);

        Assert.DoesNotContain(missingItem, result.MustHaveMissing);
        Assert.Contains(missingItem, result.NiceToHaveMissing);
    }

    [Theory]
    [InlineData("Oracle — обязательно", "Oracle")]
    [InlineData("Oracle", "Oracle")]
    public void ReclassifyDesirableMissing_KeepsInMustHaveMissing(string requirements, string missingItem)
    {
        var vacancy = new Vacancy { Requirements = requirements };
        var result = new AnalysisResult { MustHaveMissing = new List<string> { missingItem } };

        JobAnalyzer.ReclassifyDesirableMissing(result, vacancy);

        Assert.Contains(missingItem, result.MustHaveMissing);
        Assert.DoesNotContain(missingItem, result.NiceToHaveMissing);
    }

    [Fact]
    public void GetContradictions_NoProblems_WhenMissingEmptyAndRedFlagsPresent()
    {
        var result = new AnalysisResult
        {
            Score = 7,
            MustHaveMissing = new List<string>(),
            RedFlags = new List<string> { "some red flag" },
            MustHaveScore = 7,
        };

        var contradictions = JobAnalyzer.GetContradictions(result);

        Assert.Empty(contradictions);
    }

    [Fact]
    public void GetContradictions_Contradiction_WhenScoreLowAndMissingEmpty()
    {
        var result = new AnalysisResult
        {
            Score = 4,
            MustHaveMissing = new List<string>(),
            MustHaveScore = 6,
        };

        var contradictions = JobAnalyzer.GetContradictions(result);

        Assert.NotEmpty(contradictions);
        Assert.Contains("Противоречие: final_score=4, но must_have_missing_count=0", contradictions);
    }

    [Fact]
    public void GetContradictions_Contradiction_WhenMissingPresentAndScoreHigh()
    {
        var result = new AnalysisResult
        {
            Score = 8,
            MustHaveMissing = new List<string> { "Oracle" },
            MustHaveScore = 9,
        };

        var contradictions = JobAnalyzer.GetContradictions(result);

        Assert.NotEmpty(contradictions);
        Assert.Contains("Противоречие: final_score=8, но must_have_missing_count=1", contradictions);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsCommentRules()
    {
        var systemPrompt = JobAnalyzer.BuildSystemPrompt();

        Assert.Contains("comment: называй навыки из must_have_met, must_have_missing и nice_to_have_missing.", systemPrompt);
        Assert.Contains("comment: при наличии СТЕК ПРОЕКТА назови 1–2 технологии оттуда, иначе — из НАВЫКИ.", systemPrompt);
        Assert.Contains("comment: не перечисляй весь список.", systemPrompt);
    }

    [Fact]
    public void BuildRepairMessages_InstructionInSystem_RawResponseInUser()
    {
        var (system, user) = JobAnalyzer.BuildRepairMessages("ПРЕДЫДУЩИЙ-JSON");

        Assert.Contains("не прошёл валидацию JSON-схемы", system);
        Assert.Contains("\"score\": <1..10>", system);
        Assert.StartsWith("ПРЕДЫДУЩИЙ ОТВЕТ:", user);
        Assert.EndsWith("ПРЕДЫДУЩИЙ-JSON", user);
    }

    [Fact]
    public void Defaults_WithoutConfig_ModelAndMaxTokens()
    {
        var analyzer = new JobAnalyzer("key", "сводка кандидата");

        Assert.Equal("GigaChat-2-Pro", analyzer.Model);
        Assert.Equal(1200, analyzer.MaxTokens);
    }

    private static Vacancy VacancyWith(string description, string requirements = "", string techStack = "") =>
        new()
        {
            Title = "Тестировщик",
            Company = "Компания",
            Link = "https://hh.ru/vacancy/1",
            Description = description,
            Requirements = requirements,
            TechStack = techStack,
        };

    private static string Repeat(char c, int count) => new(c, count);

    [Fact]
    public void BuildUserPrompt_Description6000_SingleFullDescriptionBlock()
    {
        var analyzer = new JobAnalyzer("key", "сводка");
        var vacancy = VacancyWith(Repeat('a', 6000));

        var user = analyzer.BuildUserPrompt(vacancy);

        Assert.Contains("=== ПОЛНОЕ ОПИСАНИЕ ===", user);
        Assert.DoesNotContain("=== ТРЕБОВАНИЯ ===", user);
        Assert.DoesNotContain("=== СТЕК ПРОЕКТА ===", user);
        Assert.Contains(Repeat('a', 6000), user);
    }

    [Fact]
    public void BuildUserPrompt_Description6001WithSections_SectionsBlock()
    {
        var analyzer = new JobAnalyzer("key", "сводка");
        var vacancy = VacancyWith(Repeat('a', 6001), requirements: "Ручное тестирование");

        var user = analyzer.BuildUserPrompt(vacancy);

        Assert.DoesNotContain("=== ПОЛНОЕ ОПИСАНИЕ ===", user);
        Assert.Contains("=== ТРЕБОВАНИЯ ===", user);
        Assert.Contains("Ручное тестирование", user);
    }

    [Fact]
    public void BuildUserPrompt_Description6001WithoutSections_First6000Only()
    {
        var analyzer = new JobAnalyzer("key", "сводка");
        var description = Repeat('a', 6000) + "ENDMARK";
        var vacancy = VacancyWith(description);

        var user = analyzer.BuildUserPrompt(vacancy);

        Assert.Contains("=== ПОЛНОЕ ОПИСАНИЕ ===", user);
        Assert.Contains(Repeat('a', 6000), user);
        Assert.DoesNotContain("ENDMARK", user);
    }

    [Fact]
    public void BuildUserPrompt_EmptyDescriptionWithSections_SectionsAndTechStack()
    {
        var analyzer = new JobAnalyzer("key", "сводка");
        var vacancy = VacancyWith("", requirements: "Ручное тестирование", techStack: "Docker");

        var user = analyzer.BuildUserPrompt(vacancy);

        Assert.DoesNotContain("=== ПОЛНОЕ ОПИСАНИЕ ===", user);
        Assert.Contains("=== ТРЕБОВАНИЯ ===", user);
        Assert.Contains("=== СТЕК ПРОЕКТА ===", user);
        Assert.Contains("Docker", user);
    }

    [Fact]
    public async Task AnalyzeVacancyAsync_NoDescriptionNoSections_ReturnsZeroWithoutNetwork()
    {
        var analyzer = new JobAnalyzer("key", "сводка");
        var vacancy = VacancyWith("");

        var result = await analyzer.AnalyzeVacancyAsync(vacancy);

        Assert.Equal(0, result.Score);
        Assert.Contains("не загружены", result.Comment);
    }

    [Fact]
    public void BuildUserPrompt_Summary4001_First4000CharsOnly()
    {
        var summary = Repeat('s', 4001) + "TAILMARK";
        var analyzer = new JobAnalyzer("key", summary);
        var vacancy = VacancyWith("Описание вакансии для проверки обрезки сводки; данные вымышлены и нужны только для тестов.");

        var user = analyzer.BuildUserPrompt(vacancy);

        Assert.Contains(Repeat('s', 4000), user);
        Assert.DoesNotContain("TAILMARK", user);
    }

    [Fact]
    public void ReclassifyDesirableMissing_DesirableMarkerInOtherSection_DoesNotMove()
    {
        var vacancy = new Vacancy
        {
            Requirements = "Oracle",
            Description = "Желательно: Oracle\nОпыт с базами данных.",
        };
        var result = new AnalysisResult { MustHaveMissing = new List<string> { "Oracle" } };

        JobAnalyzer.ReclassifyDesirableMissing(result, vacancy);

        Assert.Contains("Oracle", result.MustHaveMissing);
        Assert.DoesNotContain("Oracle", result.NiceToHaveMissing);
    }
}
