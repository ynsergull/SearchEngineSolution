using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace SearchEngineService.Tests;

public class SearchControllerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public SearchControllerTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Get_Should_Return_400_When_Query_Empty()
    {
        var res = await _client.GetAsync("/api/Search?query=&type=all&sort=popularity&page=1&size=20");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await res.Content.ReadFromJsonAsync<ProblemDetailsLike>();
        problem!.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Get_Should_Return_200_With_Results()
    {
        var res = await _client.GetAsync("/api/Search?query=go&type=all&sort=popularity&page=1&size=20");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<SearchResponseLike>();
        body!.results.Should().NotBeNull();
    }

    // küçük test-DTO'ları
    private record ProblemDetailsLike(string? Title, string? Detail);
    private record SearchResponseLike(Meta meta, List<object> results);
    private record Meta(int page, int size, int total);
}
