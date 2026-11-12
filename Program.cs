using System.Collections.Generic;
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
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json", optional: true)
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

            var analyzer = new JobAnalyzer(authKey, resumeText, profileText, contacts, config);

            var notifier = new TelegramNotifier(botToken, chatId);

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
                    // 5–6 — в дайджест «на подумать» (лог только)
                    // <= 4 — только в лог
                    if (result.Score >= 7)
                    {
                        await notifier.SendVacancyAsync(vacancy, result);
                    }
                    else if (result.Score >= 5)
                    {
                        Logger.Log($"[Дайджест] {vacancy.Title} | {vacancy.Link} | оценка {result.Score}/10 — на подумать.");
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
}
