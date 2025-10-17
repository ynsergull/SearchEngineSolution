using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using SearchEngineService.Transport;

namespace SearchEngineService.Providers;

public sealed class ResilientProviderClient : IProviderClient, IDisposable
{
    private static readonly Func<Exception, bool> ExceptionFilter = ex => ex is not OperationCanceledException;

    private readonly IProviderClient _inner;
    private readonly AsyncPolicy<IReadOnlyList<NormalizedContent>> _pipeline;
    private readonly SemaphoreSlim _rateLimiter;
    private readonly ILogger<ResilientProviderClient> _logger;

    public ResilientProviderClient(
        IProviderClient inner,
        IOptions<ProviderResilienceOptions> options,
        ILogger<ResilientProviderClient> logger)
    {
        _inner = inner;
        _logger = logger;
        var resolved = options.Value.Resolve(inner.Name);

        _rateLimiter = new SemaphoreSlim(resolved.MaxConcurrentRequests);

        var retryPolicy = BuildRetryPolicy(resolved);
        var breakerPolicy = BuildCircuitBreakerPolicy(resolved);

        _pipeline = Policy.WrapAsync(retryPolicy, breakerPolicy);
    }

    public string Name => _inner.Name;

    public async Task<IReadOnlyList<NormalizedContent>> SearchAsync(string query, int page, int size, CancellationToken ct)
    {
        return await _pipeline.ExecuteAsync(async token =>
        {
            await _rateLimiter.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await _inner.SearchAsync(query, page, size, token).ConfigureAwait(false);
            }
            finally
            {
                _rateLimiter.Release();
            }
        }, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private AsyncPolicy<IReadOnlyList<NormalizedContent>> BuildRetryPolicy(ProviderPolicyOptions options)
    {
        if (options.RetryCount <= 0)
        {
            return Policy.NoOpAsync<IReadOnlyList<NormalizedContent>>();
        }

        return Policy<IReadOnlyList<NormalizedContent>>
            .Handle(ExceptionFilter)
            .WaitAndRetryAsync(options.RetryCount, attempt => options.GetRetryDelay(attempt),
                (exception, _, attempt, _) =>
                {
                    _logger.LogWarning(exception, "Provider {Provider} transient failure on attempt {Attempt}", Name, attempt);
                });
    }

    private AsyncCircuitBreakerPolicy<IReadOnlyList<NormalizedContent>> BuildCircuitBreakerPolicy(ProviderPolicyOptions options)
    {
        return Policy<IReadOnlyList<NormalizedContent>>
            .Handle(ExceptionFilter)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: options.CircuitBreakerFailures,
                durationOfBreak: options.CircuitBreakerDuration,
                onBreak: (exception, breakDuration) =>
                {
                    _logger.LogWarning(exception,
                        "Provider {Provider} circuit opened for {Duration} after repeated failures",
                        Name,
                        breakDuration);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Provider {Provider} circuit reset", Name);
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Provider {Provider} circuit half-open", Name);
                });
    }
}
