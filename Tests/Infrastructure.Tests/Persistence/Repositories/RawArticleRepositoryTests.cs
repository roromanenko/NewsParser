using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using Pgvector;

namespace Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Custom cache-key factory so EF Core builds a fresh model for
/// InMemoryNewsParserDbContext rather than reusing the cached production model.
/// </summary>
internal sealed class InMemoryModelCacheKeyFactory : IModelCacheKeyFactory
{
	public object Create(DbContext context, bool designTime) =>
		(context.GetType(), designTime);
}

/// <summary>
/// Minimal subclass of NewsParserDbContext for InMemory testing.
/// Configures only SourceEntity and RawArticleEntity (the two tables used by
/// RawArticleRepository), ignoring all other entity types and the pgvector
/// column that the InMemory provider cannot handle.
/// </summary>
internal sealed class InMemoryNewsParserDbContext : NewsParserDbContext
{
	public InMemoryNewsParserDbContext(DbContextOptions<NewsParserDbContext> options)
		: base(options) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Ignore the pgvector Value Object type so EF doesn't try to bind its constructor
		modelBuilder.Ignore<Vector>();

		// Ignore all entity types not required by RawArticleRepository tests
		modelBuilder.Ignore<ArticleEntity>();
		modelBuilder.Ignore<PublicationEntity>();
		modelBuilder.Ignore<UserEntity>();
		modelBuilder.Ignore<PublishLogEntity>();
		modelBuilder.Ignore<PublishTargetEntity>();
		modelBuilder.Ignore<EventEntity>();
		modelBuilder.Ignore<EventUpdateEntity>();
		modelBuilder.Ignore<ContradictionEntity>();
		modelBuilder.Ignore<ContradictionArticleEntity>();

		modelBuilder.Entity<SourceEntity>(b =>
		{
			b.HasKey(s => s.Id);
			b.HasMany(s => s.RawArticles)
			 .WithOne(r => r.Source)
			 .HasForeignKey(r => r.SourceId);
		});

		modelBuilder.Entity<RawArticleEntity>(b =>
		{
			b.HasKey(r => r.Id);
			b.HasOne(r => r.Source)
			 .WithMany(s => s.RawArticles)
			 .HasForeignKey(r => r.SourceId);

			// Exclude the pgvector column — InMemory provider cannot construct Vector
			b.Ignore(r => r.Embedding);
		});
	}
}

[TestFixture]
public class RawArticleRepositoryTests
{
	private InMemoryNewsParserDbContext _dbContext = null!;
	private RawArticleRepository _sut = null!;

	[SetUp]
	public void SetUp()
	{
		var options = new DbContextOptionsBuilder<NewsParserDbContext>()
			.UseInMemoryDatabase(Guid.NewGuid().ToString())
			.ReplaceService<IModelCacheKeyFactory, InMemoryModelCacheKeyFactory>()
			.Options;

		_dbContext = new InMemoryNewsParserDbContext(options);
		_sut = new RawArticleRepository(_dbContext);
	}

	[TearDown]
	public void TearDown() => _dbContext.Dispose();

	// ------------------------------------------------------------------
	// ExistsByUrlAsync
	// ------------------------------------------------------------------

	[Test]
	public async Task ExistsByUrlAsync_WhenUrlExistsWithPendingStatus_ReturnsTrue()
	{
		// Arrange
		var sourceId = await SeedSourceAsync();
		await SeedRawArticleAsync(sourceId, url: "https://example.com/article-pending", status: RawArticleStatus.Pending);

		// Act
		var result = await _sut.ExistsByUrlAsync("https://example.com/article-pending");

		// Assert
		result.Should().BeTrue();
	}

	[Test]
	public async Task ExistsByUrlAsync_WhenUrlExistsWithRejectedStatus_ReturnsFalse()
	{
		// Arrange
		var sourceId = await SeedSourceAsync();
		await SeedRawArticleAsync(sourceId, url: "https://example.com/article-rejected", status: RawArticleStatus.Rejected);

		// Act
		var result = await _sut.ExistsByUrlAsync("https://example.com/article-rejected");

		// Assert
		result.Should().BeFalse();
	}

	[Test]
	public async Task ExistsByUrlAsync_WhenUrlAbsent_ReturnsFalse()
	{
		// Arrange — empty DB, no articles seeded

		// Act
		var result = await _sut.ExistsByUrlAsync("https://example.com/non-existent");

		// Assert
		result.Should().BeFalse();
	}

	// ------------------------------------------------------------------
	// GetRecentTitlesForDeduplicationAsync
	// ------------------------------------------------------------------

	[Test]
	public async Task GetRecentTitlesForDeduplicationAsync_WhenTwoArticlesInWindowAndOneOutside_ReturnsTwoTitles()
	{
		// Arrange
		var sourceId = await SeedSourceAsync();
		const int windowHours = 24;
		var now = DateTimeOffset.UtcNow;

		await SeedRawArticleAsync(sourceId,
			title: "Article within window 1",
			publishedAt: now.AddHours(-1),
			status: RawArticleStatus.Pending);

		await SeedRawArticleAsync(sourceId,
			title: "Article within window 2",
			publishedAt: now.AddHours(-23),
			status: RawArticleStatus.Pending);

		await SeedRawArticleAsync(sourceId,
			title: "Article outside window",
			publishedAt: now.AddHours(-25),
			status: RawArticleStatus.Pending);

		// Act
		var result = await _sut.GetRecentTitlesForDeduplicationAsync(windowHours);

		// Assert
		result.Should().HaveCount(2);
		result.Should().BeEquivalentTo(new[] { "Article within window 1", "Article within window 2" });
	}

	[Test]
	public async Task GetRecentTitlesForDeduplicationAsync_WhenAllArticlesAreRejected_ReturnsEmptyList()
	{
		// Arrange
		var sourceId = await SeedSourceAsync();
		var now = DateTimeOffset.UtcNow;

		await SeedRawArticleAsync(sourceId,
			title: "Rejected article 1",
			publishedAt: now.AddHours(-1),
			status: RawArticleStatus.Rejected);

		await SeedRawArticleAsync(sourceId,
			title: "Rejected article 2",
			publishedAt: now.AddHours(-2),
			status: RawArticleStatus.Rejected);

		// Act
		var result = await _sut.GetRecentTitlesForDeduplicationAsync(windowHours: 24);

		// Assert
		result.Should().BeEmpty();
	}

	// ------------------------------------------------------------------
	// Helpers
	// ------------------------------------------------------------------

	private async Task<Guid> SeedSourceAsync()
	{
		var source = new SourceEntity
		{
			Id = Guid.NewGuid(),
			Name = "Test Source",
			Url = $"https://source-{Guid.NewGuid()}.com/rss",
			IsActive = true,
			Type = SourceType.Rss.ToString()
		};

		_dbContext.Sources.Add(source);
		await _dbContext.SaveChangesAsync();
		return source.Id;
	}

	private async Task SeedRawArticleAsync(
		Guid sourceId,
		string? title = null,
		string? url = null,
		RawArticleStatus status = RawArticleStatus.Pending,
		DateTimeOffset? publishedAt = null)
	{
		_dbContext.RawArticles.Add(new RawArticleEntity
		{
			Id = Guid.NewGuid(),
			SourceId = sourceId,
			ExternalId = Guid.NewGuid().ToString(),
			Title = title ?? "Default Title",
			OriginalUrl = url ?? $"https://example.com/{Guid.NewGuid()}",
			Status = status.ToString(),
			PublishedAt = publishedAt ?? DateTimeOffset.UtcNow,
			Content = "Default content"
		});

		await _dbContext.SaveChangesAsync();
	}
}
