using Infrastructure.Persistence.Entity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.DataBase;

public class NewsParserDbContext : DbContext
{
	public NewsParserDbContext(DbContextOptions<NewsParserDbContext> options)
		: base(options)
	{

	}

	public DbSet<RawArticleEntity> RawArticles { get; set; }
	public DbSet<ArticleEntity> Articles { get; set; }
	public DbSet<PublicationEntity> Publications { get; set; }
	public DbSet<UserEntity> Users { get; set; }
	public DbSet<SourceEntity> Sources { get; set; }
	public DbSet<PublishLogEntity> PublishLogs { get; set; }
	public DbSet<PublishTargetEntity> PublishTargets { get; set; }
	public DbSet<EventEntity> Events { get; set; }
	public DbSet<EventArticleEntity> EventArticles { get; set; }
	public DbSet<EventUpdateEntity> EventUpdates { get; set; }
	public DbSet<ContradictionEntity> Contradictions { get; set; }
	public DbSet<ContradictionArticleEntity> ContradictionArticles { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.HasPostgresExtension("vector");
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(NewsParserDbContext).Assembly);
		base.OnModelCreating(modelBuilder);
	}
}

