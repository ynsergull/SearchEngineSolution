using System;
using System.ComponentModel.DataAnnotations;

namespace SearchEngineService.Providers;

public sealed class ProviderResilienceOptions
{
    [Required]
    public ProviderPolicyOptions Default { get; set; } = new();

    public Dictionary<string, ProviderPolicyOptions> Providers { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public ProviderPolicyOptions Resolve(string providerName)
    {
        if (!string.IsNullOrWhiteSpace(providerName) && Providers.TryGetValue(providerName, out var specific))
        {
            return specific;
        }

        return Default;
    }
}

public sealed class ProviderPolicyOptions
{
    [Range(1, 64)]
    public int MaxConcurrentRequests { get; set; } = 2;

    [Range(0, 10)]
    public int RetryCount { get; set; } = 2;

    [Range(1, 10_000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    [Range(1, 20)]
    public int CircuitBreakerFailures { get; set; } = 3;

    [Range(1, 600)]
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    public TimeSpan CircuitBreakerDuration => TimeSpan.FromSeconds(CircuitBreakerDurationSeconds);

    public TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(RetryBaseDelayMilliseconds * Math.Pow(2, Math.Max(0, attempt - 1)));
}
