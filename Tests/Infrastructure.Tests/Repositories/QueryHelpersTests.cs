using FluentAssertions;
using Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Infrastructure.Tests.Repositories;

[TestFixture]
public class QueryHelpersTests
{
    // ------------------------------------------------------------------
    // P0 — Escapes % to \%
    // ------------------------------------------------------------------

    [Test]
    public void EscapeILikePattern_WhenInputContainsPercent_EscapesPercent()
    {
        // Arrange
        const string input = "100% sure";

        // Act
        var result = QueryHelpers.EscapeILikePattern(input);

        // Assert
        result.Should().Be(@"100\% sure");
    }

    // ------------------------------------------------------------------
    // P0 — Escapes _ to \_
    // ------------------------------------------------------------------

    [Test]
    public void EscapeILikePattern_WhenInputContainsUnderscore_EscapesUnderscore()
    {
        // Arrange
        const string input = "hello_world";

        // Act
        var result = QueryHelpers.EscapeILikePattern(input);

        // Assert
        result.Should().Be(@"hello\_world");
    }

    // ------------------------------------------------------------------
    // P0 — Plain alphanumeric string passes through unchanged
    // ------------------------------------------------------------------

    [Test]
    public void EscapeILikePattern_WhenInputHasNoSpecialChars_ReturnsInputUnchanged()
    {
        // Arrange
        const string input = "breaking news";

        // Act
        var result = QueryHelpers.EscapeILikePattern(input);

        // Assert
        result.Should().Be("breaking news");
    }

    // ------------------------------------------------------------------
    // P2 — Empty string returns empty string
    // ------------------------------------------------------------------

    [Test]
    public void EscapeILikePattern_WhenInputIsEmpty_ReturnsEmptyString()
    {
        // Arrange
        const string input = "";

        // Act
        var result = QueryHelpers.EscapeILikePattern(input);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P1 — Both special chars in one string are both escaped
    // ------------------------------------------------------------------

    [Test]
    public void EscapeILikePattern_WhenInputContainsBothPercentAndUnderscore_EscapesBoth()
    {
        // Arrange
        const string input = "top_10%";

        // Act
        var result = QueryHelpers.EscapeILikePattern(input);

        // Assert
        result.Should().Be(@"top\_10\%");
    }
}
