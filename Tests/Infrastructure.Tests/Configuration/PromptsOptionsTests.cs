using FluentAssertions;
using Infrastructure.Configuration;
using NUnit.Framework;

namespace Infrastructure.Tests.Configuration;

/// <summary>
/// Tests for PromptsOptions placeholder substitution.
///
/// PromptsOptions reads prompt files from disk at property-access time and replaces
/// the {OUTPUT_LANGUAGE} token with the constructor-injected target language name.
/// These tests write temp prompt files to a per-test directory (no shared state)
/// and point the path properties at those files via absolute paths — Path.Combine
/// respects an absolute second argument, so AppContext.BaseDirectory is bypassed.
/// </summary>
[TestFixture]
public class PromptsOptionsTests
{
	private const string OutputLanguagePlaceholder = "{OUTPUT_LANGUAGE}";
	private const string TargetLanguageName = "English";
	private const string ForbiddenLanguageName = "Ukrainian";

	private string _tempDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), $"prompts-options-tests-{Guid.NewGuid()}");
		Directory.CreateDirectory(_tempDirectory);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}
	}

	[Test]
	public void Analyzer_WhenPromptContainsOutputLanguagePlaceholder_SubstitutesConfiguredLanguageName()
	{
		// Arrange
		const string promptTemplate =
			"SUMMARY: Summary must be 2-3 sentences in {OUTPUT_LANGUAGE}, regardless of the article language.";
		var promptPath = WritePromptFile("analyzer.txt", promptTemplate);

		var sut = new PromptsOptions(TargetLanguageName)
		{
			AnalyzerPath = promptPath
		};

		// Act
		var result = sut.Analyzer;

		// Assert
		result.Should().Contain(TargetLanguageName);
		result.Should().NotContain(OutputLanguagePlaceholder);
		result.Should().NotContain(ForbiddenLanguageName);
	}

	[Test]
	public void Generator_WhenPromptHasNoPlaceholder_ReturnsFileContentUnchanged()
	{
		// Arrange
		const string promptWithoutPlaceholder =
			"You are a professional news journalist. Return ONLY a valid JSON object.";
		var promptPath = WritePromptFile("generator.txt", promptWithoutPlaceholder);

		var sut = new PromptsOptions(TargetLanguageName)
		{
			GeneratorPath = promptPath
		};

		// Act
		var result = sut.Generator;

		// Assert
		result.Should().Be(promptWithoutPlaceholder);
	}

	private string WritePromptFile(string fileName, string content)
	{
		var path = Path.Combine(_tempDirectory, fileName);
		File.WriteAllText(path, content);
		return path;
	}
}
