using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;                       // EF.Functions, LINQ to Entities
using Microsoft.Extensions.Caching.Distributed;           // IDistributedCache
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;    // MySqlMatchSearchMode
using SearchEngineService.Data;
using SearchEngineService.Models;
using SearchEngineService.Providers;
using SearchEngineService.Services;
using System.Text.Json;

namespace SearchEngineService.Controllers
{
    [ApiController]                                        // Otomatik model doğrulama, 400 üretimi vs.
    [Route("api/[controller]")]                            // /api/search
    public class SearchController : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(


            //buraya mı eklicez

            [FromQuery] string query,                      // Aranan metin
            [FromQuery] string? type = "all",              // video | text | all
            [FromQuery] string sort = "popularity",        // popularity | relevance
            [FromQuery] int page = 1,
            [FromQuery] int size = 20,
            [FromServices] IEnumerable<IProviderClient> providers = default!, // Tüm provider adaptörleri
            [FromServices] IIngestService ingest = default!,                  // Upsert + skor
            [FromServices] AppDbContext db = default!,                        // EF Core DbContext
            [FromServices] IDistributedCache cache = default!,                // << Redis cache
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
            if (!allowedTypes.Contains((type ?? "all").Trim().ToLowerInvariant()))
                return BadRequestValidation("Invalid type", ("type", "Geçerli değerler: all, video, text."));

            var allowedSort = new[] { "popularity", "relevance" };
            if (!allowedSort.Contains((sort ?? "popularity").Trim().ToLowerInvariant()))
                return BadRequestValidation("Invalid sort", ("sort", "Geçerli değerler: popularity, relevance."));

            if (size < 1 || size > 50)
                return BadRequestValidation("Invalid size", ("size", "1 ile 50 arasında olmalı."));



            // ---- Parametreleri normalize et (sınır koy) ----
            page = Math.Max(1, page);
            size = Math.Clamp(size, 1, 50);

            // ---- 1) CACHE: Aynı kombinasyon için 60 sn sakla ----
            // Anahtarı stabilleştirmek için küçük harfe ve trim'e çekelim:
            var normQuery = (query ?? "").Trim().ToLowerInvariant();
            var normType = (type ?? "all").Trim().ToLowerInvariant();
            var normSort = (sort ?? "popularity").Trim().ToLowerInvariant();

            var cacheKey = $"search:{normQuery}:{normType}:{normSort}:{page}:{size}";
            var cachedJson = await cache.GetStringAsync(cacheKey, ct);
            if (!string.IsNullOrWhiteSpace(cachedJson))
            {
                // Log: Cache hit
                Serilog.Log.Information("CACHE_HIT {Key}", cacheKey);

                // JSON'u tekrar serialize/deserialize etmeyelim; direkt döndür.
                return Content(cachedJson, "application/json");
            }
            Serilog.Log.Information("CACHE_MISS {Key}", cacheKey);

            // ---- 2) Provider'lardan veri getir → normalize → ingest (upsert + skor) ----
            // Not: Cache miss'te ağır işi bir kez yapıyoruz.
            var tasks = providers.Select(p => p.SearchAsync(normQuery, page, size, ct));
            var batches = await Task.WhenAll(tasks);
            var flat = batches.SelectMany(x => x).ToList();
            if (flat.Count > 0)
            {
                await ingest.IngestAsync(flat, ct);        // DB'ye yaz ve skorla
            }

            // ---- 3) DB üzerinde filtre + sıralama + sayfalama ----
            var q = db.Contents.AsNoTracking()
                               .Include(c => c.Score)
                               .AsQueryable();

            // Tür filtresi (all değilse)
            if (!string.Equals(normType, "all", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(normType))
            {
                var t = normType.Equals("video", StringComparison.OrdinalIgnoreCase)
                        ? ContentType.Video
                        : ContentType.Text;
                q = q.Where(c => c.Type == t);
            }

            // 1) Her iki modda da, query varsa FULLTEXT filtre uygula:
            if (!string.IsNullOrWhiteSpace(normQuery))
            {
                q = q.Where(c => EF.Functions.Match(
                                    new[] { c.Title!, c.Description! },
                                    normQuery,
                                    MySqlMatchSearchMode.NaturalLanguage) > 0);
            }

            // 2) Sadece SIRALAMA değişsin:
            if (normSort.Equals("relevance", StringComparison.OrdinalIgnoreCase))
            {
                q = q.OrderByDescending(c => EF.Functions.Match(
                                    new[] { c.Title!, c.Description! },
                                    normQuery,
                                    MySqlMatchSearchMode.NaturalLanguage));
            }
            else
            {
                q = q.OrderByDescending(c => c.Score.FinalPopularityScore);
            }


            var total = await q.CountAsync(ct);            // toplam kayıt (sayfalama için)
            var items = await q.Skip((page - 1) * size)    // paginasyon
                               .Take(size)
                               .Select(c => new            // Projeksiyon: istemcinin görmek istediği alanlar
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

            // ---- 4) CACHE'e yaz: 60 saniye sakla ----
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) // TTL
            };
            var json = JsonSerializer.Serialize(responseObj); // string olarak saklıyoruz
            await cache.SetStringAsync(cacheKey, json, cacheOptions, ct);

            // Yanıtı döndür
            return Ok(responseObj);
        }
    }
}
