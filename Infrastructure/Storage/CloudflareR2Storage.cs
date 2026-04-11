using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Core.Interfaces.Storage;
using Infrastructure.Configuration;

namespace Infrastructure.Storage;

public class CloudflareR2Storage : IMediaStorage
{
	private readonly AmazonS3Client _client;
	private readonly string _bucketName;

	public CloudflareR2Storage(CloudflareR2Options options)
	{
		var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
		var config = new AmazonS3Config
		{
			ServiceURL = $"https://{options.AccountId}.r2.cloudflarestorage.com",
			ForcePathStyle = true,
			AuthenticationRegion = "auto"
		};
		_client = new AmazonS3Client(credentials, config);
		_bucketName = options.BucketName;
	}

	public void Dispose() => _client.Dispose();

	public async Task UploadAsync(
		string key,
		Stream content,
		string contentType,
		CancellationToken cancellationToken = default)
	{
		var request = new PutObjectRequest
		{
			BucketName = _bucketName,
			Key = key,
			InputStream = content,
			ContentType = contentType,
		};

		await _client.PutObjectAsync(request, cancellationToken);
	}
}
