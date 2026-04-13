namespace Infrastructure.Persistence.Repositories;

internal static class QueryHelpers
{
	public static string EscapeILikePattern(string input) =>
		input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
