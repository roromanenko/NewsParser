using System.Data;
using FluentAssertions;
using Infrastructure.Persistence.Dapper;
using Moq;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;
using Pgvector;

namespace Infrastructure.Tests.Persistence;

[TestFixture]
public class VectorTypeHandlerTests
{
    private VectorTypeHandler _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new VectorTypeHandler();
    }

    // ------------------------------------------------------------------
    // Parse — P0: float array round-trips through Vector
    // ------------------------------------------------------------------

    [Test]
    public void Parse_WhenPassedFloatArray_ReturnsVectorWithSameValues()
    {
        // Arrange
        var floats = new float[] { 0.1f, 0.2f, 0.3f };

        // Act
        var result = _sut.Parse(floats);

        // Assert
        result.Should().NotBeNull();
        result.ToArray().Should().BeEquivalentTo(floats);
    }

    // ------------------------------------------------------------------
    // SetValue — P0: sets value and NpgsqlDbType.Unknown on NpgsqlParameter
    // ------------------------------------------------------------------

    [Test]
    public void SetValue_WhenParameterIsNpgsqlParameter_SetsValueAndDbTypeUnknown()
    {
        // Arrange
        var parameter = new NpgsqlParameter();
        var vector = new Vector(new float[] { 1.0f, 2.0f });

        // Act
        _sut.SetValue(parameter, vector);

        // Assert
        parameter.Value.Should().Be(vector);
        parameter.NpgsqlDbType.Should().Be(NpgsqlDbType.Unknown);
    }

    // ------------------------------------------------------------------
    // SetValue — P1: non-Npgsql parameter only gets Value set, no exception thrown
    // ------------------------------------------------------------------

    [Test]
    public void SetValue_WhenParameterIsNotNpgsqlParameter_SetsValueWithoutThrowing()
    {
        // Arrange
        var parameterMock = new Mock<IDbDataParameter>();
        var vector = new Vector(new float[] { 0.5f, 0.6f });

        // Act
        var act = () => _sut.SetValue(parameterMock.Object, vector);

        // Assert
        act.Should().NotThrow();
        parameterMock.VerifySet(p => p.Value = vector, Times.Once);
    }
}
