using SearchEngineService.Transport;

namespace SearchEngineService.Providers
{
    public interface IProviderClient
    {
        string Name { get; }
        Task<IReadOnlyList<NormalizedContent>> SearchAsync(string query, int page, int size, CancellationToken ct);
    }
}
