using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Entities;
using System.IO;

namespace ChessXiv.UnitTests;

public class PgnImportServiceTests
{
    [Fact]
    public async Task ImportAsync_ReturnsZero_WhenPgnIsEmpty()
    {
        var parser = new FakePgnParser();
        var repository = new FakeGameRepository();
        var positionCoordinator = new FakePositionImportCoordinator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

        using var reader = new StringReader(string.Empty);
        var result = await service.ImportAsync(reader);

        Assert.Equal(0, result.ParsedCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, parser.CallCount);
        Assert.Equal(0, repository.CallCount);
        Assert.Equal(0, positionCoordinator.CallCount);
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
        var positionCoordinator = new FakePositionImportCoordinator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

        using var reader = new StringReader("[Event \"X\"]\n\n*");
        var result = await service.ImportAsync(reader);

        Assert.Equal(0, result.ParsedCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, parser.CallCount);
        Assert.Equal(0, repository.CallCount);
        Assert.Equal(0, positionCoordinator.CallCount);
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
        var positionCoordinator = new FakePositionImportCoordinator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

        using var reader = new StringReader("pgn-content");
        var result = await service.ImportAsync(reader);

        Assert.Equal(3, result.ParsedCount);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(1, positionCoordinator.CallCount);
        Assert.Equal(1, unitOfWork.CallCount);
        Assert.Equal(2, repository.LastSavedGames.Count);
        Assert.Equal(2, positionCoordinator.LastGames.Count);
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
        var positionCoordinator = new FakePositionImportCoordinator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        using var reader = new StringReader("pgn-content");
        await service.ImportAsync(reader, cancellationToken: token);

        Assert.Equal(token, positionCoordinator.LastToken);
        Assert.Equal(token, repository.LastToken);
        Assert.Equal(token, unitOfWork.LastToken);
    }

    [Fact]
    public async Task ImportAsync_MarksGamesAsMaster_WhenFlagIsTrue()
    {
        var parser = new FakePgnParser
        {
            GamesToReturn = [CreateGame("Alpha", "Beta")]
        };
        var repository = new FakeGameRepository();
        var positionCoordinator = new FakePositionImportCoordinator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

        using var reader = new StringReader("pgn-content");
        var result = await service.ImportAsync(reader, markAsMaster: true);

        Assert.Equal(1, result.ImportedCount);
        Assert.All(repository.LastSavedGames, game => Assert.True(game.IsMaster));
    }

    [Fact]
    public async Task ImportAsync_FromReader_ImportsInStreamMode_AndMarksMaster()
    {
        var parser = new FakePgnParser
        {
            StreamGamesToReturn = [
                CreateGame("Alpha", "Beta"),
                CreateGame("", "Invalid")
            ]
        };
        var repository = new FakeGameRepository();
        var positionCoordinator = new FakePositionImportCoordinator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

        using var reader = new StringReader("stream-content");
        var result = await service.ImportAsync(reader, markAsMaster: true, batchSize: 10);

        Assert.Equal(2, result.ParsedCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(repository.LastSavedGames);
        Assert.True(repository.LastSavedGames[0].IsMaster);
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
        public IReadOnlyCollection<Game> StreamGamesToReturn { get; set; } = [];
        public int CallCount { get; private set; }

        public IReadOnlyCollection<Game> ParsePgn(string pgn)
        {
            CallCount++;
            return GamesToReturn;
        }

        public async IAsyncEnumerable<Game> ParsePgnAsync(TextReader reader, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            var streamGames = StreamGamesToReturn.Count > 0 ? StreamGamesToReturn : GamesToReturn;
            foreach (var game in streamGames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return game;
                await Task.Yield();
            }
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

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeUnitOfWorkTransaction());
        }

        public void ClearTracker()
        {
        }
    }

    private sealed class FakeUnitOfWorkTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePositionImportCoordinator : IPositionImportCoordinator
    {
        public int CallCount { get; private set; }
        public List<Game> LastGames { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task PopulateAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastToken = cancellationToken;
            LastGames.Clear();
            LastGames.AddRange(games);
            return Task.CompletedTask;
        }
    }
}
