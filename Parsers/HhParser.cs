using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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

                // Load the full vacancy page: description plus structured fields.
                var details = await LoadVacancyDetailsAsync(link);

                string description = details?.Description ?? string.Empty;
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = Vacancy.DescriptionNotLoaded;
                    Logger.Log($"[HhParser] Описание не загружено, вакансия добавлена: {link}");
                }

                // The vacancy page is more precise about gross/net and salary text.
                // Prefer it when it contains a real value; otherwise keep the card value.
                var pageSalary = details?.Salary ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(pageSalary)
                    && pageSalary != "не указана"
                    && !string.Equals(pageSalary, salary, StringComparison.Ordinal))
                {
                    Logger.Log($"[HhParser] Зарплата: карточка='{salary}', страница='{pageSalary}': {link}");
                    salary = pageSalary;
                }

                var resolvedCompany = string.IsNullOrWhiteSpace(company)
                    ? details?.Company ?? string.Empty
                    : company;

                // Wait 1-2 seconds between vacancy-page requests to be gentle with hh.ru.
                int descriptionDelayMs = random.Next(DescriptionDelayMinMs, DescriptionDelayMaxMs + 1);
                await Task.Delay(descriptionDelayMs);

                vacancies.Add(new Vacancy
                {
                    Title = title,
                    Company = resolvedCompany,
                    Salary = salary,
                    Link = link,
                    Description = description,
                    Experience = details?.Experience ?? string.Empty,
                    WorkFormat = details?.WorkFormat ?? string.Empty,
                    Schedule = details?.Schedule ?? string.Empty,
                    Employment = details?.Employment ?? string.Empty,
                    Location = details?.Location ?? string.Empty,
                    HiringFormats = details?.HiringFormats ?? string.Empty,
                    Requirements = details?.Requirements ?? string.Empty,
                    Responsibilities = details?.Responsibilities ?? string.Empty,
                    Conditions = details?.Conditions ?? string.Empty,
                    PublishedAt = details?.PublishedAt ?? string.Empty,
                    KeySkills = details?.KeySkills ?? new List<string>()
                });

                Logger.Log($"[HhParser] Детали: experience={details?.Experience ?? ""} | workFormat={details?.WorkFormat ?? ""} | schedule={details?.Schedule ?? ""} | employment={details?.Employment ?? ""} | location={details?.Location ?? ""} | salary={salary} | skills={details?.KeySkills.Count ?? 0} | requirements={details?.Requirements.Length ?? 0} симв. | responsibilities={details?.Responsibilities.Length ?? 0} симв. | conditions={details?.Conditions.Length ?? 0} симв. | {link}");
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
    /// Opens the vacancy's page and extracts the description plus structured fields.
    /// </summary>
    private async Task<Vacancy?> LoadVacancyDetailsAsync(string vacancyUrl)
    {
        string html;
        try
        {
            html = await FetchWithRetryAsync(vacancyUrl);
        }
        catch (HttpRequestException ex)
        {
            Logger.Log($"[HhParser] Ошибка загрузки описания {vacancyUrl}: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            Logger.Log($"[HhParser] Ошибка загрузки описания {vacancyUrl}: {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[HhParser] Ошибка загрузки описания {vacancyUrl}: {ex.Message}");
            return null;
        }

        try
        {
            return await ParseVacancyHtmlAsync(html, vacancyUrl);
        }
        catch (Exception ex)
        {
            Logger.Log($"[HhParser] Ошибка разбора страницы вакансии {vacancyUrl}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a vacancy page HTML without network access. Used by the parser and by
    /// the offline diagnostic mode (<c>--parse-samples</c>).
    /// </summary>
    public static async Task<Vacancy> ParseVacancyHtmlAsync(string html, string link)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        var vacancy = new Vacancy { Link = link };

        vacancy.Title = GetText(document.QuerySelector("[data-qa='vacancy-title']"));
        if (string.IsNullOrWhiteSpace(vacancy.Title))
        {
            vacancy.Title = GetFallbackTitle(document) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vacancy.Title))
            {
                Logger.Log($"[HhParser] Поле Title не найдено для {link}");
            }
        }

        vacancy.Company = GetText(document.QuerySelector("[data-qa='vacancy-company-name']"));
        if (string.IsNullOrWhiteSpace(vacancy.Company))
        {
            vacancy.Company = GetFallbackCompany(document) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vacancy.Company))
            {
                Logger.Log($"[HhParser] Поле Company не найдено для {link}");
            }
        }

        vacancy.Experience = GetText(document.QuerySelector("[data-qa='vacancy-experience']"));
        if (string.IsNullOrWhiteSpace(vacancy.Experience))
        {
            vacancy.Experience = GetText(document.QuerySelector("[data-qa='work-experience-text'] dd"));
        }
        if (string.IsNullOrWhiteSpace(vacancy.Experience))
        {
            var expElement = document.QuerySelector("[data-qa='work-experience-text']");
            if (expElement is not null)
            {
                var expText = GetText(expElement);
                if (!string.IsNullOrWhiteSpace(expText))
                {
                    vacancy.Experience = expText.Replace("Опыт работы:", string.Empty).Replace("Опыт работы", string.Empty).Trim().TrimStart(':').Trim();
                }
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Experience))
        {
            var mainText = GetMainText(document);
            var expMatch = Regex.Match(mainText, @"Опыт работы[:\s]*([^\n]{1,40})")
                ?? Regex.Match(mainText, @"(\d+\s*[–-]\s*\d+\s*(лет|года|год))")
                ?? Regex.Match(mainText, @"(без опыта|от \d+ лет|более \d+ лет)");
            if (expMatch.Success)
            {
                vacancy.Experience = NormalizeInline(expMatch.Groups[1].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Experience))
        {
            Logger.Log($"[HhParser] Поле Experience не найдено для {link}");
        }

        vacancy.WorkFormat = GetDdOrLabeledText(document, "work-formats-text", "Формат работы:");
        if (string.IsNullOrWhiteSpace(vacancy.WorkFormat))
        {
            var wfElement = document.QuerySelector("[data-qa='work-formats-text']");
            if (wfElement is not null)
            {
                var wfText = GetText(wfElement);
                if (!string.IsNullOrWhiteSpace(wfText))
                {
                    vacancy.WorkFormat = wfText.Replace("Формат работы:", string.Empty).Replace("Формат работы", string.Empty).Trim().TrimStart(':').Trim();
                }
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.WorkFormat))
        {
            var mainText = GetMainText(document);
            var wfMatch = Regex.Match(mainText, @"Формат работы[:\s]*([^\n]{1,40})")
                ?? Regex.Match(mainText, @"(удалённо|гибрид|на месте работодателя|офис|комбинированный)");
            if (wfMatch.Success)
            {
                vacancy.WorkFormat = NormalizeInline(wfMatch.Groups[wfMatch.Groups.Count > 1 ? 1 : 0].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.WorkFormat))
        {
            Logger.Log($"[HhParser] Поле WorkFormat не найдено для {link}");
        }

        vacancy.Schedule = GetDdOrLabeledText(document, "work-schedule-by-days-text", "График:");
        if (string.IsNullOrWhiteSpace(vacancy.Schedule))
        {
            var schedElement = document.QuerySelector("[data-qa='work-schedule-by-days-text']");
            if (schedElement is not null)
            {
                var schedText = GetText(schedElement);
                if (!string.IsNullOrWhiteSpace(schedText))
                {
                    vacancy.Schedule = schedText.Replace("График:", string.Empty).Replace("График", string.Empty).Trim().TrimStart(':').Trim();
                }
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Schedule))
        {
            var mainText = GetMainText(document);
            var schedMatch = Regex.Match(mainText, @"График[:\s]*([^\n]{1,40})")
                ?? Regex.Match(mainText, @"(\d/\d|сменный|гибкий)");
            if (schedMatch.Success)
            {
                vacancy.Schedule = NormalizeInline(schedMatch.Groups[schedMatch.Groups.Count > 1 ? 1 : 0].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Schedule))
        {
            Logger.Log($"[HhParser] Поле Schedule не найдено для {link}");
        }

        vacancy.Employment = GetDdOrLabeledText(document, "common-employment-text", "Тип занятости");
        if (string.IsNullOrWhiteSpace(vacancy.Employment))
        {
            var empElement = document.QuerySelector("[data-qa='common-employment-text']");
            if (empElement is not null)
            {
                var empText = GetText(empElement);
                if (!string.IsNullOrWhiteSpace(empText))
                {
                    vacancy.Employment = empText.Replace("Тип занятости", string.Empty).Trim().TrimStart(':').Trim();
                }
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Employment))
        {
            var mainText = GetMainText(document);
            var empMatch = Regex.Match(mainText, @"Тип занятости[:\s]*([^\n]{1,40})")
                ?? Regex.Match(mainText, @"(полная|частичная|проектная|стажировка)");
            if (empMatch.Success)
            {
                vacancy.Employment = NormalizeInline(empMatch.Groups[empMatch.Groups.Count > 1 ? 1 : 0].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Employment))
        {
            Logger.Log($"[HhParser] Поле Employment не найдено для {link}");
        }

        vacancy.Location = GetText(document.QuerySelector("[data-qa='vacancy-view-raw-address']"));
        if (string.IsNullOrWhiteSpace(vacancy.Location))
        {
            vacancy.Location = GetText(document.QuerySelector("[data-qa='vacancy-address-with-map']"));
        }
        if (string.IsNullOrWhiteSpace(vacancy.Location))
        {
            vacancy.Location = GetText(document.QuerySelector("[data-qa='vacancy-view-location']"));
        }
        if (string.IsNullOrWhiteSpace(vacancy.Location))
        {
            var metaDesc = document.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
            var locMatch = Regex.Match(metaDesc, @"Вакансия .+? в (?:компании .+?)\. (?:Зарплата: .+?\. )?(.+?)\. Требуемый опыт");
            if (!locMatch.Success)
            {
                locMatch = Regex.Match(metaDesc, @"Вакансия .+? в (.+?)\.");
            }
            if (locMatch.Success)
            {
                vacancy.Location = NormalizeInline(locMatch.Groups[1].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Location))
        {
            var mainText = GetMainText(document);
            var locMatch = Regex.Match(mainText, @"(?:Город|Локация|Адрес)[:\s]*([^\n]{1,80})");
            if (locMatch.Success)
            {
                vacancy.Location = NormalizeInline(locMatch.Groups[1].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.Location))
        {
            Logger.Log($"[HhParser] Поле Location не найдено для {link}");
        }

        vacancy.HiringFormats = GetHiringFormats(document);
        if (string.IsNullOrWhiteSpace(vacancy.HiringFormats))
        {
            var mainText = GetMainText(document);
            var hfMatch = Regex.Match(mainText, @"Оформление[:\s]*([^\n]{1,80})");
            if (hfMatch.Success)
            {
                vacancy.HiringFormats = NormalizeInline(hfMatch.Groups[1].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.HiringFormats))
        {
            Logger.Log($"[HhParser] Поле HiringFormats не найдено для {link}");
        }

        vacancy.KeySkills = GetKeySkills(document);
        if (vacancy.KeySkills.Count == 0)
        {
            vacancy.KeySkills = GetFallbackKeySkills(document);
            if (vacancy.KeySkills.Count == 0)
            {
                Logger.Log($"[HhParser] Поле KeySkills не найдено для {link}");
            }
        }

        vacancy.PublishedAt = GetPublishedAt(document);
        if (string.IsNullOrWhiteSpace(vacancy.PublishedAt))
        {
            var metaDesc = document.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
            var pubMatch = Regex.Match(metaDesc, @"Дата публикации:\s*(.+?)\.");
            if (pubMatch.Success)
            {
                vacancy.PublishedAt = NormalizeInline(pubMatch.Groups[1].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.PublishedAt))
        {
            var mainText = GetMainText(document);
            var pubMatch = Regex.Match(mainText, @"Дата публикации[:\s]*([^\n]{5,40})")
                ?? Regex.Match(mainText, @"\d{1,2}\s+\S+\s+\d{4}");
            if (pubMatch.Success)
            {
                vacancy.PublishedAt = NormalizeInline(pubMatch.Groups[pubMatch.Groups.Count > 1 ? 1 : 0].Value);
            }
        }
        if (string.IsNullOrWhiteSpace(vacancy.PublishedAt))
        {
            Logger.Log($"[HhParser] Поле PublishedAt не найдено для {link}");
        }

        var pageSalary = ExtractPageSalaryWithSource(document, out var salarySource);
        vacancy.Salary = pageSalary;
        Logger.Log($"[HhParser] Salary источник={salarySource} | {link}");

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
        string descriptionSource = "selector";
        foreach (var selector in descriptionSelectors)
        {
            var candidate = document.QuerySelector(selector);
            if (candidate is not null)
            {
                var text = GetTextWithBreaks(candidate);
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
                var text = GetTextWithBreaks(a11y);
                if (text.Length >= 200)
                {
                    descriptionElement = a11y;
                }
            }
        }

        // НОВЫЙ FALLBACK: самый длинный текстовый блок основного контента.
        if (descriptionElement is null)
        {
            var longestBlock = GetLongestDescriptionBlock(document);
            if (longestBlock is not null && longestBlock.Value.Element is not null)
            {
                descriptionElement = longestBlock.Value.Element!;
                descriptionSource = longestBlock.Value.Source;
                Logger.Log($"[HhParser] Description взят из longest-block для {link}");
            }
        }

        if (descriptionElement is null)
        {
            var hasVacancyTitle = document.QuerySelector("[data-qa='vacancy-title']") is not null;
            Logger.Log($"[HhParser] Описание не найдено: {link} | title={document.Title} | hasVacancyTitle={hasVacancyTitle}");
            vacancy.Description = string.Empty;
            Logger.Log($"[HhParser] Поле Description не найдено для {link}");
            return vacancy;
        }

        vacancy.Description = GetTextWithBreaks(descriptionElement);
        Logger.Log($"[HhParser] Description источник={descriptionSource} | {link}");
        ExtractSections(descriptionElement, vacancy, document);
        return vacancy;
    }

    /// <summary>
    /// Splits the description into Requirements / Responsibilities / Conditions by
    /// strong/bold headings. Non-category headings (e.g. "Тест-дизайн:") stay inside
    /// the current section.
    /// </summary>
    private static void ExtractSections(IElement descriptionElement, Vacancy vacancy, IDocument document)
    {
        var requirements = new StringBuilder();
        var responsibilities = new StringBuilder();
        var conditions = new StringBuilder();
        var other = new StringBuilder();
        var intro = new StringBuilder();

        string current = "intro";

        foreach (var child in descriptionElement.ChildNodes)
        {
            if (child is IText textNode)
            {
                var text = NormalizeMultiline(textNode.Data);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AppendToSection(current, text, intro, requirements, responsibilities, conditions, other);
                }
                continue;
            }

            if (child is not IElement element)
            {
                continue;
            }

            if (TryGetCategoryHeading(element, out var category, out var headingText))
            {
                // Preserve the heading text at the start of the corresponding section
                var heading = NormalizeInline(headingText);
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    switch (category)
                    {
                        case "requirements":
                            if (requirements.Length > 0) requirements.Append('\n');
                            requirements.Append(heading);
                            break;
                        case "responsibilities":
                            if (responsibilities.Length > 0) responsibilities.Append('\n');
                            responsibilities.Append(heading);
                            break;
                        case "conditions":
                            if (conditions.Length > 0) conditions.Append('\n');
                            conditions.Append(heading);
                            break;
                        case "other":
                            if (other.Length > 0) other.Append('\n');
                            other.Append(heading);
                            break;
                    }
                }
                current = category;
                continue;
            }

            // Lists are processed item-by-item so closing blocks like
            // "О компании" / "Откликайтесь" do not pollute Conditions.
            if (element.TagName is "UL" or "OL")
            {
                var listItems = element.Children
                    .Where(child => child.TagName == "LI")
                    .ToList();

                if (listItems.Count > 0)
                {
                    foreach (var li in listItems)
                    {
                        var itemText = GetTextWithBreaks(li);
                        if (string.IsNullOrWhiteSpace(itemText))
                        {
                            continue;
                        }

                        itemText = "- " + itemText;
                        var targetSection = IsOtherText(itemText) ? "other" : current;
                        AppendToSection(targetSection, itemText, intro, requirements, responsibilities, conditions, other);
                    }
                    continue;
                }
            }

            var blockText = GetTextWithBreaks(element);
            if (!string.IsNullOrWhiteSpace(blockText))
            {
                var targetSection = IsOtherText(blockText) ? "other" : current;
                AppendToSection(targetSection, blockText, intro, requirements, responsibilities, conditions, other);
            }
        }

        vacancy.Requirements = NormalizeMultiline(requirements.ToString());
        vacancy.Responsibilities = NormalizeMultiline(responsibilities.ToString());
        vacancy.Conditions = NormalizeMultiline(conditions.ToString());

        if (string.IsNullOrWhiteSpace(vacancy.Requirements)
            && string.IsNullOrWhiteSpace(vacancy.Responsibilities)
            && string.IsNullOrWhiteSpace(vacancy.Conditions))
        {
            // Fallback: try regex-based section extraction from Description text
            ExtractSectionsFromText(vacancy.Description, vacancy);
            if (string.IsNullOrWhiteSpace(vacancy.Requirements)
                && string.IsNullOrWhiteSpace(vacancy.Responsibilities)
                && string.IsNullOrWhiteSpace(vacancy.Conditions))
            {
                vacancy.Requirements = vacancy.Description;
                Logger.Log($"[HhParser] Структура описания не распознана: {vacancy.Link}");
            }
        }

        // Secondary fallback: if Requirements is still empty, scan main content headers
        if (string.IsNullOrWhiteSpace(vacancy.Requirements))
        {
            ExtractSectionsFromMainContent(document, vacancy);
        }

        if (string.IsNullOrWhiteSpace(vacancy.Requirements))
        {
            vacancy.Requirements = vacancy.Description;
            Logger.Log($"[HhParser] Структура описания не распознана: {vacancy.Link}");
        }

        if (string.IsNullOrWhiteSpace(vacancy.Requirements))
        {
            Logger.Log($"[HhParser] Поле Requirements не найдено для {vacancy.Link}");
        }
        if (string.IsNullOrWhiteSpace(vacancy.Responsibilities))
        {
            Logger.Log($"[HhParser] Поле Responsibilities не найдено для {vacancy.Link}");
        }
        if (string.IsNullOrWhiteSpace(vacancy.Conditions))
        {
            Logger.Log($"[HhParser] Поле Conditions не найдено для {vacancy.Link}");
        }

        Logger.Log($"[HhParser] Секции: requirements={vacancy.Requirements.Length} | responsibilities={vacancy.Responsibilities.Length} | conditions={vacancy.Conditions.Length} | {vacancy.Link}");
    }

    /// <summary>
    /// Fallback: extract Requirements / Responsibilities / Conditions from Description
    /// text using regex headings.
    /// </summary>
    private static void ExtractSectionsFromText(string description, Vacancy vacancy)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        var sectionRegex = new Regex(@"(?im)^\s*(Требования|Обязанности|Условия|Мы предлагаем|Что предстоит делать|Вам предстоит|Вы нам подходите|Необходимые навыки|Чего мы ждем|Чего мы ждём|Ожидаем|Кто нам подходит|Кого мы ищем|Будет плюсом|Желательно|Преимуществом|Что мы предлагаем|Что предлагаем)[:\s]*$", RegexOptions.Multiline);
        var matches = sectionRegex.Matches(description).Cast<Match>().ToList();

        if (matches.Count == 0)
        {
            return;
        }

        var requirements = new StringBuilder();
        var responsibilities = new StringBuilder();
        var conditions = new StringBuilder();

        for (int i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = (i + 1 < matches.Count) ? matches[i + 1].Index : description.Length;
            var sectionText = description.Substring(start, end - start).Trim();

            var heading = matches[i].Groups[1].Value.Trim().ToLowerInvariant();
            if (heading.StartsWith("требования") || heading.StartsWith("необходимые навыки") || heading.StartsWith("вы нам подходите") || heading.StartsWith("наши ожидания") || heading.StartsWith("стек технологий") || heading.StartsWith("что мы ждём") || heading.StartsWith("мы ищем кандидата") || heading.StartsWith("чего мы ждем") || heading.StartsWith("чего мы ждём") || heading.StartsWith("ожидаем") || heading.StartsWith("кто нам подходит") || heading.StartsWith("кого мы ищем") || heading.StartsWith("мы ждём") || heading.StartsWith("наши требования") || heading.StartsWith("что мы ожидаем") || heading.StartsWith("будет плюсом") || heading.StartsWith("желательно") || heading.StartsWith("преимуществом"))
            {
                if (requirements.Length > 0) requirements.Append('\n');
                requirements.Append(sectionText);
            }
            else if (heading.StartsWith("обязанности") || heading.StartsWith("что предстоит делать") || heading.StartsWith("вам предстоит") || heading.StartsWith("задачи") || heading.StartsWith("чем предстоит заниматься") || heading.StartsWith("что делать") || heading.StartsWith("что нужно делать") || heading.StartsWith("обязанности и задачи"))
            {
                if (responsibilities.Length > 0) responsibilities.Append('\n');
                responsibilities.Append(sectionText);
            }
            else if (heading.StartsWith("условия") || heading.StartsWith("мы предлагаем") || heading.StartsWith("что мы предлагаем") || heading.StartsWith("условия работы") || heading.StartsWith("условия труда") || heading.StartsWith("что предлагаем"))
            {
                if (conditions.Length > 0) conditions.Append('\n');
                conditions.Append(sectionText);
            }
        }

        vacancy.Requirements = NormalizeMultiline(requirements.ToString());
        vacancy.Responsibilities = NormalizeMultiline(responsibilities.ToString());
        vacancy.Conditions = NormalizeMultiline(conditions.ToString());
    }

    /// <summary>
    /// Secondary fallback: scan main content for h2/h3/h4/strong/b elements matching
    /// section aliases, and collect following siblings until the next heading.
    /// </summary>
    private static void ExtractSectionsFromMainContent(IDocument document, Vacancy vacancy)
    {
        var main = document.QuerySelector("main, [role='main'], #a11y-main-content, .tmpl-hh-content") ?? document.Body;
        if (main is null)
        {
            return;
        }

        // Exclude header, footer, nav, aside
        var content = main;
        var excluded = content.QuerySelectorAll("header, footer, nav, aside");
        foreach (var ex in excluded)
        {
            // We can't easily remove from the tree, so we'll skip these when iterating
        }

        var headingCandidates = content.QuerySelectorAll("h2, h3, h4, strong, b");
        var processed = false;

        foreach (var heading in headingCandidates)
        {
            var headingText = NormalizeInline(heading.TextContent);
            if (string.IsNullOrWhiteSpace(headingText) || headingText.Length > 120)
            {
                continue;
            }

            var normalized = headingText.Trim().TrimEnd(':').Trim().ToLowerInvariant();
            string? category = null;

            if (Matches(normalized, "требования", "мы ищем кандидата", "вы нам подходите", "необходимые навыки", "наши ожидания", "что мы ждём", "стек технологий", "квалификационные требования", "чего мы ждем", "чего мы ждём", "ожидаем", "кто нам подходит", "кого мы ищем", "мы ждём", "наши требования", "что мы ожидаем", "будет плюсом", "желательно", "преимуществом"))
            {
                category = "requirements";
            }
            else if (Matches(normalized, "обязанности", "что предстоит делать", "вам предстоит", "задачи", "чем предстоит заниматься", "что делать", "что нужно делать", "обязанности и задачи"))
            {
                category = "responsibilities";
            }
            else if (Matches(normalized, "условия", "что мы предлагаем", "мы предлагаем", "условия работы", "условия труда", "что предлагаем"))
            {
                category = "conditions";
            }
            else if (Matches(normalized, "важно", "этапы отбора", "о компании", "как откликнуться", "контакты"))
            {
                continue; // skip Other
            }
            else
            {
                continue;
            }

            // Collect following siblings until the next heading
            var sb = new StringBuilder();
            var sbTarget = category switch
            {
                "requirements" => vacancy.Requirements,
                "responsibilities" => vacancy.Responsibilities,
                "conditions" => vacancy.Conditions,
                _ => string.Empty
            };

            var current = heading.NextElementSibling;
            while (current is not null)
            {
                var currentTag = current.TagName;
                // Stop at next heading
                if (currentTag is "H2" or "H3" or "H4" or "STRONG" or "B")
                {
                    var nextText = NormalizeInline(current.TextContent);
                    if (!string.IsNullOrWhiteSpace(nextText) && nextText.Length <= 120 && MatchesAlias(nextText))
                    {
                        break;
                    }
                }

                // Skip header, footer, nav, aside
                if (currentTag is "HEADER" or "FOOTER" or "NAV" or "ASIDE")
                {
                    current = current.NextElementSibling;
                    continue;
                }

                var blockText = GetTextWithBreaks(current);
                if (!string.IsNullOrWhiteSpace(blockText))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(blockText);
                }

                current = current.NextElementSibling;
            }

            var collected = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(collected))
            {
                switch (category)
                {
                    case "requirements":
                        if (string.IsNullOrWhiteSpace(vacancy.Requirements))
                        {
                            vacancy.Requirements = collected;
                            processed = true;
                        }
                        break;
                    case "responsibilities":
                        if (string.IsNullOrWhiteSpace(vacancy.Responsibilities))
                        {
                            vacancy.Responsibilities = collected;
                            processed = true;
                        }
                        break;
                    case "conditions":
                        if (string.IsNullOrWhiteSpace(vacancy.Conditions))
                        {
                            vacancy.Conditions = collected;
                            processed = true;
                        }
                        break;
                }
            }
        }

        if (processed)
        {
            Logger.Log($"[HhParser] Секции взяты из заголовков основного контента для {vacancy.Link}");
        }
    }

    private static void AppendToSection(
        string section,
        string text,
        StringBuilder intro,
        StringBuilder requirements,
        StringBuilder responsibilities,
        StringBuilder conditions,
        StringBuilder other)
    {
        var target = section switch
        {
            "requirements" => requirements,
            "responsibilities" => responsibilities,
            "conditions" => conditions,
            "other" => other,
            _ => intro
        };

        if (target.Length > 0)
        {
            target.Append('\n');
        }
        target.Append(text);
    }

    private static bool TryGetCategoryHeading(IElement element, out string category, out string? headingText)
    {
        category = string.Empty;
        headingText = null;

        var tag = element.TagName;
        string? text = null;

        if (tag is "STRONG" or "B")
        {
            text = GetText(element);
        }
        else if (tag == "P")
        {
            var strongChildren = element.QuerySelectorAll("strong, b");
            if (strongChildren.Length == 1)
            {
                var strong = strongChildren[0];
                // Heading only if the paragraph contains nothing but the strong text.
                if (NormalizeInline(element.TextContent) == NormalizeInline(strong.TextContent))
                {
                    text = GetText(strong);
                }
            }
        }
        else if (tag is "DIV" or "SECTION" or "H2" or "H3" or "H4")
        {
            var rawText = NormalizeInline(element.TextContent);
            if (string.IsNullOrWhiteSpace(rawText) || rawText.Length > 120)
            {
                return false;
            }
            // For div/section: must contain a strong/b child, or text consists only of the heading
            if (tag is "DIV" or "SECTION")
            {
                var hasStrong = element.QuerySelector("strong, b") is not null;
                var strongChildren = element.QuerySelectorAll("strong, b");
                if (!hasStrong && strongChildren.Length > 0)
                {
                    // strong inside li or nested p with other text - not a heading
                    return false;
                }
                if (!hasStrong && !MatchesAlias(rawText))
                {
                    return false;
                }
                text = rawText;
            }
            else
            {
                // h2/h3/h4 - text must match an alias
                if (!MatchesAlias(rawText))
                {
                    return false;
                }
                text = rawText;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        headingText = text;
        var normalized = text.Trim().TrimEnd(':').Trim().ToLowerInvariant();

        if (Matches(normalized, "обязанности", "что предстоит делать", "вам предстоит", "задачи", "чем предстоит заниматься", "что делать", "что нужно делать", "обязанности и задачи"))
        {
            category = "responsibilities";
            return true;
        }
        if (Matches(normalized, "требования", "мы ищем кандидата", "вы нам подходите", "необходимые навыки", "наши ожидания", "что мы ждём", "стек технологий", "квалификационные требования", "чего мы ждем", "чего мы ждём", "ожидаем", "кто нам подходит", "кого мы ищем", "мы ждём", "наши требования", "что мы ожидаем", "будет плюсом", "желательно", "преимуществом"))
        {
            category = "requirements";
            return true;
        }
        if (Matches(normalized, "условия", "что мы предлагаем", "мы предлагаем", "условия работы", "условия труда", "что предлагаем"))
        {
            category = "conditions";
            return true;
        }
        if (Matches(normalized, "важно", "этапы отбора", "о компании", "как откликнуться", "контакты"))
        {
            category = "other";
            return true;
        }

        return false;
    }

    private static bool MatchesAlias(string text)
    {
        var normalized = text.Trim().TrimEnd(':').Trim().ToLowerInvariant();
        return Matches(normalized,
            // Requirements aliases
            "требования", "мы ищем кандидата", "вы нам подходите", "необходимые навыки", "наши ожидания",
            "что мы ждём", "стек технологий", "квалификационные требования", "чего мы ждем", "чего мы ждём",
            "ожидаем", "кто нам подходит", "кого мы ищем", "мы ждём", "наши требования", "что мы ожидаем",
            "будет плюсом", "желательно", "преимуществом",
            // Responsibilities aliases
            "обязанности", "что предстоит делать", "вам предстоит", "задачи", "чем предстоит заниматься",
            "что делать", "что нужно делать", "обязанности и задачи",
            // Conditions aliases
            "условия", "что мы предлагаем", "мы предлагаем", "условия работы", "условия труда", "что предлагаем",
            // Other aliases
            "важно", "этапы отбора", "о компании", "как откликнуться", "контакты");
    }

    private static bool IsOtherText(string text)
    {
        var firstLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim()
            .TrimStart('-', ' ')
            .ToLowerInvariant() ?? string.Empty;

        return firstLine.StartsWith("о компании", StringComparison.Ordinal)
            || firstLine.StartsWith("откликайтесь", StringComparison.Ordinal)
            || firstLine.StartsWith("этапы отбора", StringComparison.Ordinal)
            || firstLine.StartsWith("важно", StringComparison.Ordinal)
            || firstLine.StartsWith("если вам близки", StringComparison.Ordinal);
    }

    private static bool Matches(string text, params string[] prefixes) =>
        prefixes.Any(p => text.Equals(p, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string GetDdOrLabeledText(IDocument document, string dataQa, string labelPrefix)
    {
        var dd = document.QuerySelector($"[data-qa='{dataQa}'] dd");
        if (dd is not null)
        {
            var ddText = GetText(dd);
            if (!string.IsNullOrWhiteSpace(ddText))
            {
                return ddText;
            }
        }

        var element = document.QuerySelector($"[data-qa='{dataQa}']");
        var text = GetText(element);
        if (!string.IsNullOrWhiteSpace(text)
            && text.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[labelPrefix.Length..].Trim().TrimStart(':').Trim();
        }
        return text;
    }

    private static string GetHiringFormats(IDocument document)
    {
        var values = document.QuerySelectorAll("[data-qa='vacancy-hiring-formats'] dd")
            .Select(GetText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (values.Count == 0)
        {
            values = document.QuerySelectorAll("[data-qa='vacancy-hiring-formats'] li")
                .Select(GetText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        if (values.Count > 0)
        {
            return string.Join("; ", values);
        }

        var element = document.QuerySelector("[data-qa='vacancy-hiring-formats']");
        var text = GetText(element);
        if (!string.IsNullOrWhiteSpace(text)
            && text.StartsWith("Оформление", StringComparison.OrdinalIgnoreCase))
        {
            text = text["Оформление".Length..].Trim().TrimStart(':').Trim();
        }
        return text;
    }

    private static List<string> GetKeySkills(IDocument document)
    {
        var skills = document.QuerySelectorAll("[data-qa='skills-element']")
            .Select(GetText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (skills.Count == 0)
        {
            skills = document.QuerySelectorAll("ul[class*='vacancy-skill-list'] li")
                .Select(GetText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        return skills
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetPublishedAt(IDocument document)
    {
        var time = document.QuerySelector("time");
        var text = GetText(time);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var meta = document.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
        var match = Regex.Match(meta, @"Дата публикации:\s*(.+?)\.\s*$");
        return match.Success ? NormalizeInline(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractPageSalary(IDocument document)
    {
        var salaryElement = document.QuerySelector("[data-qa='vacancy-salary']");
        var salary = GetText(salaryElement);
        if (!string.IsNullOrWhiteSpace(salary))
        {
            return NormalizeSalaryText(salary);
        }

        var net = document.QuerySelector("[data-qa='vacancy-salary-compensation-type-net']");
        salary = GetText(net);
        if (!string.IsNullOrWhiteSpace(salary))
        {
            return NormalizeSalaryText(salary);
        }

        var gross = document.QuerySelector("[data-qa='vacancy-salary-compensation-type-gross']");
        salary = GetText(gross);
        if (!string.IsNullOrWhiteSpace(salary))
        {
            return NormalizeSalaryText(salary);
        }

        salary = ExtractSalaryFromMeta(document.QuerySelector("meta[name='description']")?.GetAttribute("content"));
        if (!string.IsNullOrWhiteSpace(salary))
        {
            return salary;
        }

        salary = ExtractSalaryFromMeta(document.QuerySelector("meta[property='og:description']")?.GetAttribute("content"));
        return string.IsNullOrWhiteSpace(salary) ? "не указана" : salary;
    }

    private static string ExtractSalaryFromMeta(string? meta)
    {
        if (string.IsNullOrWhiteSpace(meta))
        {
            return string.Empty;
        }

        var match = Regex.Match(meta, @"Зарплата:\s*(.+?)\.\s");
        if (!match.Success)
        {
            return string.Empty;
        }

        var value = NormalizeSalaryText(match.Groups[1].Value);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    private static string GetText(IElement? element) =>
        element is null ? string.Empty : NormalizeInline(element.TextContent);

    private static string NormalizeInline(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        text = text.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string NormalizeMultiline(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        text = text.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @"\n{2,}(?=- )", "\n");
        return text.Trim();
    }

    private static string GetTextWithBreaks(IElement element)
    {
        var sb = new StringBuilder();
        AppendNodeText(element, sb);
        return NormalizeMultiline(sb.ToString());
    }

    private static void AppendNodeText(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText text)
            {
                sb.Append(text.Data);
                continue;
            }

            if (child is not IElement element)
            {
                continue;
            }

            var tag = element.TagName;
            if (tag == "BR")
            {
                sb.Append('\n');
                continue;
            }
            if (tag == "LI")
            {
                sb.Append("- ");
                AppendNodeText(element, sb);
                sb.Append('\n');
                continue;
            }

            AppendNodeText(element, sb);

            if (tag is "P" or "DIV" or "UL" or "OL" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6")
            {
                sb.Append('\n');
            }
        }
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

    // -------------------------------------------------------------------------
    // Fallback helpers for vacancy page fields
    // -------------------------------------------------------------------------

    private static string? GetFallbackTitle(IDocument document)
    {
        // 2) первый h1 внутри main/[role='main']/#a11y-main-content
        var h1 = document.QuerySelector("main h1, [role='main'] h1, #a11y-main-content h1")
            ?? document.QuerySelector("h1");
        if (h1 is not null)
        {
            var text = GetText(h1);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // 3) meta[property='og:title'] или <title>, regex "Вакансия (.+?) в "
        var ogTitle = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content");
        if (!string.IsNullOrWhiteSpace(ogTitle))
        {
            return NormalizeInline(ogTitle);
        }

        var titleEl = document.QuerySelector("title");
        if (titleEl is not null)
        {
            var titleText = GetText(titleEl);
            var titleMatch = Regex.Match(titleText, @"Вакансия (.+?) в ");
            if (titleMatch.Success)
            {
                return NormalizeInline(titleMatch.Groups[1].Value);
            }
            if (!string.IsNullOrWhiteSpace(titleText))
            {
                return NormalizeInline(titleText);
            }
        }

        return null;
    }

    private static string? GetFallbackCompany(IDocument document)
    {
        // 2) [data-qa='vacancy-company-name'] a
        var companyLink = document.QuerySelector("[data-qa='vacancy-company-name'] a");
        if (companyLink is not null)
        {
            var text = GetText(companyLink);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // 3) meta[name='description'] / og:description, regex "в компании (.+?)\."
        var metaDesc = document.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
        var companyMatch = Regex.Match(metaDesc, @"в компании (.+?)\.");
        if (companyMatch.Success)
        {
            return NormalizeInline(companyMatch.Groups[1].Value);
        }

        var ogDesc = document.QuerySelector("meta[property='og:description']")?.GetAttribute("content") ?? string.Empty;
        companyMatch = Regex.Match(ogDesc, @"в компании (.+?)\.");
        if (companyMatch.Success)
        {
            return NormalizeInline(companyMatch.Groups[1].Value);
        }

        return null;
    }

    private static string GetMainText(IDocument document)
    {
        var main = document.QuerySelector("main, [role='main'], #a11y-main-content, .vacancy-description, [data-qa='vacancy-description']") ?? document.Body;
        return main is not null ? main.TextContent : string.Empty;
    }

    private static (IElement? Element, string Source)? GetLongestDescriptionBlock(IDocument document)
    {
        var candidates = new List<(IElement Element, int Length)>();

        var candidateSelectors = new[]
        {
            "main p", "main div",
            "[role='main'] p", "[role='main'] div",
            "#a11y-main-content p", "#a11y-main-content div",
            ".tmpl-hh-content p", ".tmpl-hh-content div",
        };

        foreach (var selector in candidateSelectors)
        {
            foreach (var element in document.QuerySelectorAll(selector))
            {
                // Исключить header, footer, nav, aside, script, style
                if (element.TagName is "HEADER" or "FOOTER" or "NAV" or "ASIDE" or "SCRIPT" or "STYLE")
                {
                    continue;
                }

                // Исключить блоки с >40% текста внутри ссылок
                var linkTextLength = element.QuerySelectorAll("a").Sum(a => a.TextContent.Length);
                var totalTextLength = element.TextContent.Length;
                if (totalTextLength > 0 && (double)linkTextLength / totalTextLength > 0.4)
                {
                    continue;
                }

                var text = GetTextWithBreaks(element);
                if (text.Length >= 200)
                {
                    candidates.Add((element, text.Length));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.OrderByDescending(c => c.Length).First();
        return (best.Element, "longest-block");
    }

    private static List<string> GetFallbackKeySkills(IDocument document)
    {
        // Найти заголовок "Ключевые навыки" и взять ближайший следующий ul/li
        var skillsHeader = document.QuerySelectorAll("h2, h3, h4")
            .FirstOrDefault(h => h.TextContent.Contains("Ключевые навыки", StringComparison.OrdinalIgnoreCase));

        if (skillsHeader is not null)
        {
            // Попробовать ближайший следующий ul
            var nextUl = skillsHeader.NextElementSibling;
            if (nextUl is not null && nextUl.TagName == "UL")
            {
                var skills = nextUl.QuerySelectorAll("li")
                    .Select(GetText)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (skills.Count > 0)
                {
                    return skills;
                }
            }

            // Если списка нет — текст после заголовка до 300 символов, разбить по "," и ";"
            var headerParent = skillsHeader.ParentElement;
            if (headerParent is not null)
            {
                var textAfter = headerParent.TextContent;
                var headerText = skillsHeader.TextContent;
                var idx = textAfter.IndexOf(headerText, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var after = textAfter.Substring(idx + headerText.Length).Trim();
                    var skillsPart = after.Length > 300 ? after[..300] : after;
                    var skills = skillsPart
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (skills.Count > 0)
                    {
                        return skills;
                    }
                }
            }
        }

        return new List<string>();
    }

    private static string ExtractPageSalaryWithSource(IDocument document, out string source)
    {
        source = "page-block";

        // 1) [data-qa='vacancy-salary']
        var salaryElement = document.QuerySelector("[data-qa='vacancy-salary']");
        var salary = GetText(salaryElement);
        if (!string.IsNullOrWhiteSpace(salary))
        {
            var normalized = NormalizeSalaryText(salary);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        // 2) [data-qa='vacancy-salary-compensation-type-net']
        var net = document.QuerySelector("[data-qa='vacancy-salary-compensation-type-net']");
        salary = GetText(net);
        if (!string.IsNullOrWhiteSpace(salary))
        {
            source = "net";
            var normalized = NormalizeSalaryText(salary);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        // 3) [data-qa='vacancy-salary-compensation-type-gross']
        var gross = document.QuerySelector("[data-qa='vacancy-salary-compensation-type-gross']");
        salary = GetText(gross);
        if (!string.IsNullOrWhiteSpace(salary))
        {
            source = "gross";
            var normalized = NormalizeSalaryText(salary);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        // 4) meta[name='description'], regex "Зарплата:\s*(.+?)\.\s"
        salary = ExtractSalaryFromMeta(document.QuerySelector("meta[name='description']")?.GetAttribute("content"));
        if (!string.IsNullOrWhiteSpace(salary))
        {
            source = "meta";
            return salary;
        }

        // 5) meta[property='og:description'], тот же regex
        salary = ExtractSalaryFromMeta(document.QuerySelector("meta[property='og:description']")?.GetAttribute("content"));
        if (!string.IsNullOrWhiteSpace(salary))
        {
            source = "meta";
            return salary;
        }

        // 6) mainText regex по валюте и цифрам
        source = "bodytext";
        var mainText = GetMainText(document);
        var salaryMatch = Regex.Match(mainText, @"\d[\d\s\u00A0\u202F]*(\s*[–-]\s*\d[\d\s]*)?\s*(₽|\$|€|₸|руб|USD|EUR)");
        if (salaryMatch.Success)
        {
            var value = NormalizeSalaryText(salaryMatch.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        source = "card";
        return "не указана";
    }

    /// <summary>
    /// Агрегированная сводка по прогону парсинга вакансий.
    /// </summary>
    public class ParserRunSummary
    {
        public int Total { get; set; }
        public int WithRequirements { get; set; }
        public int WithResponsibilities { get; set; }
        public int WithKeySkills { get; set; }
        public int WithConditions { get; set; }
        public int DescriptionFallback { get; set; }

        public int Percent(int count)
        {
            if (Total == 0) return 0;
            return (int)Math.Round(count * 100.0 / Total);
        }

        public string ToLogLine()
        {
            return
                $"[HhParser] Сводка за прогон: вакансий={Total}, " +
                $"с requirements={WithRequirements}/{Total} ({Percent(WithRequirements)}%), " +
                $"с responsibilities={WithResponsibilities}/{Total} ({Percent(WithResponsibilities)}%), " +
                $"с keySkills={WithKeySkills}/{Total} ({Percent(WithKeySkills)}%), " +
                $"с conditions={WithConditions}/{Total} ({Percent(WithConditions)}%), " +
                $"descriptionFallback=True: {DescriptionFallback}/{Total} ({Percent(DescriptionFallback)}%).";
        }

        /// <summary>
        /// Строит сводку по списку вакансий.
        /// </summary>
        public static ParserRunSummary BuildRunSummary(IReadOnlyList<Vacancy> vacancies)
        {
            var summary = new ParserRunSummary { Total = vacancies.Count };

            foreach (var v in vacancies)
            {
                if (!string.IsNullOrWhiteSpace(v.Requirements)) summary.WithRequirements++;
                if (!string.IsNullOrWhiteSpace(v.Responsibilities)) summary.WithResponsibilities++;
                if (v.KeySkills.Count > 0) summary.WithKeySkills++;
                if (!string.IsNullOrWhiteSpace(v.Conditions)) summary.WithConditions++;
                if (string.IsNullOrWhiteSpace(v.Requirements)) summary.DescriptionFallback++;
            }

            return summary;
        }
    }
}
