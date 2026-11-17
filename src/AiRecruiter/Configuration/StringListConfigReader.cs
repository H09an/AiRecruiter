using Microsoft.Extensions.Configuration;

namespace AiRecruiter.Configuration;

/// <summary>
/// Читает список строк из IConfiguration без Binder и null-forgiving:
/// дочерние значения секции обрезаются, пустые и whitespace-значения отбрасываются,
/// порядок сохраняется.
/// </summary>
internal static class StringListConfigReader
{
    /// <summary>
    /// Читает список строк из секции конфигурации. Если конфигурация или секция
    /// отсутствуют — возвращается пустой список.
    /// </summary>
    public static IReadOnlyList<string> Read(IConfiguration? config, string key)
    {
        if (config is null)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var child in config.GetSection(key).GetChildren())
        {
            var value = child.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }
}
