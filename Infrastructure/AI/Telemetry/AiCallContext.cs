namespace Infrastructure.AI.Telemetry;

public static class AiCallContext
{
    private static readonly AsyncLocal<Guid> _correlationId = new();
    private static readonly AsyncLocal<Guid?> _articleId = new();
    private static readonly AsyncLocal<string> _worker = new();

    public static Guid CurrentCorrelationId => _correlationId.Value;
    public static Guid? CurrentArticleId => _articleId.Value;
    public static string CurrentWorker => _worker.Value ?? string.Empty;

    public static IDisposable Push(Guid correlationId, Guid? articleId, string worker)
    {
        var previousCorrelationId = _correlationId.Value;
        var previousArticleId = _articleId.Value;
        var previousWorker = _worker.Value;

        _correlationId.Value = correlationId;
        _articleId.Value = articleId;
        _worker.Value = worker;

        return new ContextScope(previousCorrelationId, previousArticleId, previousWorker);
    }

    private sealed class ContextScope(Guid previousCorrelationId, Guid? previousArticleId, string? previousWorker) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _correlationId.Value = previousCorrelationId;
            _articleId.Value = previousArticleId;
            _worker.Value = previousWorker;
            _disposed = true;
        }
    }
}
