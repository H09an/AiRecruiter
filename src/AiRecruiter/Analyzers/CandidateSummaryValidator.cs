namespace AiRecruiter.Analyzers;

/// <summary>
/// Валидация сводки кандидата на старте: без непустой
/// <c>candidate_summary.md</c> программа не парсит, не анализирует и не
/// отправляет.
/// </summary>
internal static class CandidateSummaryValidator
{
    /// <summary>
    /// Возвращает непустую сводку или null, если сводка отсутствует или состоит
    /// только из пробелов и переводов строк; <paramref name="error"/> содержит
    /// понятное сообщение для лога.
    /// </summary>
    internal static string? Validate(string? summary, out string error)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            error = "Ошибка: candidate_summary.md отсутствует или пуст. "
                + "Сводка кандидата — единственный источник оценки; "
                + "заполните файл и повторите запуск. Парсинг, анализ и отправка не выполнялись.";
            return null;
        }

        error = string.Empty;
        return summary;
    }
}
