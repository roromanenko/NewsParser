using System.Data;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Persistence.Dapper;
using Moq;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;

namespace Infrastructure.Tests.Persistence;

[TestFixture]
public class JsonbTypeHandlerStringListTests
{
    private JsonbTypeHandler<List<string>> _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new JsonbTypeHandler<List<string>>();
    }

    // ------------------------------------------------------------------
    // Parse — P0: valid JSON string deserializes to List<string>
    // ------------------------------------------------------------------

    [Test]
    public void Parse_WhenPassedValidJsonArray_ReturnsDeserializedStringList()
    {
        // Arrange
        var json = """["fact one","fact two","fact three"]""";

        // Act
        var result = _sut.Parse(json);

        // Assert
        result.Should().BeEquivalentTo(new List<string> { "fact one", "fact two", "fact three" });
    }

    // ------------------------------------------------------------------
    // Parse — P2: empty JSON array deserializes to empty list
    // ------------------------------------------------------------------

    [Test]
    public void Parse_WhenPassedEmptyJsonArray_ReturnsEmptyList()
    {
        // Arrange
        var json = "[]";

        // Act
        var result = _sut.Parse(json);

        // Assert
        result.Should().NotBeNull().And.BeEmpty();
    }

    // ------------------------------------------------------------------
    // SetValue — P0: non-null value is serialized to JSON with NpgsqlDbType.Jsonb
    // ------------------------------------------------------------------

    [Test]
    public void SetValue_WhenValueIsNotNull_SetsSerializedJsonAndJsonbDbType()
    {
        // Arrange
        var parameter = new NpgsqlParameter();
        var value = new List<string> { "alpha", "beta" };

        // Act
        _sut.SetValue(parameter, value);

        // Assert
        var expectedJson = JsonSerializer.Serialize(value);
        parameter.Value.Should().Be(expectedJson);
        parameter.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
    }

    // ------------------------------------------------------------------
    // SetValue — P1: null value is stored as DBNull
    // ------------------------------------------------------------------

    [Test]
    public void SetValue_WhenValueIsNull_SetsDbNullValue()
    {
        // Arrange
        var parameter = new NpgsqlParameter();

        // Act
        _sut.SetValue(parameter, null);

        // Assert
        parameter.Value.Should().Be(DBNull.Value);
    }

    // ------------------------------------------------------------------
    // SetValue — P1: non-Npgsql parameter receives the serialized value without throwing
    // ------------------------------------------------------------------

    [Test]
    public void SetValue_WhenParameterIsNotNpgsqlParameter_SetsValueWithoutThrowing()
    {
        // Arrange
        var parameterMock = new Mock<IDbDataParameter>();
        var value = new List<string> { "item" };

        // Act
        var act = () => _sut.SetValue(parameterMock.Object, value);

        // Assert
        act.Should().NotThrow();
        parameterMock.VerifySet(p => p.Value = It.IsAny<object>(), Times.Once);
    }
}

[TestFixture]
public class JsonbTypeHandlerGuidListTests
{
    private JsonbTypeHandler<List<Guid>> _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new JsonbTypeHandler<List<Guid>>();
    }

    // ------------------------------------------------------------------
    // Parse — P0: valid JSON GUID array round-trips correctly
    // ------------------------------------------------------------------

    [Test]
    public void Parse_WhenPassedValidJsonGuidArray_ReturnsDeserializedGuidList()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new List<Guid> { id1, id2 });

        // Act
        var result = _sut.Parse(json);

        // Assert
        result.Should().BeEquivalentTo(new List<Guid> { id1, id2 });
    }

    // ------------------------------------------------------------------
    // SetValue — P0: GUID list is serialized as JSON with NpgsqlDbType.Jsonb
    // ------------------------------------------------------------------

    [Test]
    public void SetValue_WhenListOfGuids_SetsJsonbTypeAndSerializedValue()
    {
        // Arrange
        var parameter = new NpgsqlParameter();
        var guids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        _sut.SetValue(parameter, guids);

        // Assert
        var expectedJson = JsonSerializer.Serialize(guids);
        parameter.Value.Should().Be(expectedJson);
        parameter.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
    }
}
