using Core.DomainModels.AI;
using System.Text.Json;

namespace Infrastructure.AI;

public static class ArticleJsonHelper
{
	public static ArticleAnalysisResult ParseAnalysisResult(string json)
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

		json = RepairMissingBraces(json);

		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

		ArticleAnalysisResult? result;
		try
		{
			result = JsonSerializer.Deserialize<ArticleAnalysisResult>(json, options);
		}
		catch (JsonException)
		{
			json = RepairUnescapedQuotes(json);
			result = JsonSerializer.Deserialize<ArticleAnalysisResult>(json, options);
		}

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
	public static string RepairUnescapedQuotes(string json)
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
				bool isCloser = j >= json.Length || IsJsonStructuralToken(json[j]);

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

	public static string NormalizeSingleQuotedJson(string json)
	{
		var sb = new System.Text.StringBuilder(json.Length);
		bool inString = false;
		char stringChar = '"';

		for (int i = 0; i < json.Length; i++)
		{
			char c = json[i];

			// Handle escape sequences inside strings
			if (inString && c == '\\' && i + 1 < json.Length)
			{
				char next = json[i + 1];
				if (next == '\'')
				{
					// \' is not a valid JSON escape — emit the literal single quote instead
					sb.Append('\'');
					i++;
				}
				else
				{
					sb.Append(c);
					sb.Append(json[++i]);
				}
				continue;
			}

			if (!inString && (c == '\'' || c == '"'))
			{
				inString = true;
				stringChar = c;
				sb.Append('"');
				continue;
			}

			if (inString && stringChar == '\'')
			{
				if (c == '"')
				{
					// Double-quote inside a single-quoted string — must be escaped in the output
					sb.Append('\\');
					sb.Append('"');
					continue;
				}

				if (c == '\'')
				{
					bool isCloser = IsSingleQuoteCloser(json, i);

					if (isCloser)
					{
						inString = false;
						sb.Append('"');
					}
					else
					{
						// Inner single quote — emit as-is (no escaping needed in double-quoted output)
						sb.Append('\'');
					}
					continue;
				}
			}

			if (inString && stringChar == '"' && c == '"')
			{
				inString = false;
				sb.Append('"');
				continue;
			}

			sb.Append(c);
		}

		return sb.ToString();
	}

	/// <summary>
	/// Appends missing closing braces when the JSON object is truncated.
	/// Only counts braces that are outside of string values.
	/// </summary>
	public static string RepairMissingBraces(string json)
	{
		int depth = CountUnmatchedOpenBraces(json);

		if (depth == 0)
			return json;

		return json + new string('}', depth);
	}

	private static int CountUnmatchedOpenBraces(string json)
	{
		bool inString = false;
		int depth = 0;

		for (int i = 0; i < json.Length; i++)
		{
			char c = json[i];

			if (inString && c == '\\' && i + 1 < json.Length)
			{
				i++;
				continue;
			}

			if (c == '"')
			{
				inString = !inString;
				continue;
			}

			if (inString) continue;

			if (c == '{') depth++;
			else if (c == '}') depth--;
		}

		return Math.Max(depth, 0);
	}

	/// <summary>
	/// Determines whether the single quote at position <paramref name="pos"/> inside
	/// a single-quoted string is a legitimate string terminator.
	///
	/// Strategy:
	/// 1. Find the next non-whitespace character after pos.
	/// 2. If it is ':', '}', ']', or end-of-input → always a closer.
	/// 3. If it is ',' → look further past the ',' and whitespace.
	///    If what follows is a JSON value opener (' " { [) or another structural token,
	///    the comma is a JSON property separator → real closer.
	///    Otherwise the comma is sentence punctuation → inner character.
	/// </summary>
	private static bool IsSingleQuoteCloser(string json, int pos)
	{
		int j = pos + 1;
		while (j < json.Length && json[j] == ' ') j++;

		if (j >= json.Length) return true;

		char next = json[j];

		if (next is ':' or '}' or ']') return true;

		if (next == ',')
		{
			int k = j + 1;
			while (k < json.Length && (json[k] == ' ' || json[k] == '\t' || json[k] == '\r' || json[k] == '\n')) k++;
			if (k >= json.Length) return true;
			char afterComma = json[k];
			return afterComma is '\'' or '"' or '{' or '[' or '}' or ']';
		}

		return false;
	}

	private static bool IsJsonStructuralToken(char c) =>
		c is ':' or ',' or '}' or ']';
}
