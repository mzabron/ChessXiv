# ChessXiv Backend Documentation (Current Architecture)

This document describes the backend as implemented in the current codebase under backend/.

Companion schema image: docs/database-schema.png

## 1. Scope

This document covers:

1. Solution and runtime architecture
2. Startup/bootstrap behavior (DI, middleware, auth, CORS, rate limiting)
3. API endpoint behavior with practical semantics
4. Import pipeline internals (direct import, staging import, promotion)
5. Explorer and move-tree internals
6. Security and account lifecycle
7. Database model and mapping behavior
8. Background jobs and real-time progress updates
9. CLI import path
10. Testing strategy and known coverage map
11. Operational defaults and limits

## 2. Backend Topology

Solution: backend/ChessXiv.sln

Projects:

1. backend/src/ChessXiv.Api
2. backend/src/ChessXiv.Application
3. backend/src/ChessXiv.Domain
4. backend/src/ChessXiv.Infrastructure
5. backend/src/ChessXiv.Cli
6. backend/tests/ChessXiv.UnitTests
7. backend/tests/ChessXiv.IntegrationTests

Layer boundaries:

- ChessXiv.Api: HTTP transport, identity/auth endpoints, exception handling, middleware, SignalR hub.
- ChessXiv.Application: use-case orchestration and business workflow (import, promotion, explorer logic, position play).
- ChessXiv.Domain: chess engine primitives and entity types used by persistence.
- ChessXiv.Infrastructure: EF Core DbContext, repository implementations, quota service, unit of work.
- ChessXiv.Cli: non-HTTP batch importer for marking games as master data.



## 3. Runtime Bootstrap and Request Pipeline

Primary source: backend/src/ChessXiv.Api/Program.cs

### 3.1 Startup sequence

At startup, the API does the following:

1. Registers controllers, problem details, memory cache.
2. Binds Frontend and Brevo option sections.
3. Builds CORS policy from Cors:AllowedOrigins and fails startup if none provided.
4. Adds rate limiter policies for auth endpoints.
5. Binds Jwt options and fails startup if Jwt:SigningKey is missing.
6. Configures PostgreSQL DbContext.
7. Configures IdentityCore with password policy and unique email.
8. Configures JWT Bearer auth and SignalR query-string token extraction.
9. Registers authorization and SignalR.
10. Registers all service and repository dependencies.
11. Registers hosted cleanup services.
12. Builds middleware pipeline and maps controllers + hub.

### 3.2 Middleware order

Current order:

1. UseExceptionHandler
2. UseRateLimiter
3. UseAuthentication
4. UseCors("Frontend")
5. UseAuthorization
6. MapControllers
7. MapHub<ImportProgressHub>

### 3.3 Global exception behavior

The global handler distinguishes:

- BadHttpRequestException: returns the original HTTP status (including 413 Payload Too Large) with ProblemDetails body.
- Any other exception: logs and returns 500 ProblemDetails with generic message.

## 4. Dependency Injection Map

Registered key services (scoped unless noted):

- Parsing/import:
  - IPgnParser -> PgnService
  - IPgnImportService -> PgnImportService
  - IDraftImportService -> DraftImportService
  - IDirectDatabaseImportService -> DirectDatabaseImportService
  - IDraftPromotionService -> DraftPromotionService

- Explorer/position:
  - IGameExplorerService -> GameExplorerService
  - IPositionPlayService -> PositionPlayService

- Repositories:
  - IGameRepository -> GameRepository
  - IGameExplorerRepository -> GameExplorerRepository
  - IDraftImportRepository -> DraftImportRepository
  - IDraftPromotionRepository -> DraftPromotionRepository
  - IUserDatabaseGameRepository -> UserDatabaseGameRepository

- Chess engine:
  - IBoardStateSerializer -> FenBoardStateSerializer
  - IBoardStateFactory -> BoardStateFactory
  - IBoardStateTransition -> BitboardBoardStateTransition
  - IPositionHasher -> ZobristPositionHasher
  - IPositionImportCoordinator -> PositionImportCoordinator

- Infra/system:
  - IUnitOfWork -> EfUnitOfWork
  - IQuotaService -> UserQuotaService
  - IJwtTokenService -> JwtTokenService

- Email:
  - BrevoEmailSender via HttpClient
  - LoggingEmailSender
  - IEmailSender chooses Brevo when ApiKey + SenderEmail are configured, otherwise logging sender

- Real-time progress:
  - DraftImportProgressCache (singleton)
  - ImportProgressConnectionRegistry (singleton)
  - IDraftImportProgressPublisher -> SignalRDraftImportProgressPublisher
  - IUserIdProvider -> SubOrNameIdentifierUserIdProvider (singleton)

## 5. Configuration Reference

Primary config source: backend/src/ChessXiv.Api/appsettings.Example.json

### 5.1 Connection

- ConnectionStrings:DefaultConnection (PostgreSQL)

### 5.2 JWT

- Jwt:Issuer default ChessXiv.Api
- Jwt:Audience default ChessXiv.Web
- Jwt:SigningKey required
- Jwt:ExpirationMinutes default 60

### 5.3 Frontend integration

- Frontend:BaseUrl used to generate confirmation/reset links

### 5.4 CORS

- Cors:AllowedOrigins must contain at least one origin (startup throws if empty)

### 5.5 Email

- Brevo:ApiKey
- Brevo:SenderEmail
- Brevo:SenderName

Operational note:

- If Brevo credentials are empty, emails are logged, not sent externally.

## 6. Authentication and Identity Model

### 6.1 User entity

ApplicationUser extends IdentityUser with:

- CreatedAtUtc
- UserTier (default Free)

Note:

- CreatedAtUtc supports stale-unconfirmed-account cleanup.
- UserTier drives quota policy with minimal DB footprint.

### 6.2 Password policy

Configured in Program.cs:

- Minimum length 8
- Requires digit, uppercase, lowercase
- Does not require non-alphanumeric
- Unique email required

### 6.3 JWT claims

JwtTokenService emits:

- sub: user id
- unique_name: username
- email: email
- ClaimTypes.NameIdentifier: user id
- ClaimTypes.Name: username

Validation rules:

- Validates issuer, audience, key, lifetime
- Clock skew 1 minute

Note:

- Some ASP.NET and SignalR flows read ClaimTypes.* while others prefer standard JWT claims like sub.

### 6.4 SignalR auth handling

JWT bearer OnMessageReceived reads access_token query param only for ImportProgressHub path.

Note:

- WebSocket/SSE clients commonly pass token in query string during upgrade.
- Scope is tightly restricted to hub path to avoid broad token-in-query acceptance.

### 6.5 Auth rate limiting

- AuthLogin policy: 5 requests per IP per 1 minute
- AuthForgotPassword policy: 5 requests per IP per 5 minutes

Used on:

- POST /api/auth/login
- POST /api/auth/forgot-password
- POST /api/auth/resend-confirmation
- POST /api/auth/change-pending-email



## 7. API Surface (Current)

### 7.1 Auth endpoints

Base route: /api/auth

1. POST /register
- Input: login, email, password
- Behavior: creates user, sends confirmation email
- Response: 202 Accepted with RequiresEmailConfirmation=true and message
- Note: registration accepted but account usage depends on asynchronous email confirmation

2. POST /login
- Input: login (username or email), password
- Behavior: username lookup first, then email; validates password
- If email unconfirmed: returns 403 with code EMAIL_NOT_CONFIRMED
- Success: returns JWT token payload

3. POST /confirm-email
- Input: userId, token (Base64Url)
- Behavior: decodes token, confirms email
- If already confirmed: still returns a token (idempotent-friendly UX)

4. POST /resend-confirmation
- Input: usernameOrEmail
- Behavior: silently resends only when applicable
- Response always generic success text to avoid account enumeration

5. POST /change-pending-email
- Input: usernameOrEmail, password, newEmail
- Allowed only for unconfirmed accounts
- Note: allows typo recovery before initial confirmation without admin intervention

6. POST /forgot-password
- Input: email
- Behavior: generates reset link using Frontend:BaseUrl when user exists
- Always returns generic success message

7. POST /reset-password
- Input: email, token (Base64Url), newPassword
- Behavior: decodes token and submits Identity reset

### 7.2 Account endpoints

Base route: /api/account
All endpoints require auth except confirm-email-change.

1. GET /summary
- Returns nickname/email and quota usage/limits:
  - savedGamesUsed, savedGamesLimit
  - importedGamesUsed, importedGamesLimit

2. POST /change-email
- Input: newEmail, currentPassword
- Behavior: validates current password and uniqueness, then sends confirmation link
- Email is not changed immediately (two-step confirmation)

3. POST /confirm-email-change (AllowAnonymous)
- Input: userId, newEmail, token (Base64Url)
- Behavior: validates token and applies email change
- Explicitly ensures EmailConfirmed true after change

4. POST /change-password
- Input: currentPassword, newPassword
- Uses Identity ChangePassword flow

5. POST /delete
- Input: password
- Behavior: validates password and deletes user via Identity
- Cascade effects come from FK delete behavior in DB model

### 7.3 PGN import endpoints

Base route: /api/pgn

1. POST /import
- Anonymous endpoint
- Input: pgn
- Executes PgnImportService (main tables, no user database linking)

2. POST /import-to-database (Authorize, 200MB request limit)
- Input: pgn, userDatabaseId
- Executes DirectDatabaseImportService
- Verifies ownership of target user database

3. POST /drafts/import (Authorize, 200MB request limit)
- Input: pgn
- Executes DraftImportService into staging tables
- Always clears existing staging games for owner first

4. POST /drafts/promote (Authorize)
- Input: userDatabaseId
- Executes DraftPromotionService -> DraftPromotionRepository bulk SQL promotion

5. GET /drafts/import-progress (Authorize)
- Returns last cached progress update for current user or 204 NoContent

6. GET /drafts/games (Authorize)
- Paginated list of staging games with filters/sorting
- Supports player, elo, year, ECO, result, move count, and position search

7. GET /drafts/games/{gameId} (Authorize)
- Replay payload for one staging game (moves + FEN history)

8. DELETE /drafts (Authorize)
- Clears current user staging games

### 7.4 Explorer endpoints

Base route: /api/games/explorer

1. POST /search (AllowAnonymous)
- Executes GameExplorerService.SearchAsync
- Optional scoped search by userDatabaseId
- Exceptions mapped:
  - ForbiddenException -> 403
  - KeyNotFoundException -> 404

2. POST /position/move
- No auth required
- Executes PositionPlayService.TryApplyMove
- Supports SAN path and coordinate path

3. POST /move-tree (Authorize)
- Input includes source (user database or staging), fen, optional filters
- Requires userDatabaseId when source=UserDatabase
- Returns next-move aggregate with outcome percentages

### 7.5 User database endpoints

Base route: /api/user-databases

1. GET /mine (Authorize)
- Owner databases only, sorted by name

2. GET /bookmarks (Authorize)
- Returns bookmarks visible to user (public DBs or own DBs)

3. GET /{id}
- Public DB: accessible
- Private DB: owner-only

4. GET /{id}/games
- Public/private visibility checks as above
- Paginated and filterable game list for that user DB

5. GET /{id}/games/{gameId}
- Returns replay for linked game

6. POST /
- Creates owner-scoped database, enforces unique name per owner

7. PUT /{id}
- Owner-only update of name + visibility, preserves owner-scoped uniqueness

8. DELETE /{id}
- Owner-only
- Deletes links and DB, then deletes orphaned Games in batches

9. POST /{id}/bookmark
- Idempotent bookmark create (returns Created flag)

10. DELETE /{id}/bookmark
- Idempotent bookmark removal

11. POST /{id}/games
- Owner-only bulk link of existing games
- Validates all game IDs exist
- Copies metadata snapshot (Date/Year/Event/Round/Site) into join row

12. DELETE /{id}/games/{gameId}
- Owner-only unlink of one game

## 8. Import Workflows in Depth

### 8.1 Standard import (PgnImportService)

Flow:

1. Parse PGN stream incrementally (IAsyncEnumerable from parser).
2. Count parsed/imported/skipped.
3. Skip games with missing white or black player names.
4. Per game: derive Year from Date, set MoveCount, normalize names, compute GameHash.
5. Populate positions via PositionImportCoordinator.
6. Persist in batches through repository and SaveChanges.
7. Clear EF tracker after each batch.

Note:

- Incremental parsing avoids loading huge PGN files fully in memory.
- Tracker clearing prevents memory blowup and degraded performance during large imports.
- Name normalization + hash generation enables fast filtering and dedup-like logic later.

### 8.2 Direct import to user database (DirectDatabaseImportService)

Flow:

1. Validates owner and target DB ownership.
2. Opens one transaction for whole operation.
3. Parses + maps games similarly to PgnImportService.
4. Populates positions and inserts Games.
5. Inserts UserDatabaseGame links with metadata snapshot.
6. Commits transaction.

Note:

- Prevents partial state where games exist but links are missing (or vice versa).

### 8.3 Draft import to staging (DraftImportService)

Flow:

1. Resolve draft quota from UserQuotaService.
2. Begin transaction.
3. Clear all staging games for owner.
4. Publish progress update "Import started".
5. Parse games; skip invalid-name games.
6. Enforce quota with all-or-nothing semantics:
   - If quota exceeded, throw and rollback all imported staging rows.
7. Map each parsed game to StagingGame + StagingMove entries.
8. Generate positions by mapping staging game to transient Game and reusing PositionImportCoordinator.
9. Persist via DraftImportRepository.
10. Publish progress every batch and periodically every ~500ms.
11. On success: commit + publish completed.
12. On failure: rollback + publish failed.

Note:

- Simplifies quota semantics and UX by ensuring one active draft set per user.
- Avoids state complexity from partial merges of multiple unpromoted imports.

### 8.4 Draft persistence optimization (DraftImportRepository)

Behavior:

- If provider is Npgsql, uses PostgreSQL binary COPY for StagingGames, StagingMoves, StagingPositions.
- Falls back to normal EF AddRange for non-Npgsql connections.

Note:

- Binary COPY is significantly faster for large bulk inserts than row-by-row ORM writes.

### 8.5 Draft promotion (DraftPromotionService + Repository)

Flow:

1. Validate target DB existence and ownership.
2. Begin transaction.
3. Execute one SQL script block that:
   - Inserts StagingGames -> Games (ON CONFLICT Id DO NOTHING)
   - Inserts StagingMoves -> Moves (ON CONFLICT Id DO NOTHING)
   - Inserts StagingPositions -> Positions (ON CONFLICT Id DO NOTHING)
   - Inserts links into UserDatabaseGames (ON CONFLICT UserDatabaseId,GameId DO NOTHING)
4. Deletes owner staging games.
5. Returns delete count as promotedCount.
6. Restores DB command timeout after operation.

Command timeout is set to 5 minutes during promotion.

Note:

- Set-based SQL is faster than graph materialization through EF for promotion-scale data.
- ON CONFLICT makes operation idempotent for duplicate IDs and existing links.

Important behavioral change from older docs:

- There is no duplicate handling mode parameter in current API. Promotion is conflict-tolerant via SQL ON CONFLICT rules.

## 9. Explorer and Filtering Pipeline

### 9.1 Search scope model

In GameExplorerRepository.SearchAsync:

- If request contains userDatabaseId:
  - access check performed
  - query scoped to links in that one user database
- Else:
  - query includes games linked to public databases
  - and private databases owned by current user (if authenticated)

Note:

- Anonymous users can explore public data.
- Authenticated users transparently see own private database content in unscoped explorer mode.

### 9.2 Shared filter extensions

GameFilteringExtensions applies reusable filters for:

- Scalar filters:
  - Elo (One/Both/Avg modes)
  - Year range
  - ECO prefix
  - Result exact
  - Move count min/max
- Player filters:
  - first/last-name normalized comparisons
  - optional color-agnostic matching
- Position filters:
  - Exact and SamePosition modes using Fen/FenHash rules

Implemented for query shapes:

- IQueryable<Game>
- IQueryable<StagingGame>
- IQueryable<UserDatabaseGame>

Note:

- Keeps controller/repository code readable.
- Ensures filter logic is consistent across explorer, staging list, and user-db list endpoints.

### 9.3 Position filtering strategy

- Exact mode:
  - Game queries can use FenHash + Fen exact string.
  - Staging/UserDatabaseGame paths compare by Fen for exact and by hash for same-position.
- SamePosition mode:
  - Uses FenHash for transposition-aware equivalence.

Note:

- Hash is efficient for grouping and quick equality checks.
- Full Fen string provides collision-safe exactness where needed.

### 9.4 Move tree aggregation

Move tree logic supports two sources:

1. UserDatabase source:
- Owner-only access check (private owner context)
- Finds parent positions matching request fen
- Joins child positions at ply+1
- Groups by child LastMove SAN
- Computes counts: games, white wins, draws, black wins

2. StagingSession source:
- Same strategy over staging tables filtered to ownerUserId

Service-level post-processing:

- Calculates percentages rounded to 2 decimals:
  - WhiteWinPct
  - DrawPct
  - BlackWinPct

Note:

- Produces statistically meaningful "next move" frequencies from actual continuation positions.

### 9.5 Position move endpoint internals

PositionPlayService supports:

1. SAN-first path:
- If SAN provided, tries to apply SAN directly.

2. Coordinate path:
- Validates source/target squares.
- Validates moving piece belongs to side to move.
- Infers promotion queen when pawn reaches last rank and promotion not provided.
- Generates SAN candidates (including castling aliases O-O and 0-0).
- Tests candidates by applying SAN on cloned board states.
- Verifies board transition matches requested from->to and expected promoted piece.

Note:

- SAN path supports normal chess notation input.
- Coordinate path supports UI drag-and-drop while still reusing SAN legality engine.

## 10. Database Model and Mapping

Source: backend/src/ChessXiv.Infrastructure/Data/ChessXivDbContext.cs

Tables and key relations:

- Games, Moves, Positions
- StagingGames, StagingMoves, StagingPositions
- UserDatabases, UserDatabaseGames, UserDatabaseBookmarks
- AspNetUsers (Identity)

Selected constraints and indexes:

- Game:
  - GameHash max 64, indexed
  - normalized name fields indexed for player filtering
  - index on (Year, Id), MoveCount
- Position:
  - indexes on FenHash, Fen, (GameId, PlyCount)
- UserDatabase:
  - unique (OwnerUserId, Name)
  - index IsPublic
- UserDatabaseGame:
  - composite PK (UserDatabaseId, GameId)
- StagingGame:
  - indexes by owner plus hash/name fields and CreatedAtUtc
- StagingPosition:
  - indexes FenHash and (StagingGameId, PlyCount)

Cascade behavior highlights:

- Moves and Positions cascade on Game delete.
- Staging moves/positions cascade on staging game delete.
- UserDatabase and bookmarks cascade from user delete.

Note:

- Endpoint list sorting/filtering can use link-level snapshot without requiring repeated joins for every metadata read scenario.
- Also preserves database-specific context when source Game metadata changes later.

## 11. Background and Real-Time Services

### 11.1 Unconfirmed user cleanup

Service: UnconfirmedUserCleanupService

- Runs immediately at startup then every hour.
- Deletes users where EmailConfirmed=false and CreatedAtUtc older than 24 hours.

Note:

- Prevents long-term buildup of abandoned pre-confirmation accounts.

### 11.2 Staging draft cleanup

Service: StagingDraftCleanupService

- Runs immediately at startup then every hour.
- Deletes staging games older than 24 hours.

Note:

- Keeps staging storage bounded and removes abandoned imports.

### 11.3 Draft import progress over SignalR

Pieces:

- ImportProgressHub (/hubs/import-progress, authorized)
- ImportProgressConnectionRegistry (user->connections map)
- DraftImportProgressCache (last update per user)
- SignalRDraftImportProgressPublisher (cache + push)

Publish behavior:

- If tracked connection IDs exist for user, sends directly to those clients.
- Otherwise falls back to Clients.User(ownerUserId).

Note:

- Push gives live UX.
- Cache lets GET /api/pgn/drafts/import-progress return state even if client reconnects.

## 12. Quota Model

Service: UserQuotaService

Limits:

- Draft import games:
  - Guest: 200000
  - Free: 200000
  - Premium: int.MaxValue
- Saved games:
  - Guest: 10000
  - Free: 10000
  - Premium: int.MaxValue

Mechanics:

- User tier fetched from users table and cached in memory for 10 minutes.
- Premium is case-insensitive comparison against UserTier == "Premium".

Note:

- Avoids DB lookup on every quota check while preserving near-real-time admin tier updates.

Clarification:

- Free/Premium here are internal operational tiers controlling limits, not documented as paid billing plans in backend code.

## 13. CLI Import Path

Source: backend/src/ChessXiv.Cli/Program.cs

Behavior:

1. Reads connection string from CHESSXIV_CONNECTION_STRING, then fallback ConnectionStrings:DefaultConnection.
2. Resolves PGN path in order:
   - first positional arg
   - CHESSXIV_PGN_PATH env var
   - ancestor search fallback to backend/tests/TestData/games_sample.pgn
3. Runs PgnImportService.ImportAsync with markAsMaster=true.

Note:

- Enables offline/master-dataset seeding without exposing a privileged HTTP endpoint.

## 14. Test Coverage Map

Unit tests (backend/tests/ChessXiv.UnitTests) cover:

- Auth and token generation:
  - AccountControllerTests
  - AuthControllerTests
  - JwtTokenServiceTests
- Parsing and notation:
  - PgnService.MoveTests
  - PgnService.TagTests
  - PgnImportServiceTests
- Import/promotion logic:
  - DraftImportServiceMappingTests
  - DraftPromotionServiceTests
  - PositionImportCoordinatorTests
- Chess engine and position state:
  - BitboardBoardStateTransitionTests
  - FenBoardStateSerializerTests
- Explorer/hash/name behavior:
  - GameExplorerServiceTests
  - GameHashCalculatorTests
  - PlayerNameNormalizerTests

Integration tests (backend/tests/ChessXiv.IntegrationTests) cover:

- API import endpoints and persistence:
  - PgnImportControllerApiTests
  - PgnImportPersistenceTests
- Draft promotion end-to-end:
  - DraftPromotionIntegrationTests
- User data and quota behavior:
  - UserDatabaseIntegrationTests
  - UserQuotaServiceIntegrationTests

Infra:

- Integration tests use Testcontainers PostgreSQL.

## 15. Operational Defaults and Limits

Current important constants:

- JWT clock skew: 1 minute
- Auth login limit: 5 per minute per IP
- Forgot-password/resend/change-pending-email limit: 5 per 5 minutes per IP
- Request size limit:
  - /api/pgn/import-to-database: 200MB
  - /api/pgn/drafts/import: 200MB
- Default import batch sizes:
  - PgnImportService: 500
  - DirectDatabaseImportService: 500
  - DraftImportService call site: 200
- Explorer paging:
  - default page size: 50
  - max page size: 200
- Move tree max moves:
  - default: 20
  - max clamp: 100
- Cleanup intervals:
  - stale unconfirmed users: hourly, threshold 24h
  - stale staging games: hourly, threshold 24h
- Promotion SQL timeout: 5 minutes

## 16. File Reference Index

Core runtime and config:

- backend/src/ChessXiv.Api/Program.cs
- backend/src/ChessXiv.Api/appsettings.Example.json
- backend/src/ChessXiv.Api/Authentication/JwtTokenService.cs

Controllers:

- backend/src/ChessXiv.Api/Controllers/AuthController.cs
- backend/src/ChessXiv.Api/Controllers/AccountController.cs
- backend/src/ChessXiv.Api/Controllers/PgnImportController.cs
- backend/src/ChessXiv.Api/Controllers/GameExplorerController.cs
- backend/src/ChessXiv.Api/Controllers/UserDatabasesController.cs

Application services:

- backend/src/ChessXiv.Application/Services/PgnImportService.cs
- backend/src/ChessXiv.Application/Services/DirectDatabaseImportService.cs
- backend/src/ChessXiv.Application/Services/DraftImportService.cs
- backend/src/ChessXiv.Application/Services/DraftPromotionService.cs
- backend/src/ChessXiv.Application/Services/GameExplorerService.cs
- backend/src/ChessXiv.Application/Services/PositionPlayService.cs
- backend/src/ChessXiv.Application/Services/PositionImportCoordinator.cs
- backend/src/ChessXiv.Application/Services/PgnService.cs
- backend/src/ChessXiv.Application/Services/GameHashCalculator.cs
- backend/src/ChessXiv.Application/Services/PlayerNameNormalizer.cs

Infrastructure:

- backend/src/ChessXiv.Infrastructure/Data/ChessXivDbContext.cs
- backend/src/ChessXiv.Infrastructure/Repositories/GameExplorerRepository.cs
- backend/src/ChessXiv.Infrastructure/Repositories/GameFilteringExtensions.cs
- backend/src/ChessXiv.Infrastructure/Repositories/DraftImportRepository.cs
- backend/src/ChessXiv.Infrastructure/Repositories/DraftPromotionRepository.cs
- backend/src/ChessXiv.Infrastructure/Services/UserQuotaService.cs

Background and realtime:

- backend/src/ChessXiv.Api/Services/UnconfirmedUserCleanupService.cs
- backend/src/ChessXiv.Api/Services/StagingDraftCleanupService.cs
- backend/src/ChessXiv.Api/Hubs/ImportProgressHub.cs
- backend/src/ChessXiv.Api/Services/SignalRDraftImportProgressPublisher.cs
- backend/src/ChessXiv.Api/Services/ImportProgressConnectionRegistry.cs
- backend/src/ChessXiv.Api/Services/DraftImportProgressCache.cs

Domain entities:

- backend/src/ChessXiv.Domain/Entities/Game.cs
- backend/src/ChessXiv.Domain/Entities/Move.cs
- backend/src/ChessXiv.Domain/Entities/Position.cs
- backend/src/ChessXiv.Domain/Entities/StagingGame.cs
- backend/src/ChessXiv.Domain/Entities/StagingMove.cs
- backend/src/ChessXiv.Domain/Entities/StagingPosition.cs
- backend/src/ChessXiv.Domain/Entities/UserDatabase.cs
- backend/src/ChessXiv.Domain/Entities/UserDatabaseGame.cs
- backend/src/ChessXiv.Domain/Entities/UserDatabaseBookmark.cs

CLI and tests:

- backend/src/ChessXiv.Cli/Program.cs
- backend/tests/ChessXiv.UnitTests/
- backend/tests/ChessXiv.IntegrationTests/
