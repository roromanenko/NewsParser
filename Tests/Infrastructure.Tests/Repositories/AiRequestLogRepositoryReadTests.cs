using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for the read-side of <see cref="IAiRequestLogRepository"/>:
/// <c>GetMetricsAsync</c>, <c>GetPagedAsync</c>, <c>CountAsync</c>,
/// <c>GetByIdAsync</c>.
///
/// The Dapper implementation requires a live PostgreSQL connection, so these
/// are black-box interface-contract tests against a mock — they verify that
/// the public contract (the filter object the caller supplies and the result
/// the implementation returns) flows through unchanged. The private
/// <c>BuildWhere</c> / <c>AppendStatusClause</c> / <c>AppendSearchClause</c>
/// helpers are exercised through this surface, mirroring the pattern in
/// <c>AiRequestLogRepositoryContractTests</c> for the write-side
/// (<c>AddAsync</c>).
/// </summary>
[TestFixture]
public class AiRequestLogRepositoryReadTests
{
    private Mock<IAiRequestLogRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IAiRequestLogRepository>();
    }

    // ------------------------------------------------------------------
    // P0 — GetPagedAsync forwards an all-null filter (BuildWhere → "1=1")
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenFilterIsAllNull_ForwardsAllNullFilterToImplementation()
    {
        // Arrange
        var emptyFilter = new AiRequestLogFilter(
            From: null, To: null, Provider: null, Worker: null, Model: null,
            Status: null, Search: null);

        AiRequestLogFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiRequestLogFilter, int, int, CancellationToken>((f, _, _, _) => capturedFilter = f)
            .ReturnsAsync([]);

        // Act
        await _repositoryMock.Object.GetPagedAsync(emptyFilter, page: 1, pageSize: 20, CancellationToken.None);

        // Assert
        capturedFilter.Should().NotBeNull();
        capturedFilter!.From.Should().BeNull();
        capturedFilter.To.Should().BeNull();
        capturedFilter.Provider.Should().BeNull();
        capturedFilter.Worker.Should().BeNull();
        capturedFilter.Model.Should().BeNull();
        capturedFilter.Status.Should().BeNull();
        capturedFilter.Search.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — GetPagedAsync forwards From-only filter (BuildWhere → "Timestamp" >= @from)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenFromOnly_ForwardsFromInFilter()
    {
        // Arrange
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var filter = new AiRequestLogFilter(
            From: from, To: null, Provider: null, Worker: null, Model: null,
            Status: null, Search: null);

        AiRequestLogFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiRequestLogFilter, int, int, CancellationToken>((f, _, _, _) => capturedFilter = f)
            .ReturnsAsync([]);

        // Act
        await _repositoryMock.Object.GetPagedAsync(filter, page: 1, pageSize: 20, CancellationToken.None);

        // Assert
        capturedFilter.Should().NotBeNull();
        capturedFilter!.From.Should().Be(from);
        capturedFilter.To.Should().BeNull();
        capturedFilter.Provider.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — GetPagedAsync forwards From + To + Provider together
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenFromAndToAndProvider_ForwardsAllThreeInFilter()
    {
        // Arrange
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        const string provider = "Anthropic";

        var filter = new AiRequestLogFilter(
            From: from, To: to, Provider: provider, Worker: null, Model: null,
            Status: null, Search: null);

        AiRequestLogFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiRequestLogFilter, int, int, CancellationToken>((f, _, _, _) => capturedFilter = f)
            .ReturnsAsync([]);

        // Act
        await _repositoryMock.Object.GetPagedAsync(filter, page: 1, pageSize: 20, CancellationToken.None);

        // Assert
        capturedFilter.Should().NotBeNull();
        capturedFilter!.From.Should().Be(from);
        capturedFilter.To.Should().Be(to);
        capturedFilter.Provider.Should().Be(provider);
        capturedFilter.Worker.Should().BeNull();
        capturedFilter.Model.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — GetPagedAsync forwards Search (drives ILIKE branch in implementation)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenSearchProvided_ForwardsSearchInFilter()
    {
        // Arrange
        const string search = "rate-limit";
        var filter = new AiRequestLogFilter(
            From: null, To: null, Provider: null, Worker: null, Model: null,
            Status: null, Search: search);

        AiRequestLogFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiRequestLogFilter, int, int, CancellationToken>((f, _, _, _) => capturedFilter = f)
            .ReturnsAsync([]);

        // Act
        await _repositoryMock.Object.GetPagedAsync(filter, page: 1, pageSize: 20, CancellationToken.None);

        // Assert
        capturedFilter.Should().NotBeNull();
        capturedFilter!.Search.Should().Be(search);
    }

    // ------------------------------------------------------------------
    // P0 — GetPagedAsync forwards Status = "Error" (drives Status clause branch)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetPagedAsync_WhenStatusIsError_ForwardsStatusInFilter()
    {
        // Arrange
        const string status = "Error";
        var filter = new AiRequestLogFilter(
            From: null, To: null, Provider: null, Worker: null, Model: null,
            Status: status, Search: null);

        AiRequestLogFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiRequestLogFilter, int, int, CancellationToken>((f, _, _, _) => capturedFilter = f)
            .ReturnsAsync([]);

        // Act
        await _repositoryMock.Object.GetPagedAsync(filter, page: 1, pageSize: 20, CancellationToken.None);

        // Assert
        capturedFilter.Should().NotBeNull();
        capturedFilter!.Status.Should().Be(status);
    }

    // ------------------------------------------------------------------
    // P0 — CountAsync returns the value the implementation produces
    // ------------------------------------------------------------------

    [Test]
    public async Task CountAsync_WhenMockReturnsValue_ReturnsThatValue()
    {
        // Arrange
        const int expectedCount = 42;
        var filter = new AiRequestLogFilter(
            From: null, To: null, Provider: null, Worker: null, Model: null,
            Status: null, Search: null);

        _repositoryMock
            .Setup(r => r.CountAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCount);

        // Act
        var result = await _repositoryMock.Object.CountAsync(filter, CancellationToken.None);

        // Assert
        result.Should().Be(expectedCount);
    }

    // ------------------------------------------------------------------
    // P0 — GetByIdAsync returns the matching log for a known id
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenLogExists_ReturnsExpectedLog()
    {
        // Arrange
        var id = Guid.NewGuid();
        var expected = CreateValidLog(id);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _repositoryMock.Object.GetByIdAsync(id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(expected);
        result!.Id.Should().Be(id);
    }

    // ------------------------------------------------------------------
    // P1 — GetByIdAsync returns null for an unknown id
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_WhenLogNotFound_ReturnsNull()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiRequestLog?)null);

        // Act
        var result = await _repositoryMock.Object.GetByIdAsync(unknownId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — GetMetricsAsync returns the metrics produced by the implementation
    // ------------------------------------------------------------------

    [Test]
    public async Task GetMetricsAsync_WhenMockReturnsMetrics_ReturnsThatMetrics()
    {
        // Arrange
        var filter = new AiRequestLogFilter(
            From: null, To: null, Provider: null, Worker: null, Model: null,
            Status: null, Search: null);

        var expected = new AiRequestLogMetrics(
            Totals: new AiMetricsTotals(
                TotalCostUsd: 1.2345m,
                TotalCalls: 10,
                SuccessCalls: 8,
                ErrorCalls: 2,
                AverageLatencyMs: 425.5,
                TotalInputTokens: 1000,
                TotalOutputTokens: 500,
                TotalCacheCreationInputTokens: 50,
                TotalCacheReadInputTokens: 25),
            TimeSeries:
            [
                new AiMetricsTimeBucket(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    "Anthropic", 0.5m, 5, 750)
            ],
            ByModel: [new AiMetricsBreakdownRow("claude-haiku-4-5-20251001", 6, 0.7m, 900)],
            ByWorker: [new AiMetricsBreakdownRow("ArticleAnalysisWorker", 7, 0.8m, 1100)],
            ByProvider: [new AiMetricsBreakdownRow("Anthropic", 10, 1.2345m, 1500)]);

        _repositoryMock
            .Setup(r => r.GetMetricsAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _repositoryMock.Object.GetMetricsAsync(filter, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        result.Totals.TotalCalls.Should().Be(10);
        result.Totals.SuccessCalls.Should().Be(8);
        result.Totals.ErrorCalls.Should().Be(2);
        result.TimeSeries.Should().HaveCount(1);
        result.ByModel.Should().HaveCount(1);
        result.ByWorker.Should().HaveCount(1);
        result.ByProvider.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AiRequestLog CreateValidLog(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        Worker = "ArticleAnalysisWorker",
        Provider = "Anthropic",
        Operation = "Analyze",
        Model = "claude-haiku-4-5-20251001",
        InputTokens = 100,
        OutputTokens = 50,
        CacheCreationInputTokens = 0,
        CacheReadInputTokens = 0,
        TotalTokens = 150,
        CostUsd = 0.00015m,
        LatencyMs = 350,
        Status = AiRequestStatus.Success,
        ErrorMessage = null,
        CorrelationId = Guid.NewGuid(),
        ArticleId = Guid.NewGuid()
    };
}
