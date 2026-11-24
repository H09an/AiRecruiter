using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AiRecruiter.Storage;

/// <summary>
/// Запись о вакансии в SQLite. Status — одна из строк статуса жизненного цикла
/// (см. VacancyStatus). Comment/CoverLetter/TechStack — сохранённый результат
/// анализа, необходимый для повторной отправки без GigaChat. Счётчик попыток
/// attempts в записи не передаётся: его меняет только SaveAnalysis.
/// </summary>
internal sealed record VacancyRecord(
    string Link,
    string? EmployerId,
    string? Title,
    string? Company,
    string? Salary,
    int? Score,
    string Status,
    string FirstSeen,
    string? AnalyzedAt,
    string? FailReason,
    string? Comment = null,
    string? CoverLetter = null,
    string? TechStack = null,
    string? Experience = null,
    string? Employment = null,
    string? WorkFormat = null,
    string? PublishedAt = null,
    string? Description = null);

/// <summary>
/// Хранилище состояния вакансий в SQLite (vacancies.db). Соединение открывается
/// на каждую операцию; повторная обработка существующей ссылки запрещена —
/// GetAllLinks возвращает ВСЕ строки, включая filtered_*.
/// </summary>
internal sealed class VacancyRepository
{
    /// <summary>Лимит длины fail_reason.</summary>
    internal const int FailReasonMaxLength = 500;

    /// <summary>Число неудачных попыток анализа, после которых ставится analysis_abandoned.</summary>
    internal const int MaxAnalysisAttempts = 3;

    private readonly string _connectionString;

    public VacancyRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS vacancies (
                link TEXT PRIMARY KEY,
                employer_id TEXT,
                title TEXT,
                company TEXT,
                salary TEXT,
                score INTEGER,
                status TEXT NOT NULL,
                first_seen TEXT NOT NULL,
                analyzed_at TEXT,
                fail_reason TEXT,
                comment TEXT,
                cover_letter TEXT,
                tech_stack TEXT,
                experience TEXT,
                employment TEXT,
                work_format TEXT,
                published_at TEXT,
                description TEXT,
                attempts INTEGER NOT NULL DEFAULT 0
            );
            """, transaction: transaction);

        // Устаревшая таблица ключ-значение больше не используется: удаляем её.
        connection.Execute("DROP TABLE IF EXISTS meta", transaction: transaction);

        // Миграция legacy-схемы: добавляем отсутствующие колонки анализа.
        var existingColumns = connection
            .Query<string>("SELECT name FROM pragma_table_info('vacancies')", transaction: transaction)
            .AsList();

        foreach (var column in new[]
                 {
                     "comment", "cover_letter", "tech_stack",
                     "experience", "employment", "work_format", "published_at",
                     "description", "attempts",
                 })
        {
            if (existingColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var alterSql = column switch
            {
                "comment" => "ALTER TABLE vacancies ADD COLUMN comment TEXT",
                "cover_letter" => "ALTER TABLE vacancies ADD COLUMN cover_letter TEXT",
                "tech_stack" => "ALTER TABLE vacancies ADD COLUMN tech_stack TEXT",
                "experience" => "ALTER TABLE vacancies ADD COLUMN experience TEXT",
                "employment" => "ALTER TABLE vacancies ADD COLUMN employment TEXT",
                "work_format" => "ALTER TABLE vacancies ADD COLUMN work_format TEXT",
                "published_at" => "ALTER TABLE vacancies ADD COLUMN published_at TEXT",
                "description" => "ALTER TABLE vacancies ADD COLUMN description TEXT",
                "attempts" => "ALTER TABLE vacancies ADD COLUMN attempts INTEGER NOT NULL DEFAULT 0",
                _ => throw new InvalidOperationException($"Нет ветки миграции для колонки {column}"),
            };

            connection.Execute(alterSql, transaction: transaction);
        }

        // Legacy-записи, готовые к повторной отправке, но без сохранённого анализа:
        // повторная отправка невозможна, переводим в analysis_failed для переанализа.
        // Литералы статусов здесь — история миграции; тест на legacy-схеме сохраняется.
        connection.Execute(
            """
            UPDATE vacancies
            SET status = 'analysis_failed',
                fail_reason = 'migrated: no saved analysis'
            WHERE status IN ('send_failed','analyzed_ok')
              AND (score IS NULL OR comment IS NULL OR TRIM(comment) = '')
            """, transaction: transaction);

        // Sweep на старте прогона: analysis_failed с исчерпанным лимитом попыток
        // переводится в analysis_abandoned. fail_reason и description сохраняются.
        connection.Execute(
            $"""
            UPDATE vacancies
            SET status = @{nameof(VacancyStatus.AnalysisAbandoned)}
            WHERE status = @{nameof(VacancyStatus.AnalysisFailed)}
              AND attempts >= 3
            """,
            new
            {
                AnalysisAbandoned = VacancyStatus.AnalysisAbandoned,
                AnalysisFailed = VacancyStatus.AnalysisFailed,
            },
            transaction: transaction);

        transaction.Commit();
    }

    public IReadOnlyCollection<string> GetAllLinks()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection.Query<string>("SELECT link FROM vacancies").AsList();
    }

    public void Upsert(VacancyRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("""
            INSERT INTO vacancies (link, employer_id, title, company, salary, score, status, first_seen, analyzed_at, fail_reason, comment, cover_letter, tech_stack, experience, employment, work_format, published_at, description)
            VALUES (@Link, @EmployerId, @Title, @Company, @Salary, @Score, @Status, @FirstSeen, @AnalyzedAt, @FailReason, @Comment, @CoverLetter, @TechStack, @Experience, @Employment, @WorkFormat, @PublishedAt, @Description)
            ON CONFLICT(link) DO UPDATE SET
                employer_id = @EmployerId,
                title = @Title,
                company = @Company,
                salary = @Salary,
                score = @Score,
                status = @Status,
                analyzed_at = @AnalyzedAt,
                fail_reason = @FailReason,
                comment = @Comment,
                cover_letter = @CoverLetter,
                tech_stack = @TechStack,
                experience = @Experience,
                employment = @Employment,
                work_format = @WorkFormat,
                published_at = @PublishedAt,
                description = @Description
            """, record);
    }

    /// <summary>
    /// Сохраняет запись до анализа или отфильтрованную: статусы new,
    /// filtered_stopword, filtered_salary. Счётчик attempts сохраняет (в INSERT
    /// колонка не входит — работает DEFAULT 0, при конфликте не обновляется);
    /// description, analyzed_at, fail_reason, score, comment, cover_letter
    /// обнуляются значениями записи.
    /// </summary>
    public void SaveFiltered(VacancyRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("""
            INSERT INTO vacancies (link, employer_id, title, company, salary, score, status, first_seen, analyzed_at, fail_reason, comment, cover_letter, tech_stack, experience, employment, work_format, published_at, description)
            VALUES (@Link, @EmployerId, @Title, @Company, @Salary, @Score, @Status, @FirstSeen, @AnalyzedAt, @FailReason, @Comment, @CoverLetter, @TechStack, @Experience, @Employment, @WorkFormat, @PublishedAt, @Description)
            ON CONFLICT(link) DO UPDATE SET
                employer_id = @EmployerId,
                title = @Title,
                company = @Company,
                salary = @Salary,
                score = @Score,
                status = @Status,
                analyzed_at = @AnalyzedAt,
                fail_reason = @FailReason,
                comment = @Comment,
                cover_letter = @CoverLetter,
                tech_stack = @TechStack,
                experience = @Experience,
                employment = @Employment,
                work_format = @WorkFormat,
                published_at = @PublishedAt,
                description = @Description
            """, record);
    }

    /// <summary>
    /// Сохраняет результат анализа (analysis_failed, analyzed_low, analyzed_ok)
    /// и увеличивает attempts на 1 в той же транзакции. Причины обрезаются до
    /// 500 символов. Если после увеличения attempts >= 3, а анализ не удался
    /// (status = analysis_failed), в той же транзакции ставится
    /// analysis_abandoned; fail_reason и description сохраняются.
    /// Возвращает итоговый статус и число попыток.
    /// </summary>
    public (string Status, int Attempts) SaveAnalysis(VacancyRecord record)
    {
        var failReason = record.FailReason is null
            ? null
            : record.FailReason.Length <= FailReasonMaxLength
                ? record.FailReason
                : record.FailReason[..FailReasonMaxLength];

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            INSERT INTO vacancies (link, employer_id, title, company, salary, score, status, first_seen, analyzed_at, fail_reason, comment, cover_letter, tech_stack, experience, employment, work_format, published_at, description, attempts)
            VALUES (@Link, @EmployerId, @Title, @Company, @Salary, @Score, @Status, @FirstSeen, @AnalyzedAt, @FailReason, @Comment, @CoverLetter, @TechStack, @Experience, @Employment, @WorkFormat, @PublishedAt, @Description, 1)
            ON CONFLICT(link) DO UPDATE SET
                employer_id = @EmployerId,
                title = @Title,
                company = @Company,
                salary = @Salary,
                score = @Score,
                status = @Status,
                analyzed_at = @AnalyzedAt,
                fail_reason = @FailReason,
                comment = @Comment,
                cover_letter = @CoverLetter,
                tech_stack = @TechStack,
                experience = @Experience,
                employment = @Employment,
                work_format = @WorkFormat,
                published_at = @PublishedAt,
                description = @Description,
                attempts = attempts + 1
            """,
            new
            {
                record.Link,
                record.EmployerId,
                record.Title,
                record.Company,
                record.Salary,
                record.Score,
                record.Status,
                record.FirstSeen,
                record.AnalyzedAt,
                FailReason = failReason,
                record.Comment,
                record.CoverLetter,
                record.TechStack,
                record.Experience,
                record.Employment,
                record.WorkFormat,
                record.PublishedAt,
                record.Description,
            },
            transaction);

        connection.Execute(
            $"""
            UPDATE vacancies
            SET status = @{nameof(VacancyStatus.AnalysisAbandoned)}
            WHERE link = @link
              AND status = @{nameof(VacancyStatus.AnalysisFailed)}
              AND attempts >= @maxAttempts
            """,
            new
            {
                link = record.Link,
                AnalysisAbandoned = VacancyStatus.AnalysisAbandoned,
                AnalysisFailed = VacancyStatus.AnalysisFailed,
                maxAttempts = MaxAnalysisAttempts,
            },
            transaction);

        var row = connection.QuerySingle<(string Status, int Attempts)>(
            "SELECT status, attempts FROM vacancies WHERE link = @link",
            new { link = record.Link },
            transaction);

        transaction.Commit();
        return row;
    }

    public void SetStatus(string link, string status, int? score = null, string? failReason = null)
    {
        // analyzed_at обновляется только для статусов, связанных с анализом и отправкой.
        var now = status is VacancyStatus.AnalyzedLow
            or VacancyStatus.AnalyzedOk
            or VacancyStatus.Sent
            or VacancyStatus.SendFailed
            or VacancyStatus.AnalysisFailed
            ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            : null;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(
            $"""
            UPDATE vacancies
            SET status = @status,
                score = COALESCE(@score, score),
                fail_reason = @failReason,
                analyzed_at = COALESCE(@now, analyzed_at)
            WHERE link = @link
            """,
            new { link, status, score, failReason, now });
    }

    public IReadOnlyDictionary<int, int> GetScoreDistribution()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var rows = connection.Query<(int Score, int Count)>(
            "SELECT score AS Item1, COUNT(*) AS Item2 FROM vacancies WHERE score BETWEEN 1 AND 10 GROUP BY score").AsList();

        return rows.ToDictionary(row => row.Score, row => row.Count);
    }

    /// <summary>
    /// Ссылки, которые не анализируют и не отправляют повторно. Строится по
    /// исключению: пропускается всё, кроме new и analysis_failed (он подлежит
    /// переанализу); analysis_abandoned, seen и остальные терминальные статусы
    /// входят.
    /// </summary>
    public IReadOnlyCollection<string> GetLinksToSkip()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection
            .Query<string>(
                "SELECT link FROM vacancies WHERE status NOT IN (@New, @AnalysisFailed)",
                new
                {
                    New = VacancyStatus.New,
                    AnalysisFailed = VacancyStatus.AnalysisFailed,
                })
            .AsList();
    }

    /// <summary>
    /// Записи, готовые к повторной отправке без GigaChat: send_failed/analyzed_ok
    /// с сохранёнными score и comment. analysis_failed не входит.
    /// </summary>
    public IReadOnlyList<VacancyRecord> GetPendingSendRetries()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var rows = connection.Query(
            """
            SELECT link,title,company,salary,score,comment,cover_letter,tech_stack,status
            FROM vacancies
            WHERE status IN (@SendFailed, @AnalyzedOk)
              AND score IS NOT NULL
              AND comment IS NOT NULL AND TRIM(comment) <> ''
            ORDER BY analyzed_at
            """,
            new
            {
                SendFailed = VacancyStatus.SendFailed,
                AnalyzedOk = VacancyStatus.AnalyzedOk,
            }).AsList();

        return rows.Select(row => new VacancyRecord(
            Link: (string)row.link,
            EmployerId: null,
            Title: (string?)row.title,
            Company: (string?)row.company,
            Salary: (string?)row.salary,
            Score: (int?)row.score,
            Status: (string)row.status,
            FirstSeen: string.Empty,
            AnalyzedAt: null,
            FailReason: null,
            Comment: (string?)row.comment,
            CoverLetter: (string?)row.cover_letter,
            TechStack: (string?)row.tech_stack)).AsList();
    }
}
