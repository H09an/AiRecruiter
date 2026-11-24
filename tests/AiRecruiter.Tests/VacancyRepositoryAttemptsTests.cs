using AiRecruiter.Storage;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AiRecruiter.Tests;

/// <summary>
/// Счётчик попыток attempts, переход analysis_abandoned, sweep на старте,
/// GetLinksToSkip по исключению, политика description, обрезка fail_reason,
/// миграция колонки attempts на legacy-схеме и SQL-инварианты.
/// </summary>
public sealed class VacancyRepositoryAttemptsTests : IDisposable
{
    private static readonly string[] DatabaseFileSuffixes = { string.Empty, "-wal", "-shm", "-journal" };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
    private readonly VacancyRepository _repository;

    public VacancyRepositoryAttemptsTests()
    {
        _repository = new VacancyRepository(_dbPath);
        _repository.Initialize();
    }

    public void Dispose()
    {
        foreach (var suffix in DatabaseFileSuffixes)
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static VacancyRecord FailedRecord(
        string link = "https://hh.ru/vacancy/1",
        string? description = null,
        string? failReason = "причина сбоя") => new(
            Link: link,
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: null,
            Status: VacancyStatus.AnalysisFailed,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: "2026-01-01T01:00:00.0000000Z",
            FailReason: failReason,
            Comment: null,
            CoverLetter: null,
            TechStack: "Docker",
            Description: description);

    private (string Status, int Attempts) FailOnce(
        string link = "https://hh.ru/vacancy/1",
        string? description = null,
        string? failReason = "причина сбоя") =>
        _repository.SaveAnalysis(FailedRecord(link: link, description: description, failReason: failReason));

    private string? QueryScalar(string sql, object? param = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection.ExecuteScalar<string>(sql, param);
    }

    private long QueryLong(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection.ExecuteScalar<long>(sql);
    }

    [Fact]
    public void SaveAnalysis_NewRow_AttemptsIsOne()
    {
        var (status, attempts) = FailOnce();

        Assert.Equal(VacancyStatus.AnalysisFailed, status);
        Assert.Equal(1, attempts);
        Assert.Equal("1", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void SaveAnalysis_IncrementsAttemptsOnEachCall()
    {
        FailOnce();
        FailOnce();

        var (status, attempts) = FailOnce();

        Assert.Equal(VacancyStatus.AnalysisAbandoned, status);
        Assert.Equal(3, attempts);
        Assert.Equal("3", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void SaveAnalysis_ThirdFailure_BecomesAbandonedInSameTransaction()
    {
        FailOnce();
        FailOnce();

        FailOnce();

        Assert.Equal(
            VacancyStatus.AnalysisAbandoned,
            QueryScalar("SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Equal("причина сбоя", QueryScalar("SELECT fail_reason FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void SaveAnalysis_TwoFailuresThenSuccess_NotAbandoned()
    {
        FailOnce();
        FailOnce();

        var success = new VacancyRecord(
            Link: "https://hh.ru/vacancy/1",
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: 8,
            Status: VacancyStatus.AnalyzedOk,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: "2026-01-01T02:00:00.0000000Z",
            FailReason: null,
            Comment: "Комментарий анализа");

        var (status, attempts) = _repository.SaveAnalysis(success);

        Assert.Equal(VacancyStatus.AnalyzedOk, status);
        Assert.Equal(3, attempts);
        Assert.Equal(
            VacancyStatus.AnalyzedOk,
            QueryScalar("SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void Upsert_DoesNotChangeAttempts()
    {
        FailOnce();
        FailOnce();

        _repository.Upsert(FailedRecord());

        Assert.Equal("2", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void SetStatus_DoesNotChangeAttempts()
    {
        FailOnce();
        FailOnce();

        _repository.SetStatus("https://hh.ru/vacancy/1", VacancyStatus.Sent, 8);

        Assert.Equal("2", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void SaveFiltered_KeepsAttemptsAndClearsAnalysisFields()
    {
        FailOnce();
        FailOnce();

        var filtered = new VacancyRecord(
            Link: "https://hh.ru/vacancy/1",
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: null,
            Status: VacancyStatus.FilteredStopword,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: null,
            FailReason: null);
        _repository.SaveFiltered(filtered);

        Assert.Equal("2", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Null(QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Null(QueryScalar("SELECT fail_reason FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Null(QueryScalar("SELECT analyzed_at FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void SaveAnalysis_FailReasonTruncatedTo500()
    {
        var longReason = new string('x', 600);

        FailOnce(failReason: longReason);

        var stored = QueryScalar("SELECT fail_reason FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'");
        Assert.NotNull(stored);
        Assert.Equal(500, stored!.Length);
        Assert.Equal(VacancyRepository.FailReasonMaxLength, stored.Length);
    }

    [Fact]
    public void SaveAnalysis_DescriptionKeptForFailed_ClearedOnSuccess()
    {
        FailOnce(description: "Полное описание вакансии");

        Assert.Equal(
            "Полное описание вакансии",
            QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));

        var success = new VacancyRecord(
            Link: "https://hh.ru/vacancy/1",
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: 8,
            Status: VacancyStatus.AnalyzedOk,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: "2026-01-01T02:00:00.0000000Z",
            FailReason: null,
            Comment: "Комментарий анализа",
            Description: null);
        _repository.SaveAnalysis(success);

        Assert.Null(QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void Initialize_Sweep_ConvertsFailedWithThreeAttempts_KeepsReasonAndDescription()
    {
        FailOnce(description: "Полное описание вакансии");
        FailOnce(description: "Полное описание вакансии");

        // Имитируем три предыдущие неудачи: attempts=3, анализ не удался.
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            connection.Execute(
                "UPDATE vacancies SET attempts = 3 WHERE link = 'https://hh.ru/vacancy/1'");
        }

        _repository.Initialize();

        Assert.Equal(
            VacancyStatus.AnalysisAbandoned,
            QueryScalar("SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Equal("3", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Equal(
            "Полное описание вакансии",
            QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Equal(
            "причина сбоя",
            QueryScalar("SELECT fail_reason FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void Initialize_Sweep_LeavesFailedWithFewAttempts_AbandonedUntouched()
    {
        FailOnce();

        _repository.Initialize();
        _repository.Initialize();

        Assert.Equal(
            VacancyStatus.AnalysisFailed,
            QueryScalar("SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Equal("1", QueryScalar("SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void GetLinksToSkip_ExcludesNewAndFailed_IncludesAbandonedAndTerminal()
    {
        _repository.SaveFiltered(BuildFilteredRecord("https://hh.ru/vacancy/new"));
        _repository.SaveAnalysis(FailedRecord(link: "https://hh.ru/vacancy/failed"));
        _repository.SaveAnalysis(FailedRecord(link: "https://hh.ru/vacancy/abandoned"));
        _repository.SaveAnalysis(FailedRecord(link: "https://hh.ru/vacancy/abandoned"));
        _repository.SaveAnalysis(FailedRecord(link: "https://hh.ru/vacancy/abandoned"));

        var seen = new VacancyRecord(
            Link: "https://hh.ru/vacancy/seen",
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: null,
            Status: VacancyStatus.Seen,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: null,
            FailReason: null);
        _repository.SaveFiltered(seen);

        var sent = new VacancyRecord(
            Link: "https://hh.ru/vacancy/sent",
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: 9,
            Status: VacancyStatus.Sent,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: "2026-01-01T02:00:00.0000000Z",
            FailReason: null,
            Comment: "Комментарий анализа");
        _repository.SaveAnalysis(sent);
        _repository.SetStatus("https://hh.ru/vacancy/sent", VacancyStatus.Sent, 9);

        var skipLinks = _repository.GetLinksToSkip();

        Assert.DoesNotContain("https://hh.ru/vacancy/new", skipLinks);
        Assert.DoesNotContain("https://hh.ru/vacancy/failed", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/abandoned", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/seen", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/sent", skipLinks);
    }

    [Fact]
    public void Invariants_NoAbandonedBelowThreeAttempts_NoFailedWithThree()
    {
        FailOnce();
        FailOnce();
        FailOnce();
        FailOnce();

        // Четвёртая попытка поверх abandoned: строка не возвращается в failed.
        Assert.Equal(VacancyStatus.AnalysisAbandoned, QueryScalar(
            "SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));

        Assert.Equal(0, QueryLong(
            $"SELECT COUNT(*) FROM vacancies WHERE status = '{VacancyStatus.AnalysisAbandoned}' AND attempts < 3"));
        Assert.Equal(0, QueryLong(
            $"SELECT COUNT(*) FROM vacancies WHERE status = '{VacancyStatus.AnalysisFailed}' AND attempts >= 3"));
    }

    [Fact]
    public void Initialize_LegacySchemaWithoutAttempts_AddsColumnWithZero()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
        try
        {
            // Схема шага 5: все колонки, кроме attempts.
            using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                connection.Open();
                connection.Execute("""
                    CREATE TABLE vacancies (
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
                        description TEXT
                    );
                    INSERT INTO vacancies (link, status, first_seen) VALUES
                        ('https://hh.ru/vacancy/1', 'analysis_failed', '2026-01-01T00:00:00.0000000Z'),
                        ('https://hh.ru/vacancy/2', 'new', '2026-01-01T00:00:00.0000000Z');
                    """);
            }

            var repository = new VacancyRepository(legacyPath);
            repository.Initialize();
            repository.Initialize();

            using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                connection.Open();
                Assert.Equal(
                    "attempts",
                    connection.ExecuteScalar<string>(
                        "SELECT name FROM pragma_table_info('vacancies') WHERE name = 'attempts'"));
                Assert.Equal(2, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM vacancies"));
                // Старые analysis_failed начинают счёт с нуля.
                Assert.Equal(0, connection.ExecuteScalar<long>(
                    "SELECT attempts FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
                Assert.Equal(
                    VacancyStatus.AnalysisFailed,
                    connection.ExecuteScalar<string>(
                        "SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
                Assert.Equal(0, connection.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM vacancies WHERE status = 'analysis_abandoned'"));
            }
        }
        finally
        {
            foreach (var suffix in DatabaseFileSuffixes)
            {
                var path = legacyPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static VacancyRecord BuildFilteredRecord(string link) => new(
        Link: link,
        EmployerId: null,
        Title: "QA engineer",
        Company: "Company",
        Salary: "100 000 ₽",
        Score: null,
        Status: VacancyStatus.New,
        FirstSeen: "2026-01-01T00:00:00.0000000Z",
        AnalyzedAt: null,
        FailReason: null);
}
