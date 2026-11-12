using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AiRecruiter.Models;

namespace AiRecruiter.Parsers;

/// <summary>
/// Parses hh.ru vacancy search result pages using AngleSharp HTML parsing only
/// (the official hh.ru API is not used).
/// </summary>
public class HhParser
{
    private const string BaseUrl = "https://hh.ru";
    private static readonly string[] CardSelectors =
    {
        ".magritte-redesign",   // current hh.ru vacancy card markup
        "[data-qa='serp-item']" // fallback selector if the markup changes
    };

    // Delay between vacancy-page requests so hh.ru does not block us.
    private const int DescriptionDelayMinMs = 1000;
    private const int DescriptionDelayMaxMs = 2000;

    private readonly HttpClient _http;

    public HhParser()
    {
        _http = new HttpClient();

        // Pretend to be a real browser so hh.ru does not block the request.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.8");
    }

    /// <summary>
    /// Searches hh.ru for vacancies matching <paramref name="query"/>, paginating through
    /// the search results (page=0, page=1, ... up to page 10) and returning only vacancies
    /// whose link has not been seen before.
    /// </summary>
    /// <param name="query">Search text (e.g. "C# developer").</param>
    /// <param name="areaCode">hh.ru area code (e.g. "113" for Russia, "4" for Novosibirsk).</param>
    /// <param name="workFormat">Work format filter (e.g. "REMOTE"). Empty string means any format.</param>
    /// <param name="seenLinks">Vacancy links that have already been processed; they are skipped.</param>
    /// <param name="maxNewVacancies">Maximum number of new vacancies to collect (default 10).</param>
    /// <returns>Up to <paramref name="maxNewVacancies"/> new vacancies, or an empty list if the request fails.</returns>
    public async Task<List<Vacancy>> ParseVacanciesAsync(string query, string areaCode, string workFormat, HashSet<string> seenLinks, int maxNewVacancies = 10)
    {
        var vacancies = new List<Vacancy>();
        var random = new Random();

        // Exclude unwanted words from the vacancy title so irrelevant vacancies
        // (automation QA, gamedev, lead roles, etc.) do not clutter the results.
        var searchQuery = query
            + " NOT NAME:Fullstack"
            + " NOT NAME:автоматизатор"
            + " NOT NAME:python"
            + " NOT NAME:java"
            + " NOT NAME:c#"
            + " NOT NAME:Lead"
            + " NOT NAME:Automation"
            + " NOT NAME:1С"
            + " NOT NAME:Сетевой"
            + " NOT NAME:Head"
            + " NOT NAME:Auto"
            + " NOT NAME:Автотестировщик"
            + " NOT NAME:автоматизации"
            + " NOT NAME:автоматизированному"
            + " NOT NAME:AQA"
            + " NOT NAME:Full-stak"
            + " NOT NAME:Gamedev"
            + " NOT NAME:Game"
            + " NOT NAME:IGaming"
            + " NOT NAME:Go";

        int pagesProcessed = 0;
        bool anyPageHadSalary = false;

        for (int page = 0; page <= 10; page++)
        {
            // Build the search URL with the query and area properly escaped.
            // Sort by publication time so the newest vacancies come first.
            var url = $"{BaseUrl}/search/vacancy?text={Uri.EscapeDataString(searchQuery)}&area={areaCode}&employment=full&order_by=publication_time&page={page}";

            // Only add the work_format parameter when a specific format is requested.
            // An empty string means "any format", so the parameter is omitted entirely.
            if (!string.IsNullOrEmpty(workFormat))
            {
                url += $"&work_format={Uri.EscapeDataString(workFormat)}";
            }

            string html;
            try
            {
                html = await _http.GetStringAsync(url);
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Failed to load hh.ru search page (page {page}): {ex.Message}");
                break;
            }

            // Parse the HTML with AngleSharp using the default configuration.
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html));

            // Select vacancy cards, falling back to the legacy selector if needed.
            var cards = document.QuerySelectorAll(CardSelectors[0]);
            if (cards.Length == 0)
            {
                cards = document.QuerySelectorAll(CardSelectors[1]);
            }

            // No cards on this page means we have reached the end of the results.
            if (cards.Length == 0)
            {
                break;
            }

            int cardsWithSalary = 0;
            int cardsWithoutSalary = 0;
            string? exampleSalary = null;
            var firstCardChecked = false;

            foreach (var card in cards)
            {
                // Stop as soon as we have collected enough new vacancies.
                if (vacancies.Count >= maxNewVacancies)
                {
                    break;
                }

                // Skip the first card if it looks like a promo/banner
                // (it has neither a vacancy title nor a vacancy link).
                if (!firstCardChecked)
                {
                    firstCardChecked = true;
                    var firstTitle = card.QuerySelector("[data-qa='serp-item__title']");
                    var firstLink = card.QuerySelector("a[href]");
                    if (string.IsNullOrWhiteSpace(firstTitle?.TextContent) && firstLink == null)
                    {
                        continue;
                    }
                }

                // Extract the title from the dedicated attribute, fallback to the first <a> text.
                var titleElement = card.QuerySelector("[data-qa='serp-item__title']") ?? card.QuerySelector("a");
                var title = titleElement?.TextContent.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    Logger.Log("[HhParser] Пропуск карточки: причина=пустой заголовок");
                    continue;
                }

                // Company name may be missing on some cards.
                var companyElement = card.QuerySelector("[data-qa='vacancy-serp__vacancy-employer']");
                var company = companyElement?.TextContent.Trim() ?? string.Empty;

                // Salary, defaulting to "не указана" when not present.
                var salary = ExtractSalary(card);
                if (salary != "не указана")
                {
                    cardsWithSalary++;
                    exampleSalary ??= salary;
                }
                else
                {
                    cardsWithoutSalary++;
                }

                // Link: href of the first <a>, make absolute if relative, strip the query string.
                var linkElement = card.QuerySelector("a[href]");
                var link = string.Empty;
                if (linkElement is not null)
                {
                    var href = linkElement.GetAttribute("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        href = href.Trim();
                        if (href.StartsWith("//"))
                        {
                            href = "https:" + href;
                        }
                        else if (href.StartsWith("/"))
                        {
                            href = BaseUrl + href;
                        }

                        var queryIndex = href.IndexOf('?');
                        if (queryIndex >= 0)
                        {
                            href = href[..queryIndex];
                        }

                        link = href;
                    }
                }

                // Skip cards without a usable link or links we have already seen.
                if (string.IsNullOrWhiteSpace(link))
                {
                    Logger.Log("[HhParser] Пропуск карточки: причина=нет ссылки");
                    continue;
                }
                if (seenLinks.Contains(link))
                {
                    Logger.Log("[HhParser] Пропуск карточки: причина=уже видели");
                    continue;
                }

                // Load the full vacancy page to extract its description.
                string description = await LoadVacancyDescriptionAsync(link);
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = Vacancy.DescriptionNotLoaded;
                    Logger.Log($"[HhParser] Описание не загружено, вакансия добавлена: {link}");
                }

                // Wait 1-2 seconds between vacancy-page requests to be gentle with hh.ru.
                int descriptionDelayMs = random.Next(DescriptionDelayMinMs, DescriptionDelayMaxMs + 1);
                await Task.Delay(descriptionDelayMs);

                vacancies.Add(new Vacancy
                {
                    Title = title,
                    Company = company,
                    Salary = salary,
                    Link = link,
                    Description = description
                });
            }

            Logger.Log($"[HhParser] Страница {page}: карточки={cards.Length}, с зарплатой={cardsWithSalary}, без зарплаты={cardsWithoutSalary}");
            if (exampleSalary is not null)
            {
                Logger.Log($"[HhParser] Пример зарплаты: {exampleSalary}");
            }
            if (cardsWithSalary > 0)
            {
                anyPageHadSalary = true;
            }
            pagesProcessed++;

            // If we collected enough new vacancies, stop paginating.
            if (vacancies.Count >= maxNewVacancies)
            {
                break;
            }

            // Wait 4-8 seconds before the next page request to be gentle with hh.ru.
            int delayMs = random.Next(4000, 8001);
            await Task.Delay(delayMs);
        }

        Logger.Log($"[HhParser] Итог: собрано {vacancies.Count} из {maxNewVacancies}, страниц={pagesProcessed}");

        if (!anyPageHadSalary && pagesProcessed > 0)
        {
            try
            {
                var debugDir = "debug";
                if (!Directory.Exists(debugDir))
                {
                    Directory.CreateDirectory(debugDir);
                }
                var debugUrl = $"{BaseUrl}/search/vacancy?text={Uri.EscapeDataString(searchQuery)}&area={areaCode}&employment=full&order_by=publication_time&page=0";
                var debugHtml = await _http.GetStringAsync(debugUrl);
                await File.WriteAllTextAsync(Path.Combine(debugDir, "hh_search_page0.html"), debugHtml);
            }
            catch
            {
                // Debug save errors are non-fatal.
            }
        }

        return vacancies;
    }

    /// <summary>
    /// Opens the vacancy's page and extracts its full description from the block
    /// marked with <c>data-qa="vacancy-description"</c> (with fallbacks).
    /// </summary>
    /// <param name="vacancyUrl">Absolute URL of the vacancy page.</param>
    /// <returns>The extracted description text, or <see cref="string.Empty"/> if the
    /// page could not be loaded or no description block was found.</returns>
    private async Task<string> LoadVacancyDescriptionAsync(string vacancyUrl)
    {
        string html;
        try
        {
            html = await FetchWithRetryAsync(vacancyUrl);
        }
        catch (HttpRequestException ex)
        {
            Logger.Log($"[HhParser] Ошибка загрузки описания {vacancyUrl}: {ex.Message}");
            return string.Empty;
        }
        catch (TaskCanceledException ex)
        {
            Logger.Log($"[HhParser] Ошибка загрузки описания {vacancyUrl}: {ex.Message}");
            return string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[HhParser] Ошибка загрузки описания {vacancyUrl}: {ex.Message}");
            return string.Empty;
        }

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        // Extended selector chain, in order. Accept first candidate whose normalized
        // text length is >= 100 characters.
        var descriptionSelectors = new[]
        {
            "[data-qa='vacancy-description']",
            "[itemprop='description']",
            ".vacancy-description",
            ".b-vacancy-desc",
            ".vacancy-branded-user-content",
            "[data-qa='vacancy-description-html']",
            ".g-user-content",
        };

        IElement? descriptionElement = null;
        foreach (var selector in descriptionSelectors)
        {
            var candidate = document.QuerySelector(selector);
            if (candidate is not null)
            {
                var text = NormalizeDescriptionText(candidate.TextContent);
                if (text.Length >= 100)
                {
                    descriptionElement = candidate;
                    break;
                }
            }
        }

        // #a11y-main-content — только если длина текста >= 200 символов.
        if (descriptionElement is null)
        {
            var a11y = document.QuerySelector("#a11y-main-content");
            if (a11y is not null)
            {
                var text = NormalizeDescriptionText(a11y.TextContent);
                if (text.Length >= 200)
                {
                    descriptionElement = a11y;
                }
            }
        }

        if (descriptionElement is null)
        {
            var hasVacancyTitle = document.QuerySelector("[data-qa='vacancy-title']") is not null;
            Logger.Log($"[HhParser] Описание не найдено: {vacancyUrl} | title={document.Title} | hasVacancyTitle={hasVacancyTitle}");
            return string.Empty;
        }

        return NormalizeDescriptionText(descriptionElement.TextContent);
    }

    /// <summary>
    /// Extracts and normalizes the salary text from a vacancy card, or returns "не указана".
    /// </summary>
    private static string ExtractSalary(IElement card)
    {
        // Chain: old data-qa, legacy data-qa, RUB/other-currency spans → ParentElement.
        var salaryElement = card.QuerySelector("[data-qa='vacancy-serp__vacancy-compensation']")
            ?? card.QuerySelector("[data-qa='vacancy-serp__compensation']")
            ?? card.QuerySelector("data[value='RUB']")?.ParentElement
            ?? card.QuerySelector("data[value='USD']")?.ParentElement
            ?? card.QuerySelector("data[value='EUR']")?.ParentElement
            ?? card.QuerySelector("data[value='KZT']")?.ParentElement
            ?? card.QuerySelector("data[value='BYN']")?.ParentElement
            ?? card.QuerySelector("data[value='UZS']")?.ParentElement
            ?? card.QuerySelector("data[value='GEL']")?.ParentElement;

        if (salaryElement is not null)
        {
            var normalized = NormalizeSalaryText(salaryElement.TextContent);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        // Structural fallback: scan all <span> elements inside the card.
        var moneyMarkers = new[] { '₽', '$', '€', '₸', '₴', '₾', '₼' };
        var moneyStrings = new[] { "руб", "USD", "EUR" };

        IElement? bestSpan = null;
        int bestLength = int.MaxValue;

        foreach (var span in card.QuerySelectorAll("span"))
        {
            var text = NormalizeSalaryText(span.TextContent);
            if (string.IsNullOrWhiteSpace(text) || text.Length >= 200)
            {
                continue;
            }

            bool hasDigit = text.Any(char.IsDigit);
            bool hasMoneyMarker = moneyMarkers.Any(m => text.Contains(m))
                || moneyStrings.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));

            if (!hasDigit || !hasMoneyMarker)
            {
                continue;
            }

            if (text.Length < bestLength)
            {
                bestLength = text.Length;
                bestSpan = span;
            }
        }

        if (bestSpan is not null)
        {
            return NormalizeSalaryText(bestSpan.TextContent);
        }

        return "не указана";
    }

    private static string NormalizeSalaryText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        text = text.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string NormalizeDescriptionText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        text = text.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Fetches a URL with one retry after 2 seconds on HttpRequestException or TaskCanceledException.
    /// </summary>
    private async Task<string> FetchWithRetryAsync(string url)
    {
        try
        {
            return await _http.GetStringAsync(url);
        }
        catch (HttpRequestException)
        {
            await Task.Delay(2000);
            return await _http.GetStringAsync(url);
        }
        catch (TaskCanceledException)
        {
            await Task.Delay(2000);
            return await _http.GetStringAsync(url);
        }
    }
}
