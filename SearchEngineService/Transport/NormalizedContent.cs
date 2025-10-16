using SearchEngineService.Models;

namespace SearchEngineService.Transport
{
    public record NormalizedContent(
        string Provider, string ExternalId, ContentType Type,
        string Title, string? Description, string Url,
        int? Views, int? Likes, int? Reactions, int? ReadingTime,
        DateTime PublishedAt
    );
}
