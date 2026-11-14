using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiRecruiter.Analyzers;
using AiRecruiter.Models;

namespace AiRecruiter.Notifiers;

public class TelegramNotifier
{
    private readonly string _botToken;
    private readonly long _chatId;
    private readonly HttpClient _httpClient;

    public TelegramNotifier(string botToken, long chatId)
    {
        _botToken = botToken;
        _chatId = chatId;

        var proxyUrl = Environment.GetEnvironmentVariable("TELEGRAM_PROXY_URL");
        var proxyUser = Environment.GetEnvironmentVariable("TELEGRAM_PROXY_USER");
        var proxyPassword = Environment.GetEnvironmentVariable("TELEGRAM_PROXY_PASSWORD");

        HttpClientHandler handler;

        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            Logger.Log("[Telegram] Прокси не используется.");
            handler = new HttpClientHandler();
        }
        else
        {
            try
            {
                var uri = new Uri(proxyUrl);
                var proxy = new WebProxy(uri);

                if (!string.IsNullOrWhiteSpace(proxyUser))
                {
                    proxy.Credentials = new NetworkCredential(proxyUser, proxyPassword ?? string.Empty);
                    Logger.Log($"[Telegram] Прокси: {uri.Host}:{uri.Port} (с авторизацией)");
                }
                else
                {
                    Logger.Log($"[Telegram] Прокси: {uri.Host}:{uri.Port} (без авторизации)");
                }

                handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
            }
            catch (Exception ex)
            {
                Logger.Log($"[Telegram] ОШИБКА прокси: {ex.Message}");
                handler = new HttpClientHandler();
            }
        }

        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<bool> SendVacancyAsync(Vacancy vacancy, AnalysisResult result, string priorityLabel)
    {
        if (IsInternshipTitle(vacancy.Title))
        {
            Logger.Log($"[Telegram] Пропуск: стажировка | {vacancy.Title} | {vacancy.Link}");
            return false;
        }

        var coverLetter = result.CoverLetter.Replace("\\n", "\n");

        bool hasCoverLetter = !string.IsNullOrWhiteSpace(result.CoverLetter)
            && !string.Equals(result.CoverLetter.Trim(), "Cover letter not requested yet", StringComparison.OrdinalIgnoreCase);

        string coverLetterBlock = hasCoverLetter
            ? $"\n\n<blockquote expandable>Сопроводительное письмо:\n{WebUtility.HtmlEncode(coverLetter)}</blockquote>"
            : string.Empty;

        var techStackBlock = string.IsNullOrWhiteSpace(vacancy.TechStack)
            ? string.Empty
            : $"\nСтек: {WebUtility.HtmlEncode(vacancy.TechStack)}";

        string message = $"""
            {(string.IsNullOrEmpty(priorityLabel) ? "Найдена вакансия!" : priorityLabel)}

            Оценка: {result.Score}/10
            Комментарий: {WebUtility.HtmlEncode(result.Comment)}

            Вакансия: {WebUtility.HtmlEncode(vacancy.Title)}
            Компания: {WebUtility.HtmlEncode(vacancy.Company)}
            Зарплата: {WebUtility.HtmlEncode(vacancy.Salary)}{techStackBlock}

            Ссылка: {vacancy.Link}{coverLetterBlock}
            """;

        Logger.Log($"[Telegram] Отправка: score={result.Score} | label={priorityLabel} | {vacancy.Title} | {vacancy.Link} | письмо={coverLetter.Length} | techStack={vacancy.TechStack.Length}");

        var payload = new
        {
            chat_id = _chatId,
            text = message,
            parse_mode = "HTML",
            disable_web_page_preview = false
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_botToken}/sendMessage",
                payload);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Logger.Log($"[Telegram] ОШИБКА {response.StatusCode}: {errorBody} | {vacancy.Title} | {vacancy.Link}");
                return false;
            }

            Logger.Log($"[Telegram] OK: {vacancy.Title} | {vacancy.Link}");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Telegram] ИСКЛЮЧЕНИЕ: {ex.Message} | {vacancy.Title} | {vacancy.Link}");
            return false;
        }
    }

    public async Task<bool> SendParserAlertAsync(string message)
    {
        var preview = message.Length > 100 ? message[..100] : message;
        Logger.Log($"[Telegram] ALERT отправка: {preview}");

        var payload = new
        {
            chat_id = _chatId,
            text = message,
            parse_mode = "HTML",
            disable_web_page_preview = true
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_botToken}/sendMessage",
                payload);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Logger.Log($"[Telegram] ALERT ОШИБКА {response.StatusCode}: {errorBody}");
                return false;
            }

            Logger.Log("[Telegram] ALERT OK");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Telegram] ALERT ИСКЛЮЧЕНИЕ: {ex.Message}");
            return false;
        }
    }

    private static bool IsInternshipTitle(string title)
    {
        var lower = title.ToLowerInvariant();
        if (lower.Contains("стажер") || lower.Contains("стажёр") || lower.Contains("стажиров"))
        {
            return true;
        }
        return Regex.IsMatch(lower, @"\b(intern|internship|trainee)\b");
    }
}
