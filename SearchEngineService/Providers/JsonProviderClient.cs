using System.Text.Json;
using SearchEngineService.Models;
using SearchEngineService.Transport;

namespace SearchEngineService.Providers
{
    public class JsonProviderClient : IProviderClient
    {
        public string Name => "ProviderJson";

        public async Task<IReadOnlyList<NormalizedContent>> SearchAsync(string query, int page, int size, CancellationToken ct)
        {
            // Dosya adı seninle aynı: mocks/provider1.json
            using var fs = File.OpenRead("mocks/provider1.json");
            var payload = await JsonSerializer.DeserializeAsync<JsonPayload>(fs, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, ct);

            // Bu JSON örneğinde description/url yok. Açıklama olarak tag'leri birleştiriyoruz,
            // URL'yi de mock oluşturuyoruz (DB'de NOT NULL olduğu için boş bırakmıyoruz).
            var items = payload?.Contents?
                .Where(i => (i.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                .Skip((page - 1) * size)
                .Take(size)
                .Select(i => new NormalizedContent(
                    Provider: Name,
                    ExternalId: i.Id,
                    Type: i.Type?.Equals("video", StringComparison.OrdinalIgnoreCase) == true ? ContentType.Video : ContentType.Text,
                    Title: i.Title ?? "",
                    Description: i.Tags != null ? string.Join(", ", i.Tags) : null,
                    Url: $"mock://provider1/{i.Id}",
                    Views: i.Metrics?.Views,
                    Likes: i.Metrics?.Likes,
                    Reactions: null,                 // JSON örneğinde yok
                    ReadingTime: null,               // JSON örneğinde yok
                    PublishedAt: i.PublishedAt
                ))
                .ToList() ?? new List<NormalizedContent>();

            return items;
        }

        // JSON şeması
        private record JsonPayload(List<JsonItem> Contents, JsonPagination Pagination);
        private record JsonItem(
            string Id,
            string? Title,
            string? Type,
            JsonMetrics? Metrics,
            DateTime PublishedAt,
            List<string>? Tags
        );
        private record JsonMetrics(int? Views, int? Likes, string? Duration);
        private record JsonPagination(int Total, int Page, int Per_Page);
    }
}
