using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;                       // AsNoTracking, Include, CountAsync, ToListAsync
using Microsoft.Extensions.Caching.Distributed;           // IDistributedCache
using SearchEngineService.Data;
using SearchEngineService.Models;
using SearchEngineService.Providers;
using SearchEngineService.Services;                        // ⬅ IContentSearch burada
using System.Text.Json;

namespace SearchEngineService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] string query,
            [FromQuery] string? type = "all",
            [FromQuery] string sort = "popularity",
            [FromQuery] int page = 1,
            [FromQuery] int size = 20,
            [FromServices] IEnumerable<IProviderClient> providers = default!,
            [FromServices] IIngestService ingest = default!,
            [FromServices] AppDbContext db = default!,
            [FromServices] IDistributedCache cache = default!,
            [FromServices] IContentSearch contentSearch = default!,          // ⬅️ YENİ: Arama stratejisi (MySql/Fallback)
            CancellationToken ct = default)
        {
            // ---- validation ----
            static IActionResult BadRequestValidation(string title, params (string key, string msg)[] errors)
            {
                var dict = errors.GroupBy(e => e.key)
                                 .ToDictionary(g => g.Key, g => g.Select(x => x.msg).ToArray());
                var vpd = new ValidationProblemDetails(dict)
                {
                    Title = title,
                    Status = StatusCodes.Status400BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7807"
                };
                return new BadRequestObjectResult(vpd);
            }

            if (string.IsNullOrWhiteSpace(query))
                return BadRequestValidation("Invalid query", ("query", "Query boş olamaz."));

            var allowedTypes = new[] { "all", "video", "text" };
            var normTypeInput = (type ?? "all").Trim().ToLowerInvariant();
            if (!allowedTypes.Contains(normTypeInput))
                return BadRequestValidation("Invalid type", ("type", "Geçerli değerler: all, video, text."));

            var allowedSort = new[] { "popularity", "relevance" };
            var normSortInput = (sort ?? "popularity").Trim().ToLowerInvariant();
            if (!allowedSort.Contains(normSortInput))
                return BadRequestValidation("Invalid sort", ("sort", "Geçerli değerler: popularity, relevance."));

            if (size < 1 || size > 50)
                return BadRequestValidation("Invalid size", ("size", "1 ile 50 arasında olmalı."));

            // ---- normalize ----
            page = Math.Max(1, page);
            size = Math.Clamp(size, 1, 50);

            var normQuery = (query ?? "").Trim().ToLowerInvariant();
            var normType = normTypeInput;      // "all" | "video" | "text"
            var normSort = normSortInput;      // "popularity" | "relevance"

            // ---- 1) CACHE ----
            var cacheKey = $"search:{normQuery}:{normType}:{normSort}:{page}:{size}";
            var cachedJson = await cache.GetStringAsync(cacheKey, ct);
            if (!string.IsNullOrWhiteSpace(cachedJson))
            {
                Serilog.Log.Information("CACHE_HIT {Key}", cacheKey);
                return Content(cachedJson, "application/json");
            }
            Serilog.Log.Information("CACHE_MISS {Key}", cacheKey);

            // ---- 2) Provider'lardan getir → ingest ----
            var tasks = providers.Select(p => p.SearchAsync(normQuery, page, size, ct));
            var batches = await Task.WhenAll(tasks);
            var flat = batches.SelectMany(x => x).ToList();
            if (flat.Count > 0)
                await ingest.IngestAsync(flat, ct);

            // ---- 3) DB üzerinde filtre + sıralama + sayfalama ----
            //    ⬇️ Arama stratejisini TEK SATIRLA uygula
            IQueryable<Content> q = db.Contents
                                       .AsNoTracking()
                                       .Include(c => c.Score);

            q = contentSearch.Apply(db, q, normQuery, normType, normSort);

            var total = await q.CountAsync(ct);
            var items = await q.Skip((page - 1) * size)
                               .Take(size)
                               .Select(c => new
                               {
                                   id = c.Id,
                                   title = c.Title,
                                   type = c.Type.ToString().ToLower(),
                                   score = c.Score.FinalPopularityScore,
                                   score_breakdown = new
                                   {
                                       baseScore = c.Score.BaseScore,
                                       typeWeight = c.Score.TypeWeight,
                                       recency = c.Score.RecencyScore,
                                       engagement = c.Score.EngagementScore
                                   },
                                   provider = c.Provider,
                                   published_at = c.PublishedAt,
                                   url = c.Url
                               })
                               .ToListAsync(ct);

            var responseObj = new { meta = new { page, size, total }, results = items };

            // ---- 4) CACHE set (60s) ----
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
            };
            var json = JsonSerializer.Serialize(responseObj);
            await cache.SetStringAsync(cacheKey, json, cacheOptions, ct);

            return Ok(responseObj);
        }
    }
}
