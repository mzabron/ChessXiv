using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class DraftImportRepository(ChessXivDbContext dbContext) : IDraftImportRepository
{
    public async Task ClearStagingGamesAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        await dbContext.StagingGames
            .Where(g => g.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddStagingGamesAsync(IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        var dbConnection = dbContext.Database.GetDbConnection();
        if (dbConnection is not NpgsqlConnection connection)
        {
            await dbContext.StagingGames.AddRangeAsync(games, cancellationToken);
            return;
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await CopyStagingGamesAsync(connection, games, cancellationToken);
        await CopyStagingMovesAsync(connection, games, cancellationToken);
        await CopyStagingPositionsAsync(connection, games, cancellationToken);
    }

    private static async Task CopyStagingGamesAsync(NpgsqlConnection connection, IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken)
    {
        const string sql = """
            COPY "StagingGames" (
                "Id",
                "OwnerUserId",
                "CreatedAtUtc",
                "Date",
                "Year",
                "Round",
                "WhiteTitle",
                "BlackTitle",
                "WhiteElo",
                "BlackElo",
                "Event",
                "Site",
                "TimeControl",
                "ECO",
                "Opening",
                "White",
                "Black",
                "WhiteNormalizedFullName",
                "WhiteNormalizedFirstName",
                "WhiteNormalizedLastName",
                "BlackNormalizedFullName",
                "BlackNormalizedFirstName",
                "BlackNormalizedLastName",
                "Result",
                "Pgn",
                "MoveCount",
                "GameHash"
            ) FROM STDIN (FORMAT BINARY)
            """;

        await using var importer = await connection.BeginBinaryImportAsync(sql, cancellationToken);
        foreach (var game in games)
        {
            await importer.StartRowAsync(cancellationToken);
            importer.Write(game.Id, NpgsqlDbType.Uuid);
            importer.Write(game.OwnerUserId, NpgsqlDbType.Text);
            importer.Write(game.CreatedAtUtc, NpgsqlDbType.TimestampTz);

            if (game.Date.HasValue)
            {
                var utcDate = game.Date.Value.Kind == DateTimeKind.Utc
                    ? game.Date.Value
                    : DateTime.SpecifyKind(game.Date.Value, DateTimeKind.Utc);
                importer.Write(utcDate, NpgsqlDbType.TimestampTz);
            }
            else
            {
                importer.WriteNull();
            }

            importer.Write(game.Year, NpgsqlDbType.Integer);
            WriteNullableText(importer, game.Round);
            WriteNullableText(importer, game.WhiteTitle);
            WriteNullableText(importer, game.BlackTitle);
            WriteNullableInt(importer, game.WhiteElo);
            WriteNullableInt(importer, game.BlackElo);
            WriteNullableText(importer, game.Event);
            WriteNullableText(importer, game.Site);
            WriteNullableText(importer, game.TimeControl);
            WriteNullableText(importer, game.ECO);
            WriteNullableText(importer, game.Opening);
            importer.Write(game.White, NpgsqlDbType.Text);
            importer.Write(game.Black, NpgsqlDbType.Text);
            importer.Write(game.WhiteNormalizedFullName, NpgsqlDbType.Text);
            WriteNullableText(importer, game.WhiteNormalizedFirstName);
            WriteNullableText(importer, game.WhiteNormalizedLastName);
            importer.Write(game.BlackNormalizedFullName, NpgsqlDbType.Text);
            WriteNullableText(importer, game.BlackNormalizedFirstName);
            WriteNullableText(importer, game.BlackNormalizedLastName);
            importer.Write(game.Result, NpgsqlDbType.Text);
            importer.Write(game.Pgn, NpgsqlDbType.Text);
            importer.Write(game.MoveCount, NpgsqlDbType.Integer);
            importer.Write(game.GameHash, NpgsqlDbType.Text);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task CopyStagingMovesAsync(NpgsqlConnection connection, IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken)
    {
        const string sql = """
            COPY "StagingMoves" (
                "Id",
                "StagingGameId",
                "MoveNumber",
                "WhiteMove",
                "BlackMove",
                "WhiteClk",
                "BlackClk"
            ) FROM STDIN (FORMAT BINARY)
            """;

        await using var importer = await connection.BeginBinaryImportAsync(sql, cancellationToken);
        foreach (var game in games)
        {
            foreach (var move in game.Moves)
            {
                await importer.StartRowAsync(cancellationToken);
                importer.Write(move.Id, NpgsqlDbType.Uuid);
                importer.Write(move.StagingGameId, NpgsqlDbType.Uuid);
                importer.Write(move.MoveNumber, NpgsqlDbType.Integer);
                importer.Write(move.WhiteMove, NpgsqlDbType.Text);
                WriteNullableText(importer, move.BlackMove);
                WriteNullableText(importer, move.WhiteClk);
                WriteNullableText(importer, move.BlackClk);
            }
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task CopyStagingPositionsAsync(NpgsqlConnection connection, IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken)
    {
        const string sql = """
            COPY "StagingPositions" (
                "Id",
                "StagingGameId",
                "Fen",
                "FenHash",
                "PlyCount",
                "LastMove",
                "SideToMove"
            ) FROM STDIN (FORMAT BINARY)
            """;

        await using var importer = await connection.BeginBinaryImportAsync(sql, cancellationToken);
        foreach (var game in games)
        {
            foreach (var position in game.Positions)
            {
                await importer.StartRowAsync(cancellationToken);
                importer.Write(position.Id, NpgsqlDbType.Uuid);
                importer.Write(position.StagingGameId, NpgsqlDbType.Uuid);
                importer.Write(position.Fen, NpgsqlDbType.Text);
                importer.Write(position.FenHash, NpgsqlDbType.Bigint);
                importer.Write(position.PlyCount, NpgsqlDbType.Integer);
                WriteNullableText(importer, position.LastMove);
                importer.Write(position.SideToMove, NpgsqlDbType.Char);
            }
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static void WriteNullableText(NpgsqlBinaryImporter importer, string? value)
    {
        if (value is null)
        {
            importer.WriteNull();
            return;
        }

        importer.Write(value, NpgsqlDbType.Text);
    }

    private static void WriteNullableInt(NpgsqlBinaryImporter importer, int? value)
    {
        if (!value.HasValue)
        {
            importer.WriteNull();
            return;
        }

        importer.Write(value.Value, NpgsqlDbType.Integer);
    }

}
