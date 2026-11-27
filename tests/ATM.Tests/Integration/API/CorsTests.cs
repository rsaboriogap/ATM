using System.Net;
using System.Net.Http.Json;
using ATM.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class CorsTests
{
    private const string AllowedOrigin =
        "https://localhost:7101";

    private const string DisallowedOrigin =
        "https://untrusted.example.com";

    private static HttpClient CreateClient(
        AtmApiFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    [Fact]
    public async Task GetAccounts_AllowedOrigin_ReturnsCorsHeader()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/accounts");

        request.Headers.Add(
            "Origin",
            AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Origin",
            out var origins));

        Assert.Contains(AllowedOrigin, origins!);
    }

    [Fact]
    public async Task GetAccounts_DisallowedOrigin_OmitsCorsHeader()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/accounts");

        request.Headers.Add(
            "Origin",
            DisallowedOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.False(response.Headers.Contains(
            "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_AllowedOrigin_ReturnsCorsHeaders()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/transfers");

        request.Headers.Add(
            "Origin",
            AllowedOrigin);

        request.Headers.Add(
            "Access-Control-Request-Method",
            "POST");

        request.Headers.Add(
            "Access-Control-Request-Headers",
            "content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Origin",
            out var origins));

        Assert.Contains(AllowedOrigin, origins!);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Methods",
            out var methods));

        Assert.Contains(
            "POST",
            string.Join(",", methods!));
    }

    [Fact]
    public async Task Preflight_AllowsIdempotencyKeyHeader()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/transfers");

        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "content-type,idempotency-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Headers",
            out var headers));

        Assert.Contains(
            "idempotency-key",
            string.Join(",", headers!),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preflight_DisallowedOrigin_OmitsCorsHeader()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/transfers");

        request.Headers.Add(
            "Origin",
            DisallowedOrigin);

        request.Headers.Add(
            "Access-Control-Request-Method",
            "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains(
            "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ErrorResponse_AllowedOrigin_PreservesCorsHeader()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await client
            .GetFromJsonAsync<AccountDto[]>("/api/accounts");

        Assert.NotNull(accounts);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/accounts/{accounts[0].Id}/withdrawals");

        request.Headers.Add(
            "Origin",
            AllowedOrigin);

        request.Headers.Add(
            IdempotencyTestExtensions.IdempotencyKeyHeader,
            Guid.NewGuid().ToString());

        request.Content = JsonContent.Create(
            new { Amount = 1500m });

        var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Origin",
            out var origins));

        Assert.Contains(AllowedOrigin, origins!);
    }
}