using FluentAssertions;
using Infrastructure.Persistence.Connection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Infrastructure.Tests.Persistence;

[TestFixture]
public class NpgsqlConnectionFactoryTests
{
    // ------------------------------------------------------------------
    // Constructor — P0: valid connection string does not throw
    //
    // NOTE: We only verify construction. CreateOpenAsync would require a real
    // PostgreSQL server — that is outside the scope of unit tests.
    // ------------------------------------------------------------------

    [Test]
    public void Constructor_WhenConnectionStringIsPresent_DoesNotThrow()
    {
        // Arrange
        var configuration = BuildConfiguration("Host=localhost;Database=test;Username=user;Password=pw");

        // Act
        var act = () => new NpgsqlConnectionFactory(configuration);

        // Assert
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // Constructor — P1: missing connection string throws InvalidOperationException
    // ------------------------------------------------------------------

    [Test]
    public void Constructor_WhenConnectionStringIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build(); // no connection strings configured

        // Act
        var act = () => new NpgsqlConnectionFactory(configuration);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NewsParserDbContext*");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IConfiguration BuildConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NewsParserDbContext"] = connectionString
            })
            .Build();
}
