# ChessXiv Backend Documentation

Describes the backend as implemented under `backend/`.

Companion schema image: `docs/assets/database-schema.png` (predates the position-storage
rework; the current shape is described in section 10).

## 1. Topology

Solution: `backend/ChessXiv.sln`

| Project | Responsibility |
|---|---|
| `ChessXiv.Api` | HTTP transport, identity/auth, middleware, SignalR hub, background import worker |
| `ChessXiv.Application` | Use-case orchestration: import, promotion, explorer, replay, position rebuild |
| `ChessXiv.Domain` | Chess engine primitives and persistence entities |
| `ChessXiv.Infrastructure` | EF Core DbContext, repositories, bulk COPY writers |
| `ChessXiv.Cli` | Batch importer and maintenance tool, bypasses the HTTP API and its limits |
| `ChessXiv.UnitTests` / `ChessXiv.IntegrationTests` | Tests; integration tests use Testcontainers PostgreSQL |

## 2. Identity model: accounts and guests

There are three kinds of caller.

1. **Anonymous, no token.** Can read public databases and their games.
2. **Guest.** `POST /api/auth/guest-session` issues a throwaway JWT whose subject is
   `guest:<guid>`, carrying the `chessxiv:guest` claim and valid for 12 hours. A guest can
   upload a PGN, browse it, filter it and use the opening tree.
3. **Registered user.** A normal account token.

Endpoints that write durable data require the `RegisteredUser` authorization policy, which
admits an authenticated caller *without* the guest claim. Everything else that needs an
identity accepts either. This is what lets a guest do everything except save.

The frontend keeps the guest token in `sessionStorage`, so closing the tab makes the guest's
draft unreachable immediately.

### 2.1 Password policy

Minimum length 8; requires digit, uppercase and lowercase; non-alphanumeric not required;
unique email required.

### 2.2 Rate limits

| Policy | Limit | Applied to |
|---|---|---|
| `AuthLogin` | 5 / minute / IP | `POST /api/auth/login` |
| `AuthForgotPassword` | 5 / 5 minutes / IP | forgot-password, resend-confirmation, change-pending-email |
| `GuestSession` | 10 / 10 minutes / IP | `POST /api/auth/guest-session` |

## 3. Limits

There are no user tiers and no per-tier quotas. Two limits exist, both in
`ChessXivLimits`:

- **Upload:** 100 MB per PGN. Matches Cloudflare's body limit, so a larger upload cannot
  reach the origin anyway. Enforced by `RequestSizeLimit` and pre-checked in the browser.
- **Saved games:** 10 000 distinct games per account, across all of their databases.
  Checked before a save writes anything; the rejection is a 409 carrying
  `code: "SAVED_GAMES_LIMIT"` plus the numbers, so the UI can say exactly how much room is
  left.

The CLI importer does not go through the HTTP API and is not subject to either.

## 4. API surface

### 4.1 Auth — `/api/auth`

`POST /register`, `POST /login`, `POST /guest-session`, `POST /confirm-email`,
`POST /resend-confirmation`, `POST /change-pending-email`, `POST /forgot-password`,
`POST /reset-password`.

Account-enumeration-sensitive endpoints always return a generic success message.

### 4.2 Account — `/api/account` (RegisteredUser)

`GET /summary` returns nickname, email, `savedGamesUsed`, `savedGamesLimit` and
`maxUploadBytes`. Also `change-email`, `confirm-email-change` (anonymous),
`change-password`, `delete`.

### 4.3 PGN import — `/api/pgn`

| Endpoint | Auth | Behaviour |
|---|---|---|
| `POST /drafts/import-file` | any token, incl. guest | Buffers the upload and queues a background draft import |
| `POST /import-to-database-file` | RegisteredUser | Queues a background import straight into a database |
| `POST /drafts/promote` | RegisteredUser | Moves the whole draft into a database |
| `GET /drafts/import-progress` | any token | Last cached progress, or 204 |
| `GET /drafts/games` | any token | Paginated, filterable draft listing |
| `GET /drafts/games/{id}` | any token | Replay payload for one draft game |
| `DELETE /drafts` | any token | Clears the caller's draft |

Both upload endpoints return `202 Accepted`; progress arrives over SignalR.

### 4.4 Explorer — `/api/games/explorer`

- `POST /position/move` — applies a SAN or coordinate move; used by the board.
- `POST /move-tree` — next-move aggregate for a position. See section 9.

### 4.5 User databases — `/api/user-databases`

| Endpoint | Auth | Behaviour |
|---|---|---|
| `GET /` | anonymous ok | **All public databases, plus the caller's own private ones.** Each row carries `isOwner` and `isBookmarked` |
| `GET /{id}`, `GET /{id}/games`, `GET /{id}/games/{gameId}` | anonymous ok | Public, or owner-only for private |
| `POST /`, `PUT /{id}`, `DELETE /{id}` | RegisteredUser | Create / rename+visibility / delete |
| `POST /{id}/bookmark`, `DELETE /{id}/bookmark` | RegisteredUser | Idempotent |
| `POST /{id}/games` | RegisteredUser | Link existing games by id |
| `POST /{id}/games/from-selection` | RegisteredUser | Add a filtered result set or an explicit selection; see section 8.3 |
| `DELETE /{id}/games/{gameId}` | RegisteredUser | Unlink one game |

The single `GET /` listing is deliberate: signing in must never *remove* a database from
view, which is what the previous split of `/mine` + `/bookmarks` + `/public` did.

## 5. Import pipeline

All import paths share one shape: stream-parse the PGN, finalise derived fields, replay each
game into positions, then bulk-write.

### 5.1 Parsing

`PgnService` streams game blocks from a `TextReader` and yields `ParsedGame` — a `Game`
entity plus a `ParsedMove` list. The moves are parse output only; they are never stored,
because the PGN itself already records them.

### 5.2 Derived fields

`ParsedGameFinalizer` fills year, move count, normalised player names and the dedup hash.
This lives in one place; it used to be copy-pasted into every import service and the CLI.

### 5.3 Position generation

`PositionImportCoordinator` replays each game and produces one `Position` per ply plus the
starting position. Replaying is CPU-bound and games are independent, so batches of 32 or
more fan out across cores.

### 5.4 Persistence

Every path writes through PostgreSQL binary `COPY` (`PostgresBulkCopy`). EF change tracking
issues one parameterised INSERT per row, which at millions of position rows dominates import
time.

`DirectDatabaseImportService` commits per batch rather than wrapping the whole file in one
transaction: a single transaction over a 100 MB PGN holds locks for minutes, blocks
autovacuum, and loses everything on any failure.

### 5.5 Draft import and promotion

Draft import writes to the staging tables, keyed by the caller's token subject (a user id or
a `guest:` id), and always clears the caller's previous draft first. Promotion is one
set-based SQL block with `ON CONFLICT DO NOTHING`, so it is idempotent, followed by a
`GameCount` resync.

### 5.6 Background worker

Uploads are buffered to a temp file and queued. `BackgroundImportWorker` runs at most
**2 imports concurrently**; each streams up to 100 MB and holds a batch of parsed games in
memory, so unbounded fan-out could exhaust RAM. `ANALYZE` runs on the touched tables
afterwards, and in-flight imports are awaited on shutdown.

## 6. Storage model

Tables: `Games`, `Positions`, `StagingGames`, `StagingPositions`, `StagingDraftSessions`,
`UserDatabases`, `UserDatabaseGames`, `UserDatabaseBookmarks`, plus ASP.NET Identity.

### 6.1 Positions

```
Positions
  GameId    uuid      ─┐ PK
  PlyCount  smallint  ─┘
  PosKey    bytea(16)     index, INCLUDE (NextMove, Result)
  NextMove  text          SAN played FROM this position, null at end of game
  Result    smallint      denormalised game result
```

Three decisions drive this shape.

**`PosKey` instead of a FEN string.** A 128-bit Zobrist key over pieces, side to move,
castling rights and the en-passant square. It deliberately excludes the halfmove clock and
move number, so a position reached by a different move order matches. At database scale a
collision is not a practical concern; the second 64 bits exist because dropping the FEN
string also dropped the tiebreak that used to back up a 64-bit hash.

The en-passant square only counts when a capture is actually available — see section 7.1.

**`NextMove` instead of `LastMove`.** Storing the move played *from* a position rather than
the one that led *to* it means a position's continuations sit in one index range. The
opening tree no longer self-joins `Positions` to itself on `ply + 1`.

**`Result` denormalised.** Lets the unfiltered tree aggregate wins, draws and losses without
touching `Games` at all.

Together with dropping the `Moves` table (redundant with the stored PGN), this cut the
storage for a 70 000-game database from roughly 2.4 GB to roughly 850 MB.

### 6.2 What is recomputed instead of stored

`GameReplayBuilder` rebuilds a game's move list and per-ply FEN history from its PGN when a
user opens that one game. This costs well under a millisecond and removed both the `Moves`
table and the per-position FEN column.

### 6.3 Other indexes

`Game`: `GameHash`, `(Year, Id)`, `MoveCount`, normalised first/last name pairs.
`UserDatabase`: unique `(OwnerUserId, Name)`, `IsPublic`.
`UserDatabaseGame`: composite PK `(UserDatabaseId, GameId)`.
`UserDatabase.GameCount` is denormalised — counting a multi-million-row link table on every
panel render was too slow — and is kept in step with every link insert, delete and promotion.

## 7. Filtering

`GameFilteringExtensions` applies the same filters to `Game`, `StagingGame` and
`UserDatabaseGame` queries: Elo (One/Both/Avg), year range, ECO prefix, exact result, move
count range, normalised player names with optional colour-agnostic matching, and position.

### 7.1 En-passant and position identity

`EnPassantRules` records an en-passant target only when an enemy pawn actually stands beside
the pawn that double-pushed. The bare FEN grammar permits recording it after every double
push, but that is wrong for a database: the square is part of the position's identity, so a
target nobody can capture on splits one position into two keys depending on the move order.

Concretely, 1.e4 e5 2.Nf3 Nc6 and 1.Nf3 Nc6 2.e4 e5 reach

```
r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3
r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3
```

— identical but for the halfmove clock, which is not part of a position. Both must therefore
carry the same key, and do.

The same rule is applied when reading a FEN, so a hand-written or third-party FEN carrying a
stale target resolves to the same key as the position produced by replaying a game.

### 7.2 Position search modes

| Mode | Matches |
|---|---|
| `SamePosition` (default) | `PosKey` — the position however it was reached, so transpositions are found |
| `ExactPly` | `PosKey` **and** `PlyCount` — the position at the same point in the game |

`ExactPly` is a ply filter, not a full-FEN comparison: the halfmove clock is not part of a
position and is ignored. The ply is derived from the searched FEN's fullmove number and side
to move, and `PlyCount` is already in the position primary key, so the mode costs nothing.

## 8. Explorer

### 8.1 Move tree

For a position key, the query reads that key's index range, groups by `(NextMove, Result)`
and counts. Verified against PostgreSQL as an `Index Only Scan` on `IX_Positions_PosKey`:
20 000 matching rows aggregate in about 3 ms. The total number of games in the position falls
out of the same rows, so it needs no separate `DISTINCT` count.

With filters active, the position rows join to the filtered link set; without filters there is
no join at all.

Sources are `UserDatabase` (access-checked) and `StagingSession` (the caller's own draft).
Percentages are rounded to two decimals in the service layer.

### 8.2 Position play

`PositionPlayService` accepts SAN directly, or a coordinate move which it resolves by
generating SAN candidates and verifying the resulting board transition. Promotion defaults to
queen when a pawn reaches the last rank.

### 8.3 Adding a filtered selection

`POST /api/user-databases/{id}/games/from-selection` takes a source (the caller's draft, or
another database they can read), an optional explicit game-id list, and the same filter set
the games list uses. Without explicit ids it adds **every game matching the filters**, not
just the visible page — the server resolves the set. Games already linked are skipped rather
than duplicated, and the saved-games limit is checked before anything is written.

## 9. Background and real-time services

- **`UnconfirmedUserCleanupService`** — hourly; deletes unconfirmed accounts older than 24 h.
- **`StagingDraftCleanupService`** — every 15 minutes; deletes drafts idle for more than
  2 hours. Idle, not age-based: every read of a draft touches `StagingDraftSessions`, so a
  draft never disappears while someone is still browsing it. Drafts with no session row are
  swept on creation age as a backstop.
- **Import progress over SignalR** — `ImportProgressHub` at `/hubs/import-progress`, with a
  per-user connection registry and a last-update cache so a reconnecting client can recover
  state via `GET /api/pgn/drafts/import-progress`.

## 10. CLI

`backend/src/ChessXiv.Cli`. Reads the connection string from `CHESSXIV_CONNECTION_STRING`,
authenticates a user, then either creates or picks one of their databases and imports into it
through the same `IDirectDatabaseImportService` the web upload uses — so it gets binary COPY
and per-batch commits, and there is one import path to keep correct.

```bash
export CHESSXIV_CONNECTION_STRING="Host=...;Database=...;Username=...;Password=..."
dotnet run --project src/ChessXiv.Cli -- /path/to/games.pgn
```

Maintenance mode regenerates `Positions` from the PGNs already in `Games`:

```bash
dotnet run --project src/ChessXiv.Cli -- --rebuild-positions
```

This is required once after the `CompactPositionStorage` migration, which drops position rows
it cannot convert in place, and is useful afterwards as a repair tool.

## 11. Configuration

`ConnectionStrings:DefaultConnection`; `Jwt:{Issuer,Audience,SigningKey,ExpirationMinutes}`
(startup fails without a signing key); `Frontend:BaseUrl` for confirmation links;
`Cors:AllowedOrigins` (startup fails if empty); `Brevo:{ApiKey,SenderEmail,SenderName}` —
without credentials, emails are logged rather than sent.

## 12. Operational notes

- nginx must allow a 100 MB body (`client_max_body_size 100M;`) to match the app and Cloudflare.
- Run `dotnet ef database update` after deploying schema changes.
- After a large import, `Positions` benefits from a `VACUUM ANALYZE` so the opening tree's
  index-only scans avoid heap fetches.
