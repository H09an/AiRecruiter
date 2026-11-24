using System.Net;
using AiRecruiter.Analyzers;

namespace AiRecruiter.Tests;

/// <summary>
/// IsRunStoppingException: сетевые ошибки и таймаут HTTP останавливают прогон,
/// остальные исключения дают analysis_failed. Сеть не используется.
/// </summary>
public sealed class IsRunStoppingExceptionTests
{
    [Fact]
    public void HttpRequestException_IsRunStopping() =>
        Assert.True(JobAnalyzer.IsRunStoppingException(
            new HttpRequestException("HTTP 401 Unauthorized", null, HttpStatusCode.Unauthorized)));

    [Fact]
    public void TaskCanceledException_IsRunStopping() =>
        Assert.True(JobAnalyzer.IsRunStoppingException(new TaskCanceledException("timeout")));

    [Fact]
    public void OperationCanceledExceptionWithoutTimeout_IsNotRunStopping() =>
        Assert.False(JobAnalyzer.IsRunStoppingException(new OperationCanceledException("cancel")));

    [Theory]
    [InlineData("invalid json")]
    [InlineData("data error")]
    public void OtherExceptions_AreNotRunStopping(string message) =>
        Assert.False(JobAnalyzer.IsRunStoppingException(new InvalidOperationException(message)));
}
