namespace Infrastructure.Configuration;

public class PublicationMediaOptions
{
    public const string SectionName = "PublicationMedia";
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
    public int MaxFilesPerPublication { get; set; } = 10;
    public List<string> AllowedContentTypes { get; set; } =
    [
        "image/jpeg", "image/png", "image/webp", "image/gif", "video/mp4"
    ];
}
