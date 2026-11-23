using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AiRecruiter.Storage;

/// <summary>
/// Запись о вакансии в SQLite. Status — одна из строк статуса жизненного цикла:
/// new, seen, filtered_stopword, filtered_salary, analyzed_low, analyzed_ok,
/// sent, send_failed, analysis_failed. Comment/CoverLetter/TechStack —
/// сохранённый результат анализа, необходимый для повторной отправки без
/// GigaChat.
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
    string? PublishedAt = null);

/// <summary>
/// Хранилище состояния вакансий в SQLite (vacancies.db). Соединение открывается
/// на каждую операцию; повторная обработка существующей ссылки запрещена —
/// GetAllLinks возвращает ВСЕ строки, включая filtered_*.
/// </summary>
internal sealed class VacancyRepository
{
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
                published_at TEXT
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
                _ => throw new InvalidOperationException($"Нет ветки миграции для колонки {column}"),
            };

            connection.Execute(alterSql, transaction: transaction);
        }

        // Legacy-записи, готовые к повторной отправке, но без сохранённого анализа:
        // повторная отправка невозможна, переводим в analysis_failed для переанализа.
        connection.Execute(
            """
            UPDATE vacancies
            SET status = 'analysis_failed',
                fail_reason = 'migrated: no saved analysis'
            WHERE status IN ('send_failed','analyzed_ok')
              AND (score IS NULL OR comment IS NULL OR TRIM(comment) = '')
            """, transaction: transaction);

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
            INSERT INTO vacancies (link, employer_id, title, company, salary, score, status, first_seen, analyzed_at, fail_reason, comment, cover_letter, tech_stack, experience, employment, work_format, published_at)
            VALUES (@Link, @EmployerId, @Title, @Company, @Salary, @Score, @Status, @FirstSeen, @AnalyzedAt, @FailReason, @Comment, @CoverLetter, @TechStack, @Experience, @Employment, @WorkFormat, @PublishedAt)
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
                published_at = @PublishedAt
            """, record);
    }

    public void SetStatus(string link, string status, int? score = null, string? failReason = null)
    {
        // analyzed_at обновляется только для статусов, связанных с анализом и отправкой.
        var now = status is "analyzed_low" or "analyzed_ok" or "sent" or "send_failed" or "analysis_failed"
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
    /// Ссылки, которые не анализируют и не отправляют повторно:
    /// все статусы, кроме analysis_failed (он подлежит переанализу).
    /// </summary>
    public IReadOnlyCollection<string> GetLinksToSkip()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection
            .Query<string>(
                """
                SELECT link FROM vacancies WHERE status IN
                ('seen','filtered_stopword','filtered_salary','analyzed_low',
                 'analyzed_ok','sent','send_failed')
                """)
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
            WHERE status IN ('send_failed','analyzed_ok')
              AND score IS NOT NULL
              AND comment IS NOT NULL AND TRIM(comment) <> ''
            ORDER BY analyzed_at
            """).AsList();

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
