using Core.DomainModels;
using Core.DomainModels.AI;
using Core.Interfaces.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public class GeminiArticleAnalyzer : IArticleAnalyzer
{
	private readonly string _apiKey;
	private readonly string _model;
	private readonly string _prompt;
	private readonly HttpClient _httpClient;

	public GeminiArticleAnalyzer(string apiKey, string model, string prompt, HttpClient httpClient)
	{
		_apiKey = apiKey;
		_model = model;
		_prompt = prompt;
		_httpClient = httpClient;
	}

	public async Task<ArticleAnalysisResult> AnalyzeAsync(Article article, CancellationToken cancellationToken = default)
	{
		var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

		var prompt = $"""
        {_prompt}

        SOURCE METADATA:
        Published: {article.PublishedAt:yyyy-MM-dd HH:mm UTC}
        Source URL: {article.OriginalUrl}
        Detected Language: {(string.IsNullOrWhiteSpace(article.Language) ? "unknown" : article.Language)}

        ARTICLE:
        Title: {article.Title}
        Content: {article.OriginalContent}
        """;

		var requestBody = JsonSerializer.Serialize(new
		{
			contents = new[]
			{
				new
				{
					parts = new[] { new { text = prompt } }
				}
			}
		});

		var httpResponse = await _httpClient.PostAsync(
			url,
			new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"),
			cancellationToken);

		httpResponse.EnsureSuccessStatusCode();

		var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

		using var doc = JsonDocument.Parse(responseJson);
		var text = doc.RootElement
			.GetProperty("candidates")[0]
			.GetProperty("content")
			.GetProperty("parts")[0]
			.GetProperty("text")
			.GetString() ?? string.Empty;

		return ParseAnalysisResult(text);
	}

	private static ArticleAnalysisResult ParseAnalysisResult(string json)
	{
		json = json
			.Replace("```json", string.Empty)
			.Replace("```", string.Empty)
			.Trim();

		// Gemini sometimes returns single-quoted JSON instead of valid JSON
		if (!json.StartsWith('{') && !json.StartsWith('['))
			json = json[json.IndexOf('{')..];

		// Gemini sometimes escapes single quotes as \' which is invalid in JSON
		json = json.Replace("\\'", "'");

		if (json.Contains('\''))
			json = NormalizeSingleQuotedJson(json);

		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

		json = RepairUnescapedQuotes(json);
		var result = JsonSerializer.Deserialize<ArticleAnalysisResult>(json, options);

		result = result ?? throw new InvalidOperationException("Gemini returned null result");

		if (string.IsNullOrWhiteSpace(result.Category))
			throw new InvalidOperationException("Gemini returned empty Category");
		if (string.IsNullOrWhiteSpace(result.Language))
			throw new InvalidOperationException("Gemini returned empty Language");
		if (string.IsNullOrWhiteSpace(result.Sentiment))
			throw new InvalidOperationException("Gemini returned empty Sentiment");
		if (result.Tags.Count == 0)
			throw new InvalidOperationException("Gemini returned empty Tags");

		return result;
	}

	/// <summary>
	/// Fixes JSON where string values contain unescaped double-quote characters,
	/// e.g. "summary": "word"word" → "summary": "word\"word"
	/// Strategy: a " is a legitimate string terminator only if the next non-space
	/// character is a JSON structural token (: , } ]).
	/// </summary>
	private static string RepairUnescapedQuotes(string json)
	{
		var sb = new System.Text.StringBuilder(json.Length + 16);
		bool inString = false;

		for (int i = 0; i < json.Length; i++)
		{
			char c = json[i];

			// Pass through escape sequences untouched
			if (inString && c == '\\' && i + 1 < json.Length)
			{
				sb.Append(c);
				sb.Append(json[++i]);
				continue;
			}

			if (c == '"')
			{
				if (!inString)
				{
					inString = true;
					sb.Append(c);
					continue;
				}

				// Check whether this " legitimately closes the string
				int j = i + 1;
				while (j < json.Length && json[j] == ' ') j++;
				bool isCloser = j >= json.Length || json[j] is ':' or ',' or '}' or ']';

				if (isCloser)
				{
					inString = false;
					sb.Append(c);
				}
				else
				{
					// Unescaped quote inside a value — escape it
					sb.Append('\\');
					sb.Append('"');
				}
				continue;
			}

			sb.Append(c);
		}

		return sb.ToString();
	}

	private static string NormalizeSingleQuotedJson(string json)
	{
		var sb = new System.Text.StringBuilder(json.Length);
		bool inString = false;
		char stringChar = '"';

		for (int i = 0; i < json.Length; i++)
		{
			char c = json[i];

			if (!inString && (c == '\'' || c == '"'))
			{
				inString = true;
				stringChar = c;
				sb.Append('"');
				continue;
			}

			if (inString && c == stringChar)
			{
				// Check for escaped quote
				if (i > 0 && json[i - 1] == '\\')
				{
					sb.Append(c);
					continue;
				}
				inString = false;
				sb.Append('"');
				continue;
			}

			if (inString && c == '"' && stringChar == '\'')
			{
				sb.Append('\\');
				sb.Append('"');
				continue;
			}

			sb.Append(c);
		}

		return sb.ToString();
	}
}