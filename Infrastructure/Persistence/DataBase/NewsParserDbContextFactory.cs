using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence.DataBase;

public class NewsParserDbContextFactory : IDesignTimeDbContextFactory<NewsParserDbContext>
{
	public NewsParserDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<NewsParserDbContext>();
		optionsBuilder.UseNpgsql(
			"Host=localhost;Database=newsparser;Username=postgres;Password=postgres",
			o => o.UseVector());

		return new NewsParserDbContext(optionsBuilder.Options);
	}
}
