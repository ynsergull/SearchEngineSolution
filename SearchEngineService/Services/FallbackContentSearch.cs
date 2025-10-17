using System.Linq;
using SearchEngineService.Data;
using SearchEngineService.Models;

namespace SearchEngineService.Services;

// Tek sınıf: MySQL varsa FULLTEXT, yoksa Contains
public sealed class FallbackContentSearch : IContentSearch
{
    public IQueryable<Content> Apply(
       AppDbContext db,
       IQueryable<Content> source,
       string query,
       string? type,
       string sort)
    {
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            var t = type.Equals("video", StringComparison.OrdinalIgnoreCase) ? ContentType.Video : ContentType.Text;
            source = source.Where(c => c.Type == t);
        }

        // çok basit bir “relevance” tahmini: Title/Description'da geçiyorsa 1, geçmiyorsa 0
        var q = (query ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            source = source.Where(c =>
                (c.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (c.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var s = (sort ?? "popularity").ToLowerInvariant();
        if (s == "relevance" && !string.IsNullOrWhiteSpace(q))
        {
            source = source
                .OrderByDescending(c =>
                    ((c.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0) +
                    ((c.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                .ThenByDescending(c => c.Score.FinalPopularityScore)  // TIE-BREAKER #1
                .ThenByDescending(c => c.PublishedAt);               // TIE-BREAKER #2
        }
        else
        {
            source = source
                .OrderByDescending(c => c.Score.FinalPopularityScore)
                .ThenByDescending(c => c.PublishedAt);
        }

        return source;
    }
}
