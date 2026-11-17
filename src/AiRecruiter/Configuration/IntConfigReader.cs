using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace AiRecruiter.Configuration;

/// <summary>
/// Читает целочисленные настройки из IConfiguration с защитой от некорректных значений.
/// Если ключ отсутствует, значение нечисловое или не удаётся распарсить — возвращается defaultValue.
/// </summary>
internal static class IntConfigReader
{
    /// <summary>
    /// Читает целочисленное значение конфигурации. Если ключ отсутствует, значение нечисловое
    /// или не удаётся распарсить — возвращается <paramref name="defaultValue"/>.
    /// </summary>
    public static int Read(IConfiguration? config, string key, int defaultValue)
    {
        if (config == null)
        {
            return defaultValue;
        }

        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return defaultValue;
    }
}
