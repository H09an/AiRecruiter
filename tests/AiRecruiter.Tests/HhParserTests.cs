using AiRecruiter.Parsers;

namespace AiRecruiter.Tests;

/// <summary>
/// Регрессия парсера на сохранённых страницах hh.ru (замена ручного --parse-samples).
/// Если hh.ru сменит вёрстку — обновите sample и ожидания.
/// </summary>
public class HhParserTests
{
    [Theory]
    [InlineData("sample_5_qa_sursk_xoxwe.html", "Тестировщик (QA-инженер)", "Xoxwe IT")]
    [InlineData("sample_9_qa_itl_consulting.html", "QA Engineer (Тестировщик)", "ИТЛ Консалтинг")]
    [InlineData("archive/sample_1_qa_middle_senior_moscow.html", "QA Engineer (Middle+/Senior)", "Rambler&Co Портал \"Рамблер\"")]
    [InlineData("archive/sample_3_manual_qa_moscow.html", "Инженер по ручному тестированию", "RWB (Wildberries & Russ)")]
    [InlineData("archive/sample_8_qa_intern_modulbank.html", "Стажер - инженер по тестированию", "Модульбанк")]
    public async Task ParseVacancyHtml_ExtractsCoreFields(string file, string title, string company)
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", file));

        var vacancy = await HhParser.ParseVacancyHtmlAsync(html, file);

        Assert.Equal(title, vacancy.Title);
        Assert.Equal(company, vacancy.Company);
        Assert.True(vacancy.HasDescription, "Description не загружено");
        Assert.False(string.IsNullOrWhiteSpace(vacancy.Requirements), "Requirements пустые");
    }
}
