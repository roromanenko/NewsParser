using FluentAssertions;
using Infrastructure.AI.Telemetry;
using NUnit.Framework;

namespace Infrastructure.Tests.AI.Telemetry;

/// <summary>
/// Tests for <see cref="AiCallContext"/>.
///
/// The context uses <see cref="AsyncLocal{T}"/> to flow
/// CorrelationId / ArticleId / Worker across async call chains without
/// touching any Core interface signature. <c>Push</c> must save the
/// previous values and restore them on dispose (including when nested),
/// and the ambient values must flow across <c>await</c> points.
/// </summary>
[TestFixture]
public class AiCallContextTests
{
    // Reset the ambient values around each test so order-independence holds
    // even though AsyncLocal is test-scoped by the NUnit synchronization
    // context — defensive belt-and-braces.
    [SetUp]
    public void SetUp()
    {
        using var _ = AiCallContext.Push(Guid.Empty, null, string.Empty);
    }

    // ------------------------------------------------------------------
    // P0 — Push sets all three ambient values
    // ------------------------------------------------------------------

    [Test]
    public void Push_WhenCalledWithAllThreeArguments_SetsCorrelationIdArticleIdAndWorker()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        const string worker = "ArticleAnalysisWorker";

        // Act
        using var _ = AiCallContext.Push(correlationId, articleId, worker);

        // Assert
        AiCallContext.CurrentCorrelationId.Should().Be(correlationId);
        AiCallContext.CurrentArticleId.Should().Be(articleId);
        AiCallContext.CurrentWorker.Should().Be(worker);
    }

    // ------------------------------------------------------------------
    // P0 — Dispose restores the prior default (empty / null) values when
    //       Push is the only scope on the stack
    // ------------------------------------------------------------------

    [Test]
    public void Push_WhenScopeDisposed_RestoresPriorDefaultValues()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        const string worker = "PublicationGenerationWorker";

        // Act
        using (AiCallContext.Push(correlationId, articleId, worker))
        {
            // inside the using block the values are set
        }

        // Assert — after dispose, values are back to defaults
        AiCallContext.CurrentCorrelationId.Should().Be(Guid.Empty);
        AiCallContext.CurrentArticleId.Should().BeNull();
        AiCallContext.CurrentWorker.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P0 — Nested Push: inner values visible inside the inner block;
    //       outer values restored on inner dispose; outer defaults
    //       restored on outer dispose
    // ------------------------------------------------------------------

    [Test]
    public void Push_WhenNested_InnerValuesVisibleInsideAndOuterRestoredOnInnerDispose()
    {
        // Arrange
        var outerCorrelationId = Guid.NewGuid();
        var outerArticleId = Guid.NewGuid();
        const string outerWorker = "OuterWorker";

        var innerCorrelationId = Guid.NewGuid();
        var innerArticleId = Guid.NewGuid();
        const string innerWorker = "InnerWorker";

        // Act & Assert
        using (AiCallContext.Push(outerCorrelationId, outerArticleId, outerWorker))
        {
            AiCallContext.CurrentCorrelationId.Should().Be(outerCorrelationId);
            AiCallContext.CurrentArticleId.Should().Be(outerArticleId);
            AiCallContext.CurrentWorker.Should().Be(outerWorker);

            using (AiCallContext.Push(innerCorrelationId, innerArticleId, innerWorker))
            {
                // Inner scope visible
                AiCallContext.CurrentCorrelationId.Should().Be(innerCorrelationId);
                AiCallContext.CurrentArticleId.Should().Be(innerArticleId);
                AiCallContext.CurrentWorker.Should().Be(innerWorker);
            }

            // Outer restored after inner dispose
            AiCallContext.CurrentCorrelationId.Should().Be(outerCorrelationId);
            AiCallContext.CurrentArticleId.Should().Be(outerArticleId);
            AiCallContext.CurrentWorker.Should().Be(outerWorker);
        }

        // After outermost dispose everything is back to defaults
        AiCallContext.CurrentCorrelationId.Should().Be(Guid.Empty);
        AiCallContext.CurrentArticleId.Should().BeNull();
        AiCallContext.CurrentWorker.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P0 — Ambient values flow across an await boundary (the whole
    //       reason we use AsyncLocal rather than ThreadLocal)
    // ------------------------------------------------------------------

    [Test]
    public async Task Push_WhenAwaitYieldCrossed_AmbientValuesAreStillVisible()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var articleId = Guid.NewGuid();
        const string worker = "AsyncFlowWorker";

        // Act
        using var _ = AiCallContext.Push(correlationId, articleId, worker);
        await Task.Yield();

        // Assert
        AiCallContext.CurrentCorrelationId.Should().Be(correlationId);
        AiCallContext.CurrentArticleId.Should().Be(articleId);
        AiCallContext.CurrentWorker.Should().Be(worker);
    }
}
