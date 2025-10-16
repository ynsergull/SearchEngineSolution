using Microsoft.EntityFrameworkCore;
using SearchEngineService.Models;

namespace SearchEngineService.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Content> Contents => Set<Content>();
        public DbSet<ContentScore> ContentScores => Set<ContentScore>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Content>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
                e.HasIndex(x => x.Type);
                e.Property(x => x.Title).HasMaxLength(512);
                e.Property(x => x.Description).HasColumnType("TEXT");

                e.HasOne(x => x.Score)
                 .WithOne(s => s.Content)
                 .HasForeignKey<ContentScore>(s => s.ContentId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<ContentScore>(e =>
            {
                e.HasKey(s => s.ContentId);
            });
        }
    }
}
