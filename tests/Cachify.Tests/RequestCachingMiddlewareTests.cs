using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cachify.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cachify.Tests;

public sealed class RequestCachingMiddlewareTests
{
    [Fact]
    public async Task GetRequestsAreCachedByDefault()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var first = await client.GetAsync("/data");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayload = await first.Content.ReadFromJsonAsync<TestPayload>();
        first.Headers.GetValues("X-Cachify-Cache").Single().Should().Be("MISS");

        var second = await client.GetAsync("/data");
        var secondPayload = await second.Content.ReadFromJsonAsync<TestPayload>();
        second.Headers.GetValues("X-Cachify-Cache").Single().Should().Be("HIT");

        secondPayload.Should().BeEquivalentTo(firstPayload);
    }

    [Fact]
    public async Task PostRequestsAreNotCachedByDefault()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var first = await client.PostAsync("/data", new StringContent("alpha", Encoding.UTF8, "text/plain"));
        var second = await client.PostAsync("/data", new StringContent("alpha", Encoding.UTF8, "text/plain"));

        var firstText = await first.Content.ReadAsStringAsync();
        var secondText = await second.Content.ReadAsStringAsync();

        firstText.Should().NotBe(secondText);
    }

    [Fact]
    public async Task PostRequestsCanBeCachedWhenEnabled()
    {
        using var server = CreateServer(options =>
        {
            options.CacheableMethods.Add("POST");
            options.KeyOptions.IncludeBody = true;
        });

        using var client = server.CreateClient();

        var first = await client.PostAsync("/data", new StringContent("alpha", Encoding.UTF8, "text/plain"));
        var second = await client.PostAsync("/data", new StringContent("alpha", Encoding.UTF8, "text/plain"));

        var firstText = await first.Content.ReadAsStringAsync();
        var secondText = await second.Content.ReadAsStringAsync();

        second.Headers.GetValues("X-Cachify-Cache").Single().Should().Be("HIT");
        secondText.Should().Be(firstText);
    }

    private static TestServer CreateServer(Action<RequestCacheOptions>? configure = null)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddCachify(options =>
                {
                    options.UseMemory();
                });
                services.AddRequestCaching(configure);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRequestCaching();
                app.UseEndpoints(endpoints =>
                {
                    var counter = 0;
                    endpoints.MapGet("/data", async context =>
                    {
                        var value = Interlocked.Increment(ref counter);
                        await context.Response.WriteAsJsonAsync(new TestPayload(value));
                    });

                    endpoints.MapPost("/data", async context =>
                    {
                        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                        var value = Interlocked.Increment(ref counter);
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync($"{value}:{body}");
                    });
                });
            });

        return new TestServer(builder);
    }

    private sealed record TestPayload(int Value);
}
