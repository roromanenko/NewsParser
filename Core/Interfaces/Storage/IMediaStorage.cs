namespace Core.Interfaces.Storage;

public interface IMediaStorage : IDisposable
{
	Task UploadAsync(
		string key,
		Stream content,
		string contentType,
		CancellationToken cancellationToken = default);
}
