using System.Collections.Generic;
using System.Globalization;
using AiRecruiter.Analyzers;
using AiRecruiter.Configuration;
using AiRecruiter.Filters;
using AiRecruiter.Models;
using AiRecruiter.Notifiers;
using AiRecruiter.Parsers;
using AiRecruiter.Storage;
using Microsoft.Extensions.Configuration;

namespace AiRecruiter;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        // Офлайн-проверка парсера на локальных HTML из samples/
        if (args.Length >= 2 && args[0] == "--parse-samples")
        {
            var dir = args[1];
            if (!Directory.Exists(dir))
            {
                Console.WriteLine($"Папка не найдена: {dir}");
                return;
            }

            foreach (var file in Directory.GetFiles(dir, "*.html").OrderBy(f => f))
            {
                var html = await File.ReadAllTextAsync(file);
                var vacancy = await HhParser.ParseVacancyHtmlAsync(html, Path.GetFileName(file));

                Console.WriteLine($"=== {Path.GetFileName(file)} ===");
                Console.WriteLine($"Title: {vacancy.Title}");
                Console.WriteLine($"Company: {vacancy.Company}");
                Console.WriteLine($"EmployerId: {vacancy.EmployerId}");
                Console.WriteLine($"Salary: {vacancy.Salary}");
                Console.WriteLine($"Experience: {vacancy.Experience}");
                Console.WriteLine($"WorkFormat: {vacancy.WorkFormat}");
                Console.WriteLine($"Schedule: {vacancy.Schedule}");
                Console.WriteLine($"Employment: {vacancy.Employment}");
                Console.WriteLine($"Location: {vacancy.Location}");
                Console.WriteLine($"HiringFormats: {vacancy.HiringFormats}");
                Console.WriteLine($"PublishedAt: {vacancy.PublishedAt}");
                Console.WriteLine($"KeySkills ({vacancy.KeySkills.Count}): {string.Join(", ", vacancy.KeySkills)}");
                Console.WriteLine($"Requirements: {vacancy.Requirements.Length} симв.");
                Console.WriteLine($"Responsibilities: {vacancy.Responsibilities.Length} симв.");
                Console.WriteLine($"Conditions: {vacancy.Conditions.Length} симв.");
                Console.WriteLine($"Description: {vacancy.Description.Length} симв.");
                Console.WriteLine($"TechStack ({vacancy.TechStack.Length}): {vacancy.TechStack}");
                Console.WriteLine();
            }

            return;
        }

        // Режим replay для детерминированной проверки парсинга ответов GigaChat
        if (args.Length >= 2 && args[0] == "--replay-failed")
        {
            var dir = args[1];
            if (!Directory.Exists(dir))
            {
                Console.WriteLine($"Папка не найдена: {dir}");
                return;
            }

            foreach (var file in Directory.GetFiles(dir, "*.txt").OrderBy(f => f))
            {
                var raw = await File.ReadAllTextAsync(file);
                if (JobAnalyzer.TryParseAndValidateDto(raw, out var dto, out var reason) && dto is not null)
                {
                    Console.WriteLine($"OK score={dto.Score} | {Path.GetFileName(file)}");
                }
                else
                {
                    Console.WriteLine($"FAIL {reason} | {Path.GetFileName(file)}");
                }
            }

            return;
        }

        Logger.Log("=== Запуск программы ===");

        // Первый Ctrl+C — дообработать текущую вакансию и выйти (иначе kill между отправкой
        // в Telegram и записью статуса в SQLite даёт дубль уведомления). Второй — жёсткий выход.
        // Без using: обработчик живёт до конца процесса и может сработать после выхода из Main.
        var stopCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            if (!stopCts.IsCancellationRequested)
            {
                e.Cancel = true;
                stopCts.Cancel();
                Logger.Log("Получен Ctrl+C: остановка после текущей вакансии (повторный Ctrl+C — немедленный выход).");
            }
        };

        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var authKey = Environment.GetEnvironmentVariable("GIGACHAT_AUTH_KEY");
            var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            var chatIdStr = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            var searchQuery = config["Search:Query"];
            var areaCode = config["Search:AreaCode"];

            var fio = config["Contacts:Fio"];
            var tg = config["Contacts:Tg"];
            var phone = config["Contacts:Phone"];
            var email = config["Contacts:Email"];

            var missingContacts = new List<string>();
            if (string.IsNullOrWhiteSpace(fio))
            {
                missingContacts.Add("Contacts:Fio");
            }

            if (string.IsNullOrWhiteSpace(tg))
            {
                missingContacts.Add("Contacts:Tg");
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                missingContacts.Add("Contacts:Phone");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                missingContacts.Add("Contacts:Email");
            }

            if (missingContacts.Count > 0)
            {
                Logger.Log($"Ошибка: не заданы контакты: {string.Join(", ", missingContacts)}. Укажите их в appsettings.json или через переменные окружения Contacts__Fio, Contacts__Tg, Contacts__Phone, Contacts__Email.");
                return;
            }

            var contacts = $"{fio},\ntg: {tg},\ntel: {phone},\nemail: {email}";

            string resumeText = File.Exists("resume.txt") ? await File.ReadAllTextAsync("resume.txt") : "Резюме не найдено.";
            string profileText = File.Exists("profile.md") ? await File.ReadAllTextAsync("profile.md") : "Профиль не найден.";
            string candidateSummary = File.Exists("candidate_summary.md") ? await File.ReadAllTextAsync("candidate_summary.md") : "Сводка кандидата не найдена.";

            if (string.IsNullOrEmpty(authKey))
            {
                Console.WriteLine("Ошибка: переменная окружения GIGACHAT_AUTH_KEY не установлена.");
                return;
            }
            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatIdStr))
            {
                Console.WriteLine("Ошибка: переменные TELEGRAM_BOT_TOKEN или TELEGRAM_CHAT_ID не установлены.");
                return;
            }
            if (!long.TryParse(chatIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
            {
                Console.WriteLine($"Ошибка: TELEGRAM_CHAT_ID должен быть целым числом, получено '{chatIdStr}'.");
                return;
            }

            if (string.IsNullOrEmpty(searchQuery)
                || string.IsNullOrEmpty(areaCode)
                || string.IsNullOrEmpty(resumeText)
                || string.IsNullOrEmpty(profileText))
            {
                Logger.Log("Ошибка: не заданы все необходимые значения конфигурации.");
                Logger.Log("Пожалуйста, укажите Search:Query, Search:AreaCode и проверьте содержимое файлов resume.txt и profile.md.");
                return;
            }

            string workFormat;
            if (areaCode == "113") // Russia
            {
                workFormat = "REMOTE"; // Only remote for all of Russia
            }
            else if (areaCode == "4") // Novosibirsk
            {
                workFormat = ""; // Any format (remote + office + hybrid) for Novosibirsk
            }
            else
            {
                workFormat = "REMOTE"; // Default fallback for other regions
            }

            // Состояние вакансий в SQLite.
            var repository = new VacancyRepository("vacancies.db");
            repository.Initialize();

            int minScore = IntConfigReader.Read(config, "Analyzer:MinScore", 3);

            var skipLinks = new HashSet<string>(repository.GetLinksToSkip(), StringComparer.Ordinal);

            var vacancies = await HhParser.ParseVacanciesAsync(searchQuery, areaCode, workFormat, skipLinks, 10);

            Logger.Log($"Found {vacancies.Count} new vacancies.");

            var summary = HhParser.ParserRunSummary.BuildRunSummary(vacancies);
            Logger.Log(summary.ToLogLine());

            // Читаем пороги из конфигурации с значениями по умолчанию.
            int minVacanciesForAlert = IntConfigReader.Read(config, "HhParser:Thresholds:MinVacanciesForAlert", 5);
            int requirementsPercent = IntConfigReader.Read(config, "HhParser:Thresholds:RequirementsPercent", 80);
            int descriptionPercent = IntConfigReader.Read(config, "HhParser:Thresholds:DescriptionPercent", 80);
            int responsibilitiesPercent = IntConfigReader.Read(config, "HhParser:Thresholds:ResponsibilitiesPercent", 10);
            int keySkillsPercent = IntConfigReader.Read(config, "HhParser:Thresholds:KeySkillsPercent", 10);
            int conditionsPercent = IntConfigReader.Read(config, "HhParser:Thresholds:ConditionsPercent", 10);
            int minSalary = IntConfigReader.Read(config, "Filters:MinSalary", 0);
            var excludeTitleWords = StringListConfigReader.Read(config, "Filters:ExcludeTitleWords");

            var warnings = new List<string>();

            if (summary.Total == 0)
            {
                Logger.Log("[HhParser] ВНИМАНИЕ: за прогон не собрано ни одной вакансии — проверьте селекторы.");
            }
            else if (summary.Total >= minVacanciesForAlert)
            {
                // Обязательные поля: Requirements, Description
                int reqPercent = summary.Percent(summary.WithRequirements);
                if (reqPercent < requirementsPercent)
                {
                    var w = $"Requirements={reqPercent}% (порог {requirementsPercent}%)";
                    warnings.Add(w);
                    Logger.Log($"[HhParser] ВНИМАНИЕ: Requirements заполнено в {reqPercent}% вакансий (порог {requirementsPercent}%). Возможна смена вёрстки hh.ru.");
                }

                int fallbackPercent = summary.Percent(summary.DescriptionFallback);
                int descriptionPresentPercent = 100 - fallbackPercent;
                if (descriptionPresentPercent < descriptionPercent)
                {
                    var w = $"Description={descriptionPresentPercent}% (порог {descriptionPercent}%), fallback={fallbackPercent}%";
                    warnings.Add(w);
                    Logger.Log($"[HhParser] ВНИМАНИЕ: Description загружено в {descriptionPresentPercent}% вакансий (порог {descriptionPercent}%), descriptionFallback=True: {fallbackPercent}%. Возможна смена вёрстки hh.ru.");
                }

                // Вариативные поля: Responsibilities, KeySkills, Conditions
                int respPercent = summary.Percent(summary.WithResponsibilities);
                if (respPercent <= responsibilitiesPercent)
                {
                    var w = $"Responsibilities={respPercent}% (порог {responsibilitiesPercent}%)";
                    warnings.Add(w);
                    Logger.Log($"[HhParser] ВНИМАНИЕ: Responsibilities заполнено в {respPercent}% вакансий (порог {responsibilitiesPercent}%). Возможна смена вёрстки hh.ru.");
                }

                int skillsPercent = summary.Percent(summary.WithKeySkills);
                if (skillsPercent <= keySkillsPercent)
                {
                    var w = $"KeySkills={skillsPercent}% (порог {keySkillsPercent}%)";
                    warnings.Add(w);
                    Logger.Log($"[HhParser] ВНИМАНИЕ: KeySkills заполнено в {skillsPercent}% вакансий (порог {keySkillsPercent}%). Возможна смена вёрстки hh.ru.");
                }

                int condPercent = summary.Percent(summary.WithConditions);
                if (condPercent <= conditionsPercent)
                {
                    var w = $"Conditions={condPercent}% (порог {conditionsPercent}%)";
                    warnings.Add(w);
                    Logger.Log($"[HhParser] ВНИМАНИЕ: Conditions заполнено в {condPercent}% вакансий (порог {conditionsPercent}%). Возможна смена вёрстки hh.ru.");
                }
            }

            var analyzer = new JobAnalyzer(authKey, resumeText, profileText, contacts, config);

            using var notifier = new TelegramNotifier(botToken, chatId);

            if (warnings.Count > 0)
            {
                var alertText = "⚠️ HhParser: подозрение на смену вёрстки.\n" + string.Join("\n", warnings) + "\nПроверьте селекторы.";
                await notifier.SendParserAlertAsync(alertText);
                Logger.Log("[HhParser] ALERT отправлен в Telegram.");
            }

            // Повторная отправка из сохранённого результата — без GigaChat.
            await RetryPendingSendsAsync(repository, notifier, minScore);

            int salaryFiltered = 0;
            var scoreCounts = new int[11];
            int sentCount = 0;
            int belowThresholdCount = 0;

            foreach (var vacancy in vacancies)
            {
                if (stopCts.IsCancellationRequested)
                {
                    Logger.Log("[Остановка] Прогон прерван пользователем.");
                    break;
                }

                if (skipLinks.Contains(vacancy.Link))
                {
                    Logger.Log($"[Пропущено] Уже видели: {vacancy.Link}");
                    continue;
                }

                var nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                var excludedWord = TitleFilter.FindExcludedWord(vacancy.Title, excludeTitleWords);
                if (excludedWord is not null)
                {
                    Logger.Log($"[Пропущено] Стоп-слово '{excludedWord}': {vacancy.Title} | {vacancy.Link}");
                    repository.Upsert(new VacancyRecord(
                        Link: vacancy.Link,
                        EmployerId: string.IsNullOrWhiteSpace(vacancy.EmployerId) ? null : vacancy.EmployerId,
                        Title: vacancy.Title,
                        Company: vacancy.Company,
                        Salary: vacancy.Salary,
                        Score: null,
                        Status: "filtered_stopword",
                        FirstSeen: nowIso,
                        AnalyzedAt: null,
                        FailReason: null,
                        Comment: null,
                        CoverLetter: null,
                        TechStack: vacancy.TechStack,
                        Experience: ToDbValue(vacancy.Experience),
                        Employment: ToDbValue(vacancy.Employment),
                        WorkFormat: ToDbValue(vacancy.WorkFormat),
                        PublishedAt: ToDbValue(vacancy.PublishedAt)));
                    skipLinks.Add(vacancy.Link);
                    continue;
                }

                int? salaryLowerBound = SalaryFilter.TryParseLowerBoundRub(vacancy.Salary);
                if (salaryLowerBound is int lowerBound && SalaryFilter.ShouldSkip(vacancy.Salary, minSalary))
                {
                    Logger.Log($"[Пропущено] Зарплата ниже порога ({lowerBound} < {minSalary}): {vacancy.Title} | {vacancy.Link}");
                    repository.Upsert(new VacancyRecord(
                        Link: vacancy.Link,
                        EmployerId: string.IsNullOrWhiteSpace(vacancy.EmployerId) ? null : vacancy.EmployerId,
                        Title: vacancy.Title,
                        Company: vacancy.Company,
                        Salary: vacancy.Salary,
                        Score: null,
                        Status: "filtered_salary",
                        FirstSeen: nowIso,
                        AnalyzedAt: null,
                        FailReason: null,
                        Comment: null,
                        CoverLetter: null,
                        TechStack: vacancy.TechStack,
                        Experience: ToDbValue(vacancy.Experience),
                        Employment: ToDbValue(vacancy.Employment),
                        WorkFormat: ToDbValue(vacancy.WorkFormat),
                        PublishedAt: ToDbValue(vacancy.PublishedAt)));
                    skipLinks.Add(vacancy.Link);
                    salaryFiltered++;
                    continue;
                }

                // Строка создаётся до анализа со статусом new; при исключении она
                // останется new без fail_reason (обработка — шаг 6).
                repository.Upsert(new VacancyRecord(
                    Link: vacancy.Link,
                    EmployerId: string.IsNullOrWhiteSpace(vacancy.EmployerId) ? null : vacancy.EmployerId,
                    Title: vacancy.Title,
                    Company: vacancy.Company,
                    Salary: vacancy.Salary,
                    Score: null,
                    Status: "new",
                    FirstSeen: nowIso,
                    AnalyzedAt: null,
                    FailReason: null,
                    Comment: null,
                    CoverLetter: null,
                    TechStack: vacancy.TechStack,
                    Experience: ToDbValue(vacancy.Experience),
                    Employment: ToDbValue(vacancy.Employment),
                    WorkFormat: ToDbValue(vacancy.WorkFormat),
                    PublishedAt: ToDbValue(vacancy.PublishedAt)));

                try
                {
                    AnalysisResult result = await analyzer.AnalyzeVacancyAsync(vacancy);

                    if (result.Score == 0)
                    {
                        Logger.Log($"[Пропущено] {vacancy.Title} | {vacancy.Link} | {result.Comment}");
                        repository.Upsert(new VacancyRecord(
                            Link: vacancy.Link,
                            EmployerId: string.IsNullOrWhiteSpace(vacancy.EmployerId) ? null : vacancy.EmployerId,
                            Title: vacancy.Title,
                            Company: vacancy.Company,
                            Salary: vacancy.Salary,
                            Score: null,
                            Status: "analysis_failed",
                            FirstSeen: nowIso,
                            AnalyzedAt: DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            FailReason: result.Comment,
                            Comment: null,
                            CoverLetter: null,
                            TechStack: vacancy.TechStack,
                            Experience: ToDbValue(vacancy.Experience),
                            Employment: ToDbValue(vacancy.Employment),
                            WorkFormat: ToDbValue(vacancy.WorkFormat),
                            PublishedAt: ToDbValue(vacancy.PublishedAt)));
                        skipLinks.Add(vacancy.Link);
                    }
                    else if (result.Score < minScore)
                    {
                        Logger.Log($"[Оценка: {result.Score}/10] {vacancy} - {result.Comment}");
                        Logger.Log($"[Оценка ниже порога] score={result.Score} < {minScore}: {vacancy.Title} | {vacancy.Link}");
                        repository.Upsert(new VacancyRecord(
                            Link: vacancy.Link,
                            EmployerId: string.IsNullOrWhiteSpace(vacancy.EmployerId) ? null : vacancy.EmployerId,
                            Title: vacancy.Title,
                            Company: vacancy.Company,
                            Salary: vacancy.Salary,
                            Score: result.Score,
                            Status: "analyzed_low",
                            FirstSeen: nowIso,
                            AnalyzedAt: DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            FailReason: null,
                            Comment: result.Comment,
                            CoverLetter: result.CoverLetter,
                            TechStack: vacancy.TechStack,
                            Experience: ToDbValue(vacancy.Experience),
                            Employment: ToDbValue(vacancy.Employment),
                            WorkFormat: ToDbValue(vacancy.WorkFormat),
                            PublishedAt: ToDbValue(vacancy.PublishedAt)));
                        skipLinks.Add(vacancy.Link);
                        scoreCounts[result.Score]++;
                        belowThresholdCount++;
                    }
                    else
                    {
                        Logger.Log($"[Оценка: {result.Score}/10] {vacancy} - {result.Comment}");
                        repository.Upsert(new VacancyRecord(
                            Link: vacancy.Link,
                            EmployerId: string.IsNullOrWhiteSpace(vacancy.EmployerId) ? null : vacancy.EmployerId,
                            Title: vacancy.Title,
                            Company: vacancy.Company,
                            Salary: vacancy.Salary,
                            Score: result.Score,
                            Status: "analyzed_ok",
                            FirstSeen: nowIso,
                            AnalyzedAt: DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                            FailReason: null,
                            Comment: result.Comment,
                            CoverLetter: result.CoverLetter,
                            TechStack: vacancy.TechStack,
                            Experience: ToDbValue(vacancy.Experience),
                            Employment: ToDbValue(vacancy.Employment),
                            WorkFormat: ToDbValue(vacancy.WorkFormat),
                            PublishedAt: ToDbValue(vacancy.PublishedAt)));

                        // Политика отправки: score >= minScore — в Telegram;
                        // метка 🔥 при score >= 7, иначе 💭.
                        var priorityLabel = result.Score >= 7 ? "🔥 откликаться" : "💭 на подумать";
                        bool sent = await notifier.SendVacancyAsync(vacancy, result, priorityLabel);
                        repository.SetStatus(vacancy.Link, sent ? "sent" : "send_failed", result.Score);
                        if (sent)
                        {
                            sentCount++;
                        }

                        skipLinks.Add(vacancy.Link);
                        scoreCounts[result.Score]++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Ошибка анализа] {vacancy.Title} | {vacancy.Link}: {ex}");
                }
            }

            if (salaryFiltered > 0)
            {
                Logger.Log($"[ИТОГ] Отфильтровано по зарплате: {salaryFiltered}");
            }

            var distribution = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"{i}:{scoreCounts[i]}"));
            Logger.Log($"[ИТОГ] Распределение оценок: {distribution} | отправлено: {sentCount} | ниже порога: {belowThresholdCount}");

            var jsonFailures = JobAnalyzer.GetJsonFailureCount();
            if (jsonFailures > 0)
            {
                Logger.Log($"[ИТОГ] Неудач парсинга JSON за прогон: {jsonFailures}");
            }

            Console.WriteLine("Работа завершена. Нажмите Enter для выхода.");
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка: {ex}");
        }
        finally
        {
            Logger.Log("=== Завершение программы ===");
        }
    }

    /// <summary>Пустая строка в БД хранится как null.</summary>
    private static string? ToDbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Повторная отправка в Telegram из сохранённого результата анализа
    /// (статусы send_failed/analyzed_ok) — без вызова GigaChat.
    /// </summary>
    private static async Task RetryPendingSendsAsync(
        VacancyRepository repository,
        TelegramNotifier notifier,
        int minScore)
    {
        foreach (var record in repository.GetPendingSendRetries())
        {
            if (record.Score < minScore)
            {
                repository.SetStatus(record.Link, "analyzed_low", record.Score);
                Logger.Log($"[Повтор] Ниже нового порога: score={record.Score} < {minScore} | {record.Title} | {record.Link}");
                continue;
            }

            var vacancy = new Vacancy
            {
                Link = record.Link,
                Title = record.Title ?? string.Empty,
                Company = record.Company ?? string.Empty,
                Salary = record.Salary ?? string.Empty,
                TechStack = record.TechStack ?? string.Empty,
            };

            var result = new AnalysisResult
            {
                Score = record.Score ?? 0,
                Comment = record.Comment ?? string.Empty,
                CoverLetter = record.CoverLetter ?? string.Empty,
            };

            var label = result.Score >= 7 ? "🔥 откликаться" : "💭 на подумать";
            bool sent = await notifier.SendVacancyAsync(vacancy, result, label);
            repository.SetStatus(record.Link, sent ? "sent" : "send_failed", result.Score);

            if (sent)
            {
                Logger.Log($"[Повтор] Отправлено: score={result.Score} | {vacancy.Title} | {vacancy.Link}");
            }
            else
            {
                Logger.Log($"[Повтор] Ошибка отправки: score={result.Score} | {vacancy.Title} | {vacancy.Link}");
            }
        }
    }
}

