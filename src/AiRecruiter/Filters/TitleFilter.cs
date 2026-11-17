using System.Text.RegularExpressions;

namespace AiRecruiter.Filters;

/// <summary>
/// Предфильтр заголовков вакансий по стоп-словам: находит первое стоп-слово,
/// встречающееся в заголовке целиком (по границам слова).
/// </summary>
internal static class TitleFilter
{
    public static string? FindExcludedWord(string? title, IReadOnlyList<string> words)
    {
        if (string.IsNullOrEmpty(title) || words.Count == 0)
        {
            return null;
        }

        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            var pattern = "(?<![\\p{L}\\p{N}])" + Regex.Escape(word.Trim()) + "(?![\\p{L}\\p{N}])";
            if (Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return word.Trim();
            }
        }

        return null;
    }
}
