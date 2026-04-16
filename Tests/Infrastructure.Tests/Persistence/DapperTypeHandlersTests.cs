using FluentAssertions;
using Infrastructure.Persistence.Dapper;
using NUnit.Framework;

namespace Infrastructure.Tests.Persistence;

[TestFixture]
public class DapperTypeHandlersTests
{
    // ------------------------------------------------------------------
    // Register — P0: calling once does not throw
    // ------------------------------------------------------------------

    [Test]
    public void Register_WhenCalledOnce_DoesNotThrow()
    {
        // Arrange & Act
        var act = DapperTypeHandlers.Register;

        // Assert
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // Register — P2: calling multiple times does not throw (idempotency)
    // ------------------------------------------------------------------

    [Test]
    public void Register_WhenCalledMultipleTimes_DoesNotThrow()
    {
        // Arrange & Act
        var act = () =>
        {
            DapperTypeHandlers.Register();
            DapperTypeHandlers.Register();
            DapperTypeHandlers.Register();
        };

        // Assert
        act.Should().NotThrow();
    }
}
