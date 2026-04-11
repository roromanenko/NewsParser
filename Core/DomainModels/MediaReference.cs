namespace Core.DomainModels;

public record MediaReference(
    string Url,
    MediaKind Kind,
    string? DeclaredContentType,
    MediaSourceKind SourceKind = MediaSourceKind.Http,
    string? ExternalHandle = null);

public enum MediaSourceKind
{
    Http,
    Telegram
}
