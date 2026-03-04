using ChessBase.Application.Abstractions;
using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Application.Services;
using ChessBase.Domain.Entities;

namespace ChessBase.UnitTests;

public class PgnImportServiceTests
{
    [Fact]
    public async Task ImportAsync_ReturnsZero_WhenPgnIsEmpty()
    {
        var parser = new FakePgnParser();
        var repository = new FakeGameRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, unitOfWork);

        var result = await service.ImportAsync(string.Empty);

        Assert.Equal(0, result.ParsedCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, parser.CallCount);
        Assert.Equal(0, repository.CallCount);
        Assert.Equal(0, unitOfWork.CallCount);
    }

    [Fact]
    public async Task ImportAsync_ReturnsZero_WhenParserReturnsNoGames()
    {
        var parser = new FakePgnParser
        {
            GamesToReturn = []
        };
        var repository = new FakeGameRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, unitOfWork);

        var result = await service.ImportAsync("[Event \"X\"]\n\n*");

        Assert.Equal(0, result.ParsedCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, parser.CallCount);
        Assert.Equal(0, repository.CallCount);
        Assert.Equal(0, unitOfWork.CallCount);
    }

    [Fact]
    public async Task ImportAsync_ImportsOnlyValidGames_AndCommitsOnce()
    {
        var validGameOne = CreateGame("Carlsen", "Nakamura");
        var invalidGame = CreateGame("", "Kasparov");
        var validGameTwo = CreateGame("Polgar", "Anand");

        var parser = new FakePgnParser
        {
            GamesToReturn = [validGameOne, invalidGame, validGameTwo]
        };
        var repository = new FakeGameRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, unitOfWork);

        var result = await service.ImportAsync("pgn-content");

        Assert.Equal(3, result.ParsedCount);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(1, unitOfWork.CallCount);
        Assert.Equal(2, repository.LastSavedGames.Count);
        Assert.DoesNotContain(repository.LastSavedGames, game => string.IsNullOrWhiteSpace(game.White));
    }

    [Fact]
    public async Task ImportAsync_ForwardsCancellationToken_ToRepositoryAndUnitOfWork()
    {
        var parser = new FakePgnParser
        {
            GamesToReturn = [CreateGame("Alpha", "Beta")]
        };
        var repository = new FakeGameRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, unitOfWork);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await service.ImportAsync("pgn-content", token);

        Assert.Equal(token, repository.LastToken);
        Assert.Equal(token, unitOfWork.LastToken);
    }

    private static Game CreateGame(string white, string black)
    {
        return new Game
        {
            Id = Guid.NewGuid(),
            White = white,
            Black = black,
            Result = "*",
            Pgn = "dummy",
            Moves =
            [
                new Move
                {
                    Id = Guid.NewGuid(),
                    MoveNumber = 1,
                    WhiteMove = "e4"
                }
            ]
        };
    }

    private sealed class FakePgnParser : IPgnParser
    {
        public IReadOnlyCollection<Game> GamesToReturn { get; set; } = [];
        public int CallCount { get; private set; }

        public IReadOnlyCollection<Game> ParsePgn(string pgn)
        {
            CallCount++;
            return GamesToReturn;
        }
    }

    private sealed class FakeGameRepository : IGameRepository
    {
        public int CallCount { get; private set; }
        public List<Game> LastSavedGames { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task AddRangeAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastToken = cancellationToken;
            LastSavedGames.Clear();
            LastSavedGames.AddRange(games);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CallCount { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastToken = cancellationToken;
            return Task.FromResult(1);
        }
    }
}
