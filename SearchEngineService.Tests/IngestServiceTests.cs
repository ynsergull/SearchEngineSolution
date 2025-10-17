using Microsoft.EntityFrameworkCore;
using SearchEngineService.Data;
using SearchEngineService.Services;
using FluentAssertions;
using CT = SearchEngineService.Models.ContentType;
using TContent = SearchEngineService.Transport.NormalizedContent;

namespace SearchEngineService.Tests;

public class IngestServiceTests
{
    [Fact]
    public async Task Ingest_Should_Upsert_And_Score()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(opts);
        var scorer = new ScoringService();
        var ingest = new IngestService(db, scorer);

        // 1) İlk ingest: Kayıt oluştur
        await ingest.IngestAsync(new[]
        {
            new TContent("P1","x1", CT.Video, "Go Concurrency", "demo",
                "mock://p1/x1", 1000, 50, 0, null, DateTime.UtcNow.AddDays(-1))
        }, CancellationToken.None);

        // 2) İkinci ingest: Aynı anahtar ile güncelle
        await ingest.IngestAsync(new[]
        {
            new TContent("P1","x1", CT.Video, "Go Concurrency (updated)", "demo",
                "mock://p1/x1", 1200, 80, 0, null, DateTime.UtcNow.AddDays(-1))
        }, CancellationToken.None);

        var contents = await db.Contents.Include(c => c.Score).ToListAsync();
        contents.Should().HaveCount(1);
        contents[0].Title.Should().Contain("updated");
        contents[0].Score!.FinalPopularityScore.Should().BeGreaterThan(0);
    }
}
