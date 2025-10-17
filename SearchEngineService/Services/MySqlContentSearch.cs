using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using SearchEngineService.Data;
using SearchEngineService.Models;
using System.Linq;

namespace SearchEngineService.Services;

// MySQL/Pomelo: FULLTEXT (MATCH ... AGAINST ...)
public sealed class MySqlContentSearch : IContentSearch
{
    public IQueryable<Content> Apply(
         AppDbContext db,
         IQueryable<Content> source,
         string query,
         string? type,
         string sort)
    {
        // tür filtresi
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            var t = type.Equals("video", StringComparison.OrdinalIgnoreCase) ? ContentType.Video : ContentType.Text;
            source = source.Where(c => c.Type == t);
        }

        // FULLTEXT match (NaturalLanguage)
        double? matchScore = null;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                EF.Functions.Match(new[] { c.Title!, c.Description! }, query, MySqlMatchSearchMode.NaturalLanguage) > 0);

            // EF, aynı ifadeyi iki kere yazmamak için değişkende tutmayı desteklemez;
            // o yüzden OrderBy içinde ifadenin kendisini tekrar yazıyoruz.
        }

        // sıralama
        var s = (sort ?? "popularity").ToLowerInvariant();
        if (s == "relevance" && !string.IsNullOrWhiteSpace(query))
        {
            source = source
                .OrderByDescending(c => EF.Functions.Match(new[] { c.Title!, c.Description! }, query, MySqlMatchSearchMode.NaturalLanguage))
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
