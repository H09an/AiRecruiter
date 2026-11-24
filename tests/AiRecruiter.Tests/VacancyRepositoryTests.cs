using AiRecruiter.Storage;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AiRecruiter.Tests;

/// <summary>
/// Хранилище вакансий в SQLite: схема, Upsert, статусы, гистограмма оценок,
/// сохранение first_seen при повторном Upsert.
/// </summary>
public sealed class VacancyRepositoryTests : IDisposable
{
    private static readonly string[] DatabaseFileSuffixes = { string.Empty, "-wal", "-shm", "-journal" };
    private static readonly string[] ExpectedTwoLinks =
    {
        "https://hh.ru/vacancy/1",
        "https://hh.ru/vacancy/2",
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
    private readonly VacancyRepository _repository;

    public VacancyRepositoryTests()
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

    private static VacancyRecord Record(
        string link = "https://hh.ru/vacancy/1",
        string? title = "QA engineer",
        int? score = null,
        string status = "seen",
        string firstSeen = "2026-01-01T00:00:00.0000000Z") => new(
            Link: link,
            EmployerId: null,
            Title: title,
            Company: "Company",
            Salary: "100 000 ₽",
            Score: score,
            Status: status,
            FirstSeen: firstSeen,
            AnalyzedAt: null,
            FailReason: null);

    private static string? QueryScalarFor(string databasePath, string sql, object? param = null)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection.ExecuteScalar<string>(sql, param);
    }

    private string? QueryScalar(string sql, object? param = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection.ExecuteScalar<string>(sql, param);
    }

    private static VacancyRecord AnalyzedRecord(
        string link = "https://hh.ru/vacancy/1",
        int? score = 8,
        string status = "analyzed_ok",
        string? comment = "Комментарий анализа",
        string? coverLetter = "Сопроводительное письмо",
        string? techStack = "Python, pytest",
        string firstSeen = "2026-01-01T00:00:00.0000000Z",
        string? analyzedAt = "2026-01-01T01:00:00.0000000Z") => new(
            Link: link,
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: score,
            Status: status,
            FirstSeen: firstSeen,
            AnalyzedAt: analyzedAt,
            FailReason: null,
            Comment: comment,
            CoverLetter: coverLetter,
            TechStack: techStack);

    private static readonly string[] LegacyLinks =
    {
        "https://hh.ru/vacancy/legacy_send_failed",
        "https://hh.ru/vacancy/legacy_analyzed_ok",
    };

    [Fact]
    public void Initialize_NewDatabase_HasAnalysisColumns()
    {
        Assert.Equal("comment", QueryScalar("SELECT name FROM pragma_table_info('vacancies') WHERE name = 'comment'"));
        Assert.Equal("cover_letter", QueryScalar("SELECT name FROM pragma_table_info('vacancies') WHERE name = 'cover_letter'"));
        Assert.Equal("tech_stack", QueryScalar("SELECT name FROM pragma_table_info('vacancies') WHERE name = 'tech_stack'"));
    }

    [Fact]
    public void Initialize_LegacyDatabase_AddsColumnsAndMigratesUnanalyzed()
    {
        // Отдельный файл: конструктор класса не должен инициализировать legacy-схему.
        var legacyPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
        try
        {
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
                        fail_reason TEXT
                    );
                    """);
                connection.Execute(
                    "INSERT INTO vacancies (link, status, first_seen, score) VALUES (@link, @status, @firstSeen, @score)",
                    new[]
                    {
                        new { link = LegacyLinks[0], status = "send_failed", firstSeen = "2026-01-01T00:00:00.0000000Z", score = 8 },
                        new { link = LegacyLinks[1], status = "analyzed_ok", firstSeen = "2026-01-01T00:00:00.0000000Z", score = 8 },
                    });
            }

            var repository = new VacancyRepository(legacyPath);
            repository.Initialize();

            foreach (var column in new[] { "comment", "cover_letter", "tech_stack" })
            {
                using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
                {
                    connection.Open();
                    var name = connection.ExecuteScalar<string>(
                        "SELECT name FROM pragma_table_info('vacancies') WHERE name = @column",
                        new { column });
                    Assert.Equal(column, name);
                }
            }

            foreach (var link in LegacyLinks)
            {
                using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
                {
                    connection.Open();
                    Assert.Equal(
                        "analysis_failed",
                        connection.ExecuteScalar<string>("SELECT status FROM vacancies WHERE link = @link", new { link }));
                    Assert.Equal(
                        "migrated: no saved analysis",
                        connection.ExecuteScalar<string>("SELECT fail_reason FROM vacancies WHERE link = @link", new { link }));
                }
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

    [Fact]
    public void Upsert_AnalysisFields_RoundTrip()
    {
        _repository.Upsert(AnalyzedRecord());

        Assert.Equal("Комментарий анализа", QueryScalar("SELECT comment FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("Сопроводительное письмо", QueryScalar("SELECT cover_letter FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("Python, pytest", QueryScalar("SELECT tech_stack FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
    }

    [Fact]
    public void GetAllLinks_ReturnsAllStatuses()
    {
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/1", status: "seen"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/2", status: "filtered_salary"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/3", status: "sent", score: 9));

        Assert.Equal(
            ExpectedTwoLinks.Append("https://hh.ru/vacancy/3").ToArray(),
            _repository.GetAllLinks());
    }

    [Fact]
    public void GetLinksToSkip_ExcludesAnalysisFailed()
    {
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/seen", status: "seen"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/stopword", status: "filtered_stopword"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/salary", status: "filtered_salary"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/low", status: "analyzed_low", score: 5));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/ok", status: "analyzed_ok", score: 8));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/sent", status: "sent", score: 9));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/failed_send", status: "send_failed", score: 8));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/failed_analysis", status: "analysis_failed"));

        var skipLinks = _repository.GetLinksToSkip();

        Assert.Equal(7, skipLinks.Count);
        Assert.Contains("https://hh.ru/vacancy/seen", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/stopword", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/salary", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/low", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/ok", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/sent", skipLinks);
        Assert.Contains("https://hh.ru/vacancy/failed_send", skipLinks);
        Assert.DoesNotContain("https://hh.ru/vacancy/failed_analysis", skipLinks);
    }

    [Fact]
    public void GetPendingSendRetries_ReturnsSendableRecordsWithTechStack()
    {
        _repository.Upsert(AnalyzedRecord(link: "https://hh.ru/vacancy/1", status: "send_failed", score: 8));
        _repository.Upsert(AnalyzedRecord(link: "https://hh.ru/vacancy/2", status: "analyzed_ok", score: 9));
        _repository.Upsert(AnalyzedRecord(link: "https://hh.ru/vacancy/3", status: "analysis_failed", score: null, comment: null));
        _repository.Upsert(AnalyzedRecord(link: "https://hh.ru/vacancy/4", status: "analyzed_ok", score: 7, comment: "   "));

        var retries = _repository.GetPendingSendRetries();

        Assert.Equal(
            ExpectedTwoLinks,
            retries.Select(r => r.Link).ToArray());
        Assert.All(retries, r => Assert.Equal("Python, pytest", r.TechStack));
        Assert.All(retries, r => Assert.Equal("Комментарий анализа", r.Comment));
    }

    [Fact]
    public void SetStatus_DoesNotTouchAnalysisFields()
    {
        _repository.Upsert(AnalyzedRecord());

        _repository.SetStatus("https://hh.ru/vacancy/1", "sent", 8);

        Assert.Equal("sent", QueryScalar("SELECT status FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("Комментарий анализа", QueryScalar("SELECT comment FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("Сопроводительное письмо", QueryScalar("SELECT cover_letter FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("Python, pytest", QueryScalar("SELECT tech_stack FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
    }

    [Fact]
    public void GetAllLinks_AfterInitialize_ReturnsEmpty()
    {
        Assert.Empty(_repository.GetAllLinks());
    }

    [Fact]
    public void Upsert_GetAllLinks_ReturnsAllLinksIncludingFiltered()
    {
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/1", status: "seen"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/2", status: "filtered_salary"));

        Assert.Equal(ExpectedTwoLinks, _repository.GetAllLinks());
    }

    [Fact]
    public void Upsert_SecondInsert_UpdatesFieldsButKeepsFirstSeen()
    {
        _repository.Upsert(Record(firstSeen: "2026-01-01T00:00:00.0000000Z", score: null, status: "seen"));

        _repository.Upsert(Record(
            firstSeen: "2026-12-31T23:59:59.0000000Z",
            score: 8,
            status: "analyzed_ok"));

        Assert.Equal(
            "2026-01-01T00:00:00.0000000Z",
            QueryScalar("SELECT first_seen FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal(
            "8",
            QueryScalar("SELECT score FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal(
            "analyzed_ok",
            QueryScalar("SELECT status FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
    }

    [Fact]
    public void SetStatus_AnalyzedStatus_UpdatesAnalyzedAt()
    {
        _repository.Upsert(Record(status: "seen"));

        _repository.SetStatus("https://hh.ru/vacancy/1", "analyzed_low", 4);

        Assert.Equal("analyzed_low", QueryScalar("SELECT status FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("4", QueryScalar("SELECT score FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.NotNull(QueryScalar("SELECT analyzed_at FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
    }

    [Fact]
    public void SetStatus_OtherStatus_KeepsAnalyzedAt()
    {
        _repository.Upsert(Record(status: "seen"));

        _repository.SetStatus("https://hh.ru/vacancy/1", "filtered_stopword");

        Assert.Equal("filtered_stopword", QueryScalar("SELECT status FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Null(QueryScalar("SELECT analyzed_at FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
    }

    [Fact]
    public void SetStatus_ScoreNull_KeepsExistingScore()
    {
        _repository.Upsert(Record(score: 8, status: "analyzed_ok"));

        _repository.SetStatus("https://hh.ru/vacancy/1", "sent");

        Assert.Equal(
            "8",
            QueryScalar("SELECT score FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
        Assert.Equal("sent", QueryScalar("SELECT status FROM vacancies WHERE link = @link", new { link = "https://hh.ru/vacancy/1" }));
    }

    [Fact]
    public void GetScoreDistribution_CountsScoresOneToTen()
    {
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/1", score: 1));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/2", score: 3));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/3", score: 3));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/4", score: 10));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/5", score: null, status: "filtered_salary"));

        var distribution = _repository.GetScoreDistribution();

        Assert.Equal(3, distribution.Count);
        Assert.Equal(1, distribution[1]);
        Assert.Equal(2, distribution[3]);
        Assert.Equal(1, distribution[10]);
    }

    [Fact]
    public void Initialize_DropsMetaAndKeepsVacancyRows()
    {
        // Отдельный файл: старая схема с meta и строкой в vacancies.
        var metaPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={metaPath}"))
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
                        fail_reason TEXT
                    );
                    CREATE TABLE meta (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );
                    INSERT INTO meta (key, value) VALUES ('schema', 'v1');
                    INSERT INTO vacancies (link, status, first_seen)
                    VALUES ('https://hh.ru/vacancy/1', 'seen', '2026-01-01T00:00:00.0000000Z');
                    """);
            }

            var repository = new VacancyRepository(metaPath);
            repository.Initialize();

            Assert.Null(QueryScalarFor(
                metaPath,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'meta'"));
            Assert.Equal(
                "https://hh.ru/vacancy/1",
                QueryScalarFor(metaPath, "SELECT link FROM vacancies"));
            foreach (var column in new[] { "comment", "cover_letter", "tech_stack" })
            {
                Assert.Equal(
                    column,
                    QueryScalarFor(
                        metaPath,
                        "SELECT name FROM pragma_table_info('vacancies') WHERE name = @name",
                        new { name = column }));
            }
        }
        finally
        {
            foreach (var suffix in DatabaseFileSuffixes)
            {
                var path = metaPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void Initialize_SecondRun_IsIdempotentAndKeepsRows()
    {
        _repository.Upsert(AnalyzedRecord());

        _repository.Initialize();
        _repository.Initialize();

        Assert.Equal(
            "https://hh.ru/vacancy/1",
            QueryScalar("SELECT link FROM vacancies"));
        Assert.Equal(
            "Сопроводительное письмо",
            QueryScalar("SELECT cover_letter FROM vacancies"));
        Assert.Null(QueryScalar(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'meta'"));
    }

    [Fact]
    public void Initialize_LegacySchemaWithoutNewColumns_AddsFourColumnsWithoutRowLoss()
    {
        // Схема до шага 4: без experience/employment/work_format/published_at.
        var legacyPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
        try
        {
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
                        tech_stack TEXT
                    );
                    INSERT INTO vacancies (link, status, first_seen) VALUES
                        ('https://hh.ru/vacancy/1', 'seen', '2026-01-01T00:00:00.0000000Z'),
                        ('https://hh.ru/vacancy/2', 'analyzed_low', '2026-01-01T00:00:00.0000000Z');
                    """);
            }

            var repository = new VacancyRepository(legacyPath);
            repository.Initialize();
            repository.Initialize();

            using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                connection.Open();
                foreach (var column in new[] { "experience", "employment", "work_format", "published_at" })
                {
                    Assert.Equal(
                        column,
                        connection.ExecuteScalar<string>(
                            "SELECT name FROM pragma_table_info('vacancies') WHERE name = @column",
                            new { column }));
                }

                Assert.Equal(
                    2,
                    connection.ExecuteScalar<long>("SELECT COUNT(*) FROM vacancies"));
                Assert.Equal(
                    "seen",
                    connection.ExecuteScalar<string>(
                        "SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
                Assert.Equal(
                    "analyzed_low",
                    connection.ExecuteScalar<string>(
                        "SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/2'"));
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

    [Fact]
    public void Upsert_NewStatus_InGetAllLinks_ButNotInSkipList()
    {
        _repository.Upsert(Record(status: "new"));

        Assert.Contains("https://hh.ru/vacancy/1", _repository.GetAllLinks());
        Assert.DoesNotContain("https://hh.ru/vacancy/1", _repository.GetLinksToSkip());
    }

    [Fact]
    public void Upsert_FilteredStatuses_KeptAndIncludedInSkipList()
    {
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/1", status: "filtered_stopword"));
        _repository.Upsert(Record(link: "https://hh.ru/vacancy/2", status: "filtered_salary"));

        Assert.Equal("filtered_stopword", QueryScalar("SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
        Assert.Equal("filtered_salary", QueryScalar("SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/2'"));
        Assert.Contains("https://hh.ru/vacancy/1", _repository.GetLinksToSkip());
        Assert.Contains("https://hh.ru/vacancy/2", _repository.GetLinksToSkip());
    }

    private static VacancyRecord RecordWithDescription(
        string status,
        string? description,
        string link = "https://hh.ru/vacancy/1",
        string? failReason = null) => new(
            Link: link,
            EmployerId: null,
            Title: "QA engineer",
            Company: "Company",
            Salary: "100 000 ₽",
            Score: null,
            Status: status,
            FirstSeen: "2026-01-01T00:00:00.0000000Z",
            AnalyzedAt: "2026-01-01T01:00:00.0000000Z",
            FailReason: failReason,
            Comment: null,
            CoverLetter: null,
            TechStack: "Docker",
            Description: description);

    [Fact]
    public void Upsert_Description_WrittenForAnalysisFailed_ClearedOnSuccess()
    {
        _repository.Upsert(RecordWithDescription("analysis_failed", "Полное описание вакансии", failReason: "нет score"));

        Assert.Equal(
            "Полное описание вакансии",
            QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));

        _repository.Upsert(RecordWithDescription("analyzed_low", null, failReason: null));

        Assert.Null(QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));

        _repository.Upsert(RecordWithDescription("analyzed_ok", null, failReason: null));

        Assert.Null(QueryScalar("SELECT description FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
    }

    [Fact]
    public void Initialize_LegacySchemaWithoutDescription_AddsColumnWithoutRowLoss()
    {
        // Схема шага 4: без description.
        var legacyPath = Path.Combine(Path.GetTempPath(), $"vacancies_test_{Guid.NewGuid():N}.db");
        try
        {
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
                        published_at TEXT
                    );
                    INSERT INTO vacancies (link, status, first_seen) VALUES
                        ('https://hh.ru/vacancy/1', 'new', '2026-01-01T00:00:00.0000000Z'),
                        ('https://hh.ru/vacancy/2', 'analyzed_ok', '2026-01-01T00:00:00.0000000Z');
                    """);
            }

            var repository = new VacancyRepository(legacyPath);
            repository.Initialize();
            repository.Initialize();

            using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                connection.Open();
                Assert.Equal(
                    "description",
                    connection.ExecuteScalar<string>(
                        "SELECT name FROM pragma_table_info('vacancies') WHERE name = 'description'"));
                Assert.Equal(
                    2,
                    connection.ExecuteScalar<long>("SELECT COUNT(*) FROM vacancies"));
                Assert.Equal(
                    "new",
                    connection.ExecuteScalar<string>(
                        "SELECT status FROM vacancies WHERE link = 'https://hh.ru/vacancy/1'"));
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
}
