using FluentAssertions;
using Infrastructure.AI;
using NUnit.Framework;

namespace Infrastructure.Tests.AI;

[TestFixture]
public class ArticleJsonHelperTests
{
	// ------------------------------------------------------------------
	// ParseAnalysisResult — happy paths
	// ------------------------------------------------------------------

	[Test]
	public void ParseAnalysisResult_WhenValidJson_ReturnsPopulatedResult()
	{
		const string json = """
			{
			  "category": "Technology",
			  "tags": ["AI", "LLM"],
			  "sentiment": "positive",
			  "language": "en",
			  "summary": "An article about AI."
			}
			""";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Category.Should().Be("Technology");
		result.Tags.Should().BeEquivalentTo(["AI", "LLM"]);
		result.Sentiment.Should().Be("positive");
		result.Language.Should().Be("en");
		result.Summary.Should().Be("An article about AI.");
	}

	[Test]
	public void ParseAnalysisResult_WhenJsonWrappedInMarkdownFence_ParsesSuccessfully()
	{
		const string json = """
			```json
			{"category":"Tech","tags":["tag1"],"sentiment":"neutral","language":"en","summary":""}
			```
			""";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Category.Should().Be("Tech");
		result.Tags.Should().ContainSingle("tag1");
	}

	[Test]
	public void ParseAnalysisResult_WhenJsonPrecededByNonJsonText_SkipsLeadingText()
	{
		// Gemini occasionally prefixes a sentence before the JSON object
		const string json = "Here is the result: {\"category\":\"Politics\",\"tags\":[\"election\"],\"sentiment\":\"negative\",\"language\":\"en\",\"summary\":\"s\"}";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Category.Should().Be("Politics");
	}

	[Test]
	public void ParseAnalysisResult_WhenPropertyNamesHaveMixedCase_DeserializesSuccessfully()
	{
		// PropertyNameCaseInsensitive = true
		const string json = """{"Category":"Sport","Tags":["tennis"],"Sentiment":"neutral","Language":"de","Summary":"s"}""";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Category.Should().Be("Sport");
		result.Language.Should().Be("de");
	}

	[Test]
	public void ParseAnalysisResult_WhenSingleQuotedJson_NormalizesAndParses()
	{
		// Gemini may return single-quoted JSON
		const string json = "{'category':'Economy','tags':['finance','market'],'sentiment':'neutral','language':'en','summary':'s'}";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Category.Should().Be("Economy");
		result.Tags.Should().BeEquivalentTo(["finance", "market"]);
	}

	[Test]
	public void ParseAnalysisResult_WhenValueContainsEscapedSingleQuote_StripsBadEscape()
	{
		// Gemini sometimes emits \' which is invalid JSON
		const string json = """{"category":"Culture","tags":["art"],"sentiment":"positive","language":"en","summary":"It\'s great"}""";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Summary.Should().Be("It's great");
	}

	[Test]
	public void ParseAnalysisResult_WhenSummaryContainsUnescapedDoubleQuote_RepairsAndParses()
	{
		// "summary": "He said "hello" today" — unescaped inner quotes
		const string json = """{"category":"News","tags":["world"],"sentiment":"neutral","language":"en","summary":"He said "hello" today"}""";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Summary.Should().Contain("hello");
	}

	[Test]
	public void ParseAnalysisResult_RawInput_RepairsAndParses()
	{
		const string json =
			"""
			{
				'category': 'Politics',
				'tags': ['иммиграция', 'сша', 'ice', 'родильный туризм', 'гражданство по рождению', 'национальная безопасность'],
				'sentiment': 'Negative',
				'language': 'ru',
				'summary': 'Служба імміграційного та митного контролю США (ICE) розпочала операцію з виявлення мереж, що допомагають вагітним іноземкам в\\'їжджати до країни для народження дитини. Ця ініціатива спрямована на боротьбу з 'пологовим туризмом', який вважається шахрайством, фінансовими злочинами та загрозою національній безпеці. Колишній президент Трамп неодноразово критикував право на отримання громадянства за народженням у США.'}
			""";

		var result = ArticleJsonHelper.ParseAnalysisResult(json);

		result.Category.Should().Contain("Politics");
	}

	// ------------------------------------------------------------------
	// ParseAnalysisResult — validation failures
	// ------------------------------------------------------------------

	[Test]
	public void ParseAnalysisResult_WhenCategoryIsEmpty_ThrowsInvalidOperationException()
	{
		const string json = """{"category":"","tags":["t"],"sentiment":"pos","language":"en","summary":"s"}""";

		var act = () => ArticleJsonHelper.ParseAnalysisResult(json);

		act.Should().Throw<InvalidOperationException>().WithMessage("*Category*");
	}

	[Test]
	public void ParseAnalysisResult_WhenLanguageIsEmpty_ThrowsInvalidOperationException()
	{
		const string json = """{"category":"Tech","tags":["t"],"sentiment":"pos","language":"","summary":"s"}""";

		var act = () => ArticleJsonHelper.ParseAnalysisResult(json);

		act.Should().Throw<InvalidOperationException>().WithMessage("*Language*");
	}

	[Test]
	public void ParseAnalysisResult_WhenSentimentIsEmpty_ThrowsInvalidOperationException()
	{
		const string json = """{"category":"Tech","tags":["t"],"sentiment":"","language":"en","summary":"s"}""";

		var act = () => ArticleJsonHelper.ParseAnalysisResult(json);

		act.Should().Throw<InvalidOperationException>().WithMessage("*Sentiment*");
	}

	[Test]
	public void ParseAnalysisResult_WhenTagsIsEmpty_ThrowsInvalidOperationException()
	{
		const string json = """{"category":"Tech","tags":[],"sentiment":"pos","language":"en","summary":"s"}""";

		var act = () => ArticleJsonHelper.ParseAnalysisResult(json);

		act.Should().Throw<InvalidOperationException>().WithMessage("*Tags*");
	}

	[Test]
	public void ParseAnalysisResult_WhenJsonIsCompletelyMalformed_ThrowsJsonException()
	{
		const string json = "not json at all";

		var act = () => ArticleJsonHelper.ParseAnalysisResult(json);

		act.Should().Throw<Exception>();
	}

	// ------------------------------------------------------------------
	// RepairUnescapedQuotes
	// ------------------------------------------------------------------

	[Test]
	public void RepairUnescapedQuotes_WhenNoUnescapedQuotes_ReturnsSameString()
	{
		const string json = """{"key":"value"}""";

		var result = ArticleJsonHelper.RepairUnescapedQuotes(json);

		result.Should().Be(json);
	}

	[Test]
	public void RepairUnescapedQuotes_WhenValueContainsUnescapedQuote_EscapesIt()
	{
		// "summary": "He said "hi""  →  "summary": "He said \"hi\""
		const string json = """{"summary":"He said "hi""}""";

		var result = ArticleJsonHelper.RepairUnescapedQuotes(json);

		result.Should().Contain("\\\"hi\\\"");
	}

	[Test]
	public void RepairUnescapedQuotes_WhenValueAlreadyHasEscapedQuote_LeavesEscapeIntact()
	{
		const string json = """{"key":"word\"word"}""";

		var result = ArticleJsonHelper.RepairUnescapedQuotes(json);

		result.Should().Be(json);
	}

	[Test]
	public void RepairUnescapedQuotes_WhenMultipleUnescapedQuotesInSameValue_EscapesAll()
	{
		const string json = """{"a":"x"y"z"}""";

		var result = ArticleJsonHelper.RepairUnescapedQuotes(json);

		result.Should().Be("""{"a":"x\"y\"z"}""");
	}

	[Test]
	public void RepairUnescapedQuotes_WhenEmptyString_ReturnsEmptyString()
	{
		var result = ArticleJsonHelper.RepairUnescapedQuotes(string.Empty);

		result.Should().BeEmpty();
	}

	// ------------------------------------------------------------------
	// NormalizeSingleQuotedJson
	// ------------------------------------------------------------------

	[Test]
	public void NormalizeSingleQuotedJson_WhenAllSingleQuotes_ReplacesWithDoubleQuotes()
	{
		const string input = "{'key':'value'}";

		var result = ArticleJsonHelper.NormalizeSingleQuotedJson(input);

		result.Should().Be("""{"key":"value"}""");
	}

	[Test]
	public void NormalizeSingleQuotedJson_WhenDoubleQuoteInsideSingleQuotedString_EscapesIt()
	{
		// Single-quoted value that contains a double-quote must be escaped in output
		const string input = "{'key':'say \"hi\"'}";

		var result = ArticleJsonHelper.NormalizeSingleQuotedJson(input);

		result.Should().Be("""{"key":"say \"hi\""}""");
	}

	[Test]
	public void NormalizeSingleQuotedJson_WhenMixedSingleAndDoubleQuotes_NormalizesCorrectly()
	{
		const string input = """{"key":'value'}""";

		var result = ArticleJsonHelper.NormalizeSingleQuotedJson(input);

		result.Should().Be("""{"key":"value"}""");
	}

	[Test]
	public void NormalizeSingleQuotedJson_WhenAlreadyDoubleQuoted_ReturnsSameString()
	{
		const string input = """{"key":"value"}""";

		var result = ArticleJsonHelper.NormalizeSingleQuotedJson(input);

		result.Should().Be(input);
	}

	[Test]
	public void NormalizeSingleQuotedJson_WhenSingleQuotedArray_NormalizesCorrectly()
	{
		const string input = "['alpha','beta','gamma']";

		var result = ArticleJsonHelper.NormalizeSingleQuotedJson(input);

		result.Should().Be("""["alpha","beta","gamma"]""");
	}

	[Test]
	public void NormalizeSingleQuotedJson_WhenValueContainsInnerSingleQuote_PreservesInnerQuote()
	{
		// The inner ' in "it's" must not be treated as a string terminator
		const string input = "{'key':'it's here'}";

		// Act
		var result = ArticleJsonHelper.NormalizeSingleQuotedJson(input);

		// Assert
		result.Should().Be("""{"key":"it's here"}""");
	}

	// ------------------------------------------------------------------
	// RepairMissingBraces
	// ------------------------------------------------------------------

	[Test]
	public void RepairMissingBraces_WhenBracesAreBalanced_ReturnsSameString()
	{
		// Arrange
		const string json = """{"key":"value"}""";

		// Act
		var result = ArticleJsonHelper.RepairMissingBraces(json);

		// Assert
		result.Should().Be(json);
	}

	[Test]
	public void RepairMissingBraces_WhenOneMissingClosingBrace_AppendsOne()
	{
		// Arrange
		const string json = """{"key":"value" """;

		// Act
		var result = ArticleJsonHelper.RepairMissingBraces(json);

		// Assert
		result.Should().Be("""{"key":"value" }""");
	}

	[Test]
	public void RepairMissingBraces_WhenTwoMissingClosingBraces_AppendsTwo()
	{
		// Arrange — outer closing brace is missing, inner object is closed
		const string json = """{"a":{"b":"c"}""";

		// Act
		var result = ArticleJsonHelper.RepairMissingBraces(json);

		// Assert
		result.Should().Be("""{"a":{"b":"c"}}""");
	}
}
