using AiRecruiter.Analyzers;

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
}
