namespace Api.Controllers;

internal static class SortOptions
{
	public const string Newest = "newest";
	public const string Oldest = "oldest";
	public const string Importance = "importance";

	public static readonly HashSet<string> BasicSortValues = [Newest, Oldest];
	public static readonly HashSet<string> EventSortValues = [Newest, Oldest, Importance];
}

internal static class PaginationDefaults
{
	public const int MaxPageSize = 100;
	public const int DefaultPageSize = 20;
}
