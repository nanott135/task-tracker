using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Data;

namespace TaskTracker.Api.Tests;

public class ApiWebApplicationFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        // Host filtering (AllowedHosts) is out of scope for these tests and
        // would otherwise reject the non-localhost Host header HstsTests
        // needs, since HstsMiddleware exempts localhost/loopback hosts.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection([new KeyValuePair<string, string?>("AllowedHosts", "*")]));
        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<TaskDbContext>)
                    || d.ServiceType == typeof(DbContextOptions)
                    || d.ServiceType == typeof(IDbContextOptionsConfiguration<TaskDbContext>))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TaskDbContext>(options =>
                options.UseInMemoryDatabase($"TaskTrackerTestDb-{Guid.NewGuid()}"));
        });
    }
}
