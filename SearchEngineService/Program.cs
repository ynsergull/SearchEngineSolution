using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Polly;
using Polly.Extensions.Http;
using SearchEngineService.Data;
using SearchEngineService.Providers;
using SearchEngineService.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ----------------- Rate Limiting -----------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (ctx, ct) =>
    {
        var retry = "60";
        ctx.HttpContext.Response.Headers.RetryAfter = retry;
        ctx.HttpContext.Response.ContentType = "application/problem+json";

        Serilog.Log.Warning("RATE_LIMIT_REJECTED {Path} {IP}",
            ctx.HttpContext.Request.Path,
            ctx.HttpContext.Connection.RemoteIpAddress?.ToString());

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = $"İstek limiti aşıldı. {retry} saniye sonra tekrar deneyin.",
            Type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4"
        };

        await ctx.HttpContext.Response.WriteAsJsonAsync(problem, ct);
    };

    // IP başına 60 istek / 1 dk
    options.AddPolicy("search", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});
// -------------------------------------------------

// Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IScoringService, ScoringService>();
builder.Services.AddScoped<IIngestService, IngestService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<IProviderClient, JsonProviderClient>();
    builder.Services.AddSingleton<IProviderClient, XmlProviderClient>();
}

// IContentSearch ortam bazlı
var cs = builder.Configuration.GetConnectionString("Default");
if (builder.Environment.IsEnvironment("Testing"))
{
    // InMemory EF + LINQ arama (EF.Functions.Match YOK)
    builder.Services.AddScoped<IContentSearch, FallbackContentSearch>();
}
else
{
    // MySQL FULLTEXT arama
    builder.Services.AddScoped<IContentSearch, MySqlContentSearch>();
}
// ⬆️ ⬆️ ⬆️

// EF Core + MySQL (Testing’de CustomWebAppFactory zaten InMemory ile override ediyor)
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseMySql(cs, ServerVersion.AutoDetect(cs)));
}

// Redis
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = builder.Configuration.GetSection("Redis")["Configuration"];
});

// HttpClient + Polly
var retry = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(r => (int)r.StatusCode == 429)
    .WaitAndRetryAsync(new[]
    {
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000)
    });
builder.Services.AddHttpClient("provider").AddPolicyHandler(retry);

var app = builder.Build();

// Rate limiter middleware
app.UseRateLimiter();

// Health (DB + Redis) ve rate limit muaf
app.MapGet("/health", async (AppDbContext db, IDistributedCache cache, CancellationToken ct) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken: ct);
    const string key = "health:ping";
    await cache.SetStringAsync(key, "pong", new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
    }, ct);
    var val = await cache.GetStringAsync(key, ct);
    return Results.Ok(new
    {
        status = "ok",
        mysql = "ok",
        redis = val == "pong" ? "ok" : "fail",
        time = DateTime.UtcNow
    });
}).DisableRateLimiting();

// Global exception handler (500 -> ProblemDetails)
app.UseExceptionHandler(err =>
{
    err.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        Serilog.Log.Error(ex, "UNHANDLED_EXCEPTION");

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Unexpected error",
            Detail = app.Environment.IsDevelopment() ? ex?.ToString() : "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc7807"
        };
        await context.Response.WriteAsJsonAsync(problem);
    });
});

// Pipeline
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Testing’de HTTPS yönlendirmesini kapat (xUnit uyarısını susturur)
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

// Sadece controller endpoint'lerine "search" policy uygula
app.MapControllers().RequireRateLimiting("search");

app.Run();

public partial class Program { }
