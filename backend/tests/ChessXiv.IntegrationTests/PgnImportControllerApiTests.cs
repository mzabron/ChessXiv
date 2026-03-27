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

public class PgnImportControllerApiTests
{
    [Fact]
    public async Task Import_ReturnsBadRequest_WhenPgnIsEmpty()
    {
        using var factory = new TestWebApplicationFactory(new SuccessImportService());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/pgn/import", new PgnImportRequest(string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_ReturnsOk_WithImportResult_WhenRequestIsValid()
    {
        var fakeService = new SuccessImportService
        {
            Result = new PgnImportResult(ParsedCount: 3, ImportedCount: 2, SkippedCount: 1)
        };

        using var factory = new TestWebApplicationFactory(fakeService);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/pgn/import", new PgnImportRequest("1. e4 e5 1-0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PgnImportResult>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload.ParsedCount);
        Assert.Equal(2, payload.ImportedCount);
        Assert.Equal(1, payload.SkippedCount);
    }

    [Fact]
    public async Task Import_ReturnsProblemDetails500_WhenUnhandledExceptionOccurs()
    {
        using var factory = new TestWebApplicationFactory(new ThrowingImportService());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/pgn/import", new PgnImportRequest("1. d4 d5 *"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal(500, payload.Status);
        Assert.Equal("Internal Server Error", payload.Title);
    }

    private sealed class TestWebApplicationFactory(IPgnImportService importService) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPgnImportService>();
                services.AddSingleton(importService);
                services.RemoveAll<IDraftImportService>();
                services.RemoveAll<IDraftPromotionService>();
                services.AddSingleton<IDraftImportService>(new NoopDraftImportService());
                services.AddSingleton<IDraftPromotionService>(new NoopDraftPromotionService());
            });
        }
    }

    private sealed class SuccessImportService : IPgnImportService
    {
        public PgnImportResult Result { get; set; } = new(ParsedCount: 1, ImportedCount: 1, SkippedCount: 0);

        public Task<PgnImportResult> ImportAsync(TextReader reader, bool markAsMaster = false, int batchSize = 500, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingImportService : IPgnImportService
    {
        public Task<PgnImportResult> ImportAsync(TextReader reader, bool markAsMaster = false, int batchSize = 500, CancellationToken cancellationToken = default)
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
