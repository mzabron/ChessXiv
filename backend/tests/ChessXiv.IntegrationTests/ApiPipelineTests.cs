using System.Net;
using System.Net.Http.Json;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChessXiv.IntegrationTests;

public class ApiPipelineTests
{
    [Fact]
    public async Task AnonymousBulkImportEndpoint_IsNoLongerExposed()
    {
        // It wrote Games rows with no UserDatabase link, so nothing could read or delete
        // them, and it accepted unauthenticated writes.
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/pgn/import", new { pgn = "1. e4 e5 1-0" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GuestSession_IssuesTokenToAnonymousCaller()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/guest-session", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.AccessToken));
        Assert.True(payload.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task GuestToken_IsRejectedByEndpointsThatWriteDurableData()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var session = await client.PostAsync("/api/auth/guest-session", content: null);
        var token = (await session.Content.ReadFromJsonAsync<AuthTokenResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/user-databases",
            new CreateUserDatabaseRequest("Guest attempt", false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetails500()
    {
        using var factory = new TestWebApplicationFactory(new ThrowingPositionPlayService());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/games/explorer/position/move",
            new PositionMoveRequest { Fen = "startpos", San = "e4" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal(500, payload!.Status);
        Assert.Equal("Internal Server Error", payload.Title);
    }

    private sealed class TestWebApplicationFactory(IPositionPlayService? positionPlayService = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                if (positionPlayService is not null)
                {
                    services.RemoveAll<IPositionPlayService>();
                    services.AddSingleton(positionPlayService);
                }

                services.RemoveAll<IDraftImportService>();
                services.RemoveAll<IDraftPromotionService>();
                services.AddSingleton<IDraftImportService>(new NoopDraftImportService());
                services.AddSingleton<IDraftPromotionService>(new NoopDraftPromotionService());
            });
        }
    }

    private sealed class ThrowingPositionPlayService : IPositionPlayService
    {
        public PositionMoveResponse TryApplyMove(PositionMoveRequest request)
        {
            throw new InvalidOperationException("Simulated failure");
        }
    }

    private sealed class NoopDraftImportService : IDraftImportService
    {
        public Task<DraftImportResult> ImportAsync(
            TextReader reader,
            string ownerUserId,
            int batchSize = 500,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DraftImportResult(0, 0, 0));
        }
    }

    private sealed class NoopDraftPromotionService : IDraftPromotionService
    {
        public Task<DraftPromotionResult> PromoteAsync(
            string ownerUserId,
            Guid userDatabaseId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DraftPromotionResult(0, 0));
        }
    }
}
