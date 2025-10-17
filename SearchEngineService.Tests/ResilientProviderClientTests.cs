using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using SearchEngineService.Models;
using SearchEngineService.Providers;
using SearchEngineService.Transport;

namespace SearchEngineService.Tests;

public class ResilientProviderClientTests
{
    [Fact]
    public async Task SearchAsync_Retries_OnTransientFailure()
    {
        var sample = new NormalizedContent(
            Provider: "Flaky",
            ExternalId: "1",
            Type: ContentType.Video,
            Title: "demo",
            Description: null,
            Url: "mock://flaky/1",
            Views: 100,
            Likes: 10,
            Reactions: null,
            ReadingTime: null,
            PublishedAt: DateTime.UtcNow);

        var attempt = 0;
        var inner = new DelegateProvider("Flaky", async (query, page, size, ct) =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new IOException("transient");
            }

            return new List<NormalizedContent> { sample };
        });

        var options = Options.Create(new ProviderResilienceOptions
        {
            Default = new ProviderPolicyOptions
            {
                MaxConcurrentRequests = 1,
                RetryCount = 2,
                RetryBaseDelayMilliseconds = 1,
                CircuitBreakerFailures = 5,
                CircuitBreakerDurationSeconds = 10
            }
        });

        var client = new ResilientProviderClient(inner, options, NullLogger<ResilientProviderClient>.Instance);

        var result = await client.SearchAsync("demo", 1, 10, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(sample);
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_OpensCircuit_AfterConfiguredFailures()
    {
        var inner = new DelegateProvider("Flaky", (query, page, size, ct) => throw new InvalidOperationException("boom"));

        var options = Options.Create(new ProviderResilienceOptions
        {
            Default = new ProviderPolicyOptions
            {
                MaxConcurrentRequests = 1,
                RetryCount = 0,
                RetryBaseDelayMilliseconds = 1,
                CircuitBreakerFailures = 1,
                CircuitBreakerDurationSeconds = 60
            }
        });

        var client = new ResilientProviderClient(inner, options, NullLogger<ResilientProviderClient>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchAsync("demo", 1, 10, CancellationToken.None));

        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.SearchAsync("demo", 1, 10, CancellationToken.None));
    }

    private sealed class DelegateProvider : IProviderClient
    {
        private readonly Func<string, int, int, CancellationToken, Task<IReadOnlyList<NormalizedContent>>> _handler;

        public DelegateProvider(string name, Func<string, int, int, CancellationToken, Task<IReadOnlyList<NormalizedContent>>> handler)
        {
            Name = name;
            _handler = handler;
        }

        public string Name { get; }

        public Task<IReadOnlyList<NormalizedContent>> SearchAsync(string query, int page, int size, CancellationToken ct)
            => _handler(query, page, size, ct);
    }
}
