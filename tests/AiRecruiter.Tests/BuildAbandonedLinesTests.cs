using AiRecruiter;

namespace AiRecruiter.Tests;

/// <summary>
/// Строки [Abandoned] в [ИТОГ]: чистая функция входных данных.
/// </summary>
public sealed class BuildAbandonedLinesTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(Program.BuildAbandonedLines([]));
    }

    [Fact]
    public void Lines_LinkAndFailReason()
    {
        var lines = Program.BuildAbandonedLines(
        [
            ("https://hh.ru/vacancy/1", "незакрытый JSON"),
            ("https://hh.ru/vacancy/2", null),
        ]);

        Assert.Equal(2, lines.Count);
        Assert.Equal("[Abandoned] https://hh.ru/vacancy/1 | незакрытый JSON", lines[0]);
        Assert.Equal("[Abandoned] https://hh.ru/vacancy/2 | причина не указана", lines[1]);
    }
}
