using ChessXiv.Domain.Entities;
using Npgsql;
using NpgsqlTypes;

namespace ChessXiv.Infrastructure.Repositories;

/// <summary>
/// Binary COPY writers for the import hot path.
/// </summary>
/// <remarks>
/// Importing through EF change tracking issues one parameterised INSERT per row; at a few
/// million position rows per PGN that is the single biggest cost of an import. PostgreSQL's
/// binary COPY protocol writes the same rows in bulk and is an order of magnitude faster,
/// so every import path goes through here.
/// </remarks>
internal static class PostgresBulkCopy
{
    /// <summary>Columns shared by Games and StagingGames, in the order the writers emit them.</summary>
    private const string SharedGameColumns = """
        "Round", "WhiteTitle", "BlackTitle", "WhiteElo", "BlackElo",
        "Event", "Site", "TimeControl", "ECO", "Opening", "White", "Black",
        "WhiteNormalizedFullName", "WhiteNormalizedFirstName", "WhiteNormalizedLastName",
        "BlackNormalizedFullName", "BlackNormalizedFirstName", "BlackNormalizedLastName",
        "Result", "Pgn", "MoveCount", "GameHash"
        """;

    public static async Task WriteGamesAsync(
        NpgsqlConnection connection,
        IEnumerable<Game> games,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            $"""COPY "Games" ("Id", "Date", "Year", {SharedGameColumns}) FROM STDIN (FORMAT BINARY)""",
            cancellationToken);

        foreach (var game in games)
        {
            await importer.StartRowAsync(cancellationToken);
            importer.Write(game.Id, NpgsqlDbType.Uuid);
            WriteNullableUtc(importer, game.Date);
            importer.Write(game.Year, NpgsqlDbType.Integer);
            WriteCommonGameColumns(
                importer,
                game.Round, game.WhiteTitle, game.BlackTitle, game.WhiteElo, game.BlackElo,
                game.Event, game.Site, game.TimeControl, game.ECO, game.Opening,
                game.White, game.Black,
                game.WhiteNormalizedFullName, game.WhiteNormalizedFirstName, game.WhiteNormalizedLastName,
                game.BlackNormalizedFullName, game.BlackNormalizedFirstName, game.BlackNormalizedLastName,
                game.Result, game.Pgn, game.MoveCount, game.GameHash);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    public static async Task WriteStagingGamesAsync(
        NpgsqlConnection connection,
        IEnumerable<StagingGame> games,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            $"""
            COPY "StagingGames" ("Id", "OwnerUserId", "CreatedAtUtc", "Date", "Year", {SharedGameColumns})
            FROM STDIN (FORMAT BINARY)
            """,
            cancellationToken);

        foreach (var game in games)
        {
            await importer.StartRowAsync(cancellationToken);
            importer.Write(game.Id, NpgsqlDbType.Uuid);
            importer.Write(game.OwnerUserId, NpgsqlDbType.Text);
            importer.Write(game.CreatedAtUtc, NpgsqlDbType.TimestampTz);
            WriteNullableUtc(importer, game.Date);
            importer.Write(game.Year, NpgsqlDbType.Integer);
            WriteCommonGameColumns(
                importer,
                game.Round, game.WhiteTitle, game.BlackTitle, game.WhiteElo, game.BlackElo,
                game.Event, game.Site, game.TimeControl, game.ECO, game.Opening,
                game.White, game.Black,
                game.WhiteNormalizedFullName, game.WhiteNormalizedFirstName, game.WhiteNormalizedLastName,
                game.BlackNormalizedFullName, game.BlackNormalizedFirstName, game.BlackNormalizedLastName,
                game.Result, game.Pgn, game.MoveCount, game.GameHash);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    public static async Task WritePositionsAsync(
        NpgsqlConnection connection,
        IEnumerable<Game> games,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            """COPY "Positions" ("GameId", "PlyCount", "PosKey", "NextMove", "Result") FROM STDIN (FORMAT BINARY)""",
            cancellationToken);

        foreach (var game in games)
        {
            foreach (var position in game.Positions)
            {
                await importer.StartRowAsync(cancellationToken);
                importer.Write(position.GameId, NpgsqlDbType.Uuid);
                importer.Write(position.PlyCount, NpgsqlDbType.Smallint);
                importer.Write(position.PosKey, NpgsqlDbType.Bytea);
                WriteNullableText(importer, position.NextMove);
                importer.Write((byte)position.Result, NpgsqlDbType.Smallint);
            }
        }

        await importer.CompleteAsync(cancellationToken);
    }

    public static async Task WriteStagingPositionsAsync(
        NpgsqlConnection connection,
        IEnumerable<StagingGame> games,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            """COPY "StagingPositions" ("StagingGameId", "PlyCount", "PosKey", "NextMove", "Result") FROM STDIN (FORMAT BINARY)""",
            cancellationToken);

        foreach (var game in games)
        {
            foreach (var position in game.Positions)
            {
                await importer.StartRowAsync(cancellationToken);
                importer.Write(position.StagingGameId, NpgsqlDbType.Uuid);
                importer.Write(position.PlyCount, NpgsqlDbType.Smallint);
                importer.Write(position.PosKey, NpgsqlDbType.Bytea);
                WriteNullableText(importer, position.NextMove);
                importer.Write((byte)position.Result, NpgsqlDbType.Smallint);
            }
        }

        await importer.CompleteAsync(cancellationToken);
    }

    public static async Task WriteUserDatabaseGamesAsync(
        NpgsqlConnection connection,
        IEnumerable<UserDatabaseGame> links,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            """
            COPY "UserDatabaseGames" ("UserDatabaseId", "GameId", "AddedAtUtc", "Date", "Year", "Event", "Round", "Site")
            FROM STDIN (FORMAT BINARY)
            """,
            cancellationToken);

        foreach (var link in links)
        {
            await importer.StartRowAsync(cancellationToken);
            importer.Write(link.UserDatabaseId, NpgsqlDbType.Uuid);
            importer.Write(link.GameId, NpgsqlDbType.Uuid);
            importer.Write(link.AddedAtUtc, NpgsqlDbType.TimestampTz);
            WriteNullableUtc(importer, link.Date);
            WriteNullableInt(importer, link.Year);
            WriteNullableText(importer, link.Event);
            WriteNullableText(importer, link.Round);
            WriteNullableText(importer, link.Site);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static void WriteCommonGameColumns(
        NpgsqlBinaryImporter importer,
        string? round, string? whiteTitle, string? blackTitle, int? whiteElo, int? blackElo,
        string? eventName, string? site, string? timeControl, string? eco, string? opening,
        string white, string black,
        string whiteNormalizedFullName, string? whiteNormalizedFirstName, string? whiteNormalizedLastName,
        string blackNormalizedFullName, string? blackNormalizedFirstName, string? blackNormalizedLastName,
        string result, string pgn, int moveCount, string gameHash)
    {
        WriteNullableText(importer, round);
        WriteNullableText(importer, whiteTitle);
        WriteNullableText(importer, blackTitle);
        WriteNullableInt(importer, whiteElo);
        WriteNullableInt(importer, blackElo);
        WriteNullableText(importer, eventName);
        WriteNullableText(importer, site);
        WriteNullableText(importer, timeControl);
        WriteNullableText(importer, eco);
        WriteNullableText(importer, opening);
        importer.Write(white, NpgsqlDbType.Text);
        importer.Write(black, NpgsqlDbType.Text);
        importer.Write(whiteNormalizedFullName, NpgsqlDbType.Text);
        WriteNullableText(importer, whiteNormalizedFirstName);
        WriteNullableText(importer, whiteNormalizedLastName);
        importer.Write(blackNormalizedFullName, NpgsqlDbType.Text);
        WriteNullableText(importer, blackNormalizedFirstName);
        WriteNullableText(importer, blackNormalizedLastName);
        importer.Write(result, NpgsqlDbType.Text);
        importer.Write(pgn, NpgsqlDbType.Text);
        importer.Write(moveCount, NpgsqlDbType.Integer);
        importer.Write(gameHash, NpgsqlDbType.Text);
    }

    private static void WriteNullableUtc(NpgsqlBinaryImporter importer, DateTime? value)
    {
        if (!value.HasValue)
        {
            importer.WriteNull();
            return;
        }

        var utc = value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

        importer.Write(utc, NpgsqlDbType.TimestampTz);
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
