using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SearchEngineService.Data;
using SearchEngineService.Providers;

namespace SearchEngineService.Tests;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // DbContext -> InMemory
            var dbDesc = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDesc != null) services.Remove(dbDesc);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("test-db"));

            // Redis -> Memory
            services.RemoveAll(typeof(IDistributedCache));
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();

            // Provider’lar -> Sadece Fake
            services.RemoveAll<IProviderClient>();
            services.AddSingleton<IProviderClient>(
                new FakeProviderClient("FakeProvider", FakeProviderClient.Seed())
            );
        });
    }
}
