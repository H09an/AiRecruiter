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
}
