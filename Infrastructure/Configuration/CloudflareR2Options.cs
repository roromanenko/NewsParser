namespace Infrastructure.Configuration;

public class CloudflareR2Options
{
    public const string SectionName = "CloudflareR2";
    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;
    public int DownloadTimeoutSeconds { get; set; } = 30;
}
