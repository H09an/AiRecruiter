namespace AiRecruiter.Storage;

/// <summary>
/// Статусы жизненного цикла вакансии в SQLite. Единственный источник строк
/// статусов: в коде используются константы, строковые литералы статусов
/// не допускаются (исключение — legacy-миграция UPDATE ... analysis_failed
/// в VacancyRepository.Initialize).
/// </summary>
internal static class VacancyStatus
{
    /// <summary>Legacy: встречается только у старых строк, новые с ним не создаются.</summary>
    public const string Seen = "seen";

    /// <summary>Стартовый статус записи после фильтров, до анализа.</summary>
    public const string New = "new";

    /// <summary>Заголовок содержит стоп-слово, до GigaChat.</summary>
    public const string FilteredStopword = "filtered_stopword";

    /// <summary>Зарплата ниже порога, до GigaChat.</summary>
    public const string FilteredSalary = "filtered_salary";

    /// <summary>Оценка ниже порога Analyzer:MinScore.</summary>
    public const string AnalyzedLow = "analyzed_low";

    /// <summary>Оценка не ниже порога, идёт в Telegram.</summary>
    public const string AnalyzedOk = "analyzed_ok";

    /// <summary>Отправлено в Telegram.</summary>
    public const string Sent = "sent";

    /// <summary>Ошибка отправки в Telegram.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>Ошибка анализа; подлежит переанализу, attempts растёт.</summary>
    public const string AnalysisFailed = "analysis_failed";

    /// <summary>Сдались после 3 неудачных попыток; нужен ручной разбор и сброс.</summary>
    public const string AnalysisAbandoned = "analysis_abandoned";

    /// <summary>Полный список статусов; Seen — legacy. Тест проверяет полноту.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        New,
        Seen,
        FilteredStopword,
        FilteredSalary,
        AnalyzedLow,
        AnalyzedOk,
        Sent,
        SendFailed,
        AnalysisFailed,
        AnalysisAbandoned,
    };
}
