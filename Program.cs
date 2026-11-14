using System.Collections.Generic;
using System.Globalization;
using AiRecruiter.Analyzers;
using AiRecruiter.Models;
using AiRecruiter.Notifiers;
using AiRecruiter.Parsers;
using Microsoft.Extensions.Configuration;

namespace AiRecruiter;

public class Program
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
                if (JobAnalyzer.TryParseAndValidateDto(raw, out var dto, out var reason))
                {
                    Console.WriteLine($"OK score={dto!.Score} | {Path.GetFileName(file)}");
                }
                else
                {
                    Console.WriteLine($"FAIL {reason} | {Path.GetFileName(file)}");
                }
            }

            return;
        }

        Logger.Log("=== Запуск программы ===");

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
            if (string.IsNullOrWhiteSpace(fio)) missingContacts.Add("Contacts:Fio");
            if (string.IsNullOrWhiteSpace(tg)) missingContacts.Add("Contacts:Tg");
            if (string.IsNullOrWhiteSpace(phone)) missingContacts.Add("Contacts:Phone");
            if (string.IsNullOrWhiteSpace(email)) missingContacts.Add("Contacts:Email");

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
            long chatId = long.Parse(chatIdStr);

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

            // Load already-seen vacancy links before parsing so the parser can filter them out.
            var seenVacancies = new HashSet<string>();
            if (File.Exists("seen_vacancies.txt"))
            {
                var lines = await File.ReadAllLinesAsync("seen_vacancies.txt");
                foreach (var line in lines) seenVacancies.Add(line.Trim());
            }

            var parser = new HhParser();
            var vacancies = await parser.ParseVacanciesAsync(searchQuery, areaCode, workFormat, seenVacancies, 10);

            Logger.Log($"Found {vacancies.Count} new vacancies.");

            var summary = HhParser.ParserRunSummary.BuildRunSummary(vacancies);
            Logger.Log(summary.ToLogLine());

            // Читаем пороги из конфигурации с значениями по умолчанию.
            int minVacanciesForAlert = ReadIntConfig(config, "HhParser:Thresholds:MinVacanciesForAlert", 5);
            int requirementsPercent = ReadIntConfig(config, "HhParser:Thresholds:RequirementsPercent", 80);
            int descriptionPercent = ReadIntConfig(config, "HhParser:Thresholds:DescriptionPercent", 80);
            int responsibilitiesPercent = ReadIntConfig(config, "HhParser:Thresholds:ResponsibilitiesPercent", 10);
            int keySkillsPercent = ReadIntConfig(config, "HhParser:Thresholds:KeySkillsPercent", 10);
            int conditionsPercent = ReadIntConfig(config, "HhParser:Thresholds:ConditionsPercent", 10);

            var warnings = new List<string>();

            if (summary.Total == 0)
            {
                Logger.Log("[HhParser] ВНИМАНИЕ: за прогон не собрано ни одной вакансии — проверьте селекторы или seen_vacancies.txt.");
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

            var notifier = new TelegramNotifier(botToken, chatId);

            if (warnings.Count > 0)
            {
                var alertText = "⚠️ HhParser: подозрение на смену вёрстки.\n" + string.Join("\n", warnings) + "\nПроверьте селекторы.";
                await notifier.SendParserAlertAsync(alertText);
                Logger.Log("[HhParser] ALERT отправлен в Telegram.");
            }

            foreach (var vacancy in vacancies)
            {
                if (seenVacancies.Contains(vacancy.Link))
                {
                    Logger.Log($"[Пропущено] Уже видели: {vacancy.Link}");
                    continue;
                }

                try
                {
                    AnalysisResult result = await analyzer.AnalyzeVacancyAsync(vacancy);

                    if (result.Score == 0)
                    {
                        Logger.Log($"[Пропущено] {vacancy.Title} | {vacancy.Link} | {result.Comment}");
                    }
                    else
                    {
                        Logger.Log($"[Оценка: {result.Score}/10] {vacancy} - {result.Comment}");
                    }

                    // Политика отправки (Prompt 5.9):
                    // score >= 7 — сразу в Telegram
                    if (result.Score >= 3)
                    {
                        var priorityLabel = result.Score >= 7 ? "🔥 откликаться" : "💭 на подумать";
                        await notifier.SendVacancyAsync(vacancy, result, priorityLabel);
                    }

                    seenVacancies.Add(vacancy.Link);
                    await File.AppendAllTextAsync("seen_vacancies.txt", vacancy.Link + Environment.NewLine);
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

    private static int ReadIntConfig(IConfiguration? config, string key, int defaultValue)
    {
        var raw = config?[key];
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
