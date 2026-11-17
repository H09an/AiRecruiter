using System.Globalization;
using System.Text.RegularExpressions;

namespace AiRecruiter.Filters;

/// <summary>
/// Предфильтр вакансий по зарплате: разбирает строку зарплаты hh.ru и решает,
/// пропускать ли вакансию с нижней границей ниже порога.
/// Оба метода чистые: без I/O, конфигурации и сети.
/// </summary>
internal static partial class SalaryFilter
{
    public static int? TryParseLowerBoundRub(string? salary)
    {
        if (salary is null)
        {
            return null;
        }

        // 1. Нормализация: неразрывный и узкий неразрывный пробелы -> обычный пробел,
        //    последовательности пробельных символов схлопываются в один, Trim.
        var normalized = CollapseWhitespaceRegex().Replace(
            salary.Replace('\u00A0', ' ').Replace('\u202F', ' '), " ").Trim();

        // 2. Пустая строка или «не указана» — зарплата неизвестна.
        if (normalized.Length == 0
            || normalized.Equals("не указана", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 3. Другие валюты: доллар, евро, тенге.
        if (normalized.Contains('$')
            || normalized.Contains('€')
            || normalized.Contains('₸')
            || normalized.Contains("USD", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("EUR", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("KZT", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 4. Почасовая / посменная оплата — некорректно сравнивать с месячным порогом.
        if (normalized.Contains("час", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("смен", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 5. Только рубли.
        if (!normalized.Contains('₽')
            && !normalized.Contains("руб", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 6. Первое число: последовательность цифр, внутри которой могут быть пробелы.
        var numberMatch = FirstNumberRegex().Match(normalized);
        if (!numberMatch.Success)
        {
            return null;
        }

        // 7. «до 100 000 ₽» — известна только верхняя граница.
        var beforeNumber = normalized[..numberMatch.Index];
        if (EndsWithUpToRegex().IsMatch(beforeNumber))
        {
            return null;
        }

        // 8. Число без пробелов; при переполнении int считаем зарплату нераспознанной.
        var digits = numberMatch.Value.Replace(" ", string.Empty);
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value;
    }

    public static bool ShouldSkip(string? salary, int minSalary)
    {
        // 9. Фильтр выключен или зарплата не распознана — вакансия проходит.
        if (minSalary <= 0)
        {
            return false;
        }

        var lowerBound = TryParseLowerBoundRub(salary);
        if (lowerBound is null)
        {
            return false;
        }

        return lowerBound.Value < minSalary;
    }

    /// <summary>Первое число: цифры, внутри которых могут быть пробелы.</summary>
    [GeneratedRegex(@"[0-9](?:[0-9 ]*[0-9])?", RegexOptions.CultureInvariant)]
    private static partial Regex FirstNumberRegex();

    /// <summary>Любая последовательность пробельных символов.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespaceRegex();

    /// <summary>Текст перед числом заканчивается на «до» по границе слова.</summary>
    [GeneratedRegex(@"\bдо\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndsWithUpToRegex();
}
