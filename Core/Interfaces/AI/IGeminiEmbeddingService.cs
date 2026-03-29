namespace Core.Interfaces.AI;

public interface IGeminiEmbeddingService
{
	Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}