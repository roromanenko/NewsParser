namespace Api.Controllers;

internal static class SortOptions
{
	public static readonly HashSet<string> BasicSortValues = ["newest", "oldest"];
}

internal static class PaginationDefaults
{
	public const int MaxPageSize = 100;
	public const int DefaultPageSize = 20;
}
