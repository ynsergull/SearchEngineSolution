// SearchEngineService.Tests/FakeProviderClient.cs

using SearchEngineService.Providers;
using SearchEngineService.Transport;
using System.Collections.Immutable;

// Projedeki ContentType enum'u için kısa ad
using CT = SearchEngineService.Models.ContentType;

namespace SearchEngineService.Tests;

/// <summary>
/// Entegre testlerde gerçek provider'lar yerine kullanılan basit sahte (fake) provider.
/// Kendi içinde seed edilmiş veriyi tutar ve title içinde query geçenleri döner.
/// </summary>
public sealed class FakeProviderClient : IProviderClient
{
    public string Name { get; }
    private readonly ImmutableArray<NormalizedContent> _data;

    /// <summary>
    /// Testlerde DI'ya tek instance olarak verilecek sahte provider.
    /// </summary>
    public FakeProviderClient(string name, IEnumerable<NormalizedContent> seed)
    {
        Name = name;
        _data = seed.ToImmutableArray();
    }

    /// <summary>
    /// Basit arama: Title içinde query geçen kayıtları sayfalayarak döner.
    /// </summary>
    public Task<IReadOnlyList<NormalizedContent>> SearchAsync(string query, int page, int size, CancellationToken ct)
    {
        var q = (query ?? string.Empty).Trim();

        var result = _data
            .Where(x => (x.Title ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
            .Skip(Math.Max(0, (page - 1) * size))
            .Take(Math.Max(1, size))
            .ToList();

        return Task.FromResult((IReadOnlyList<NormalizedContent>)result);
    }

    /// <summary>
    /// Hızlı başlamak için örnek veri seti.
    /// </summary>
    public static IEnumerable<NormalizedContent> Seed() => new[]
    {
        new NormalizedContent(
            Provider: "Fake",
            ExternalId: "v1",
            Type: CT.Video,
            Title: "Go Concurrency Patterns",
            Description: "demo",
            Url: "mock://fake/v1",
            Views: 1000,
            Likes: 50,
            Reactions: 0,
            ReadingTime: null,
            PublishedAt: DateTime.UtcNow.AddDays(-1)
        ),
        new NormalizedContent(
            Provider: "Fake",
            ExternalId: "a1",
            Type: CT.Text,
            Title: "Clean Architecture in Go",
            Description: "article",
            Url: "mock://fake/a1",
            Views: 0,
            Likes: 0,
            Reactions: 10,
            ReadingTime: 8,
            PublishedAt: DateTime.UtcNow.AddDays(-2)
        )
    };
}
