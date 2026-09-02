# ChessXiv Frontend Documentation (Current Architecture)

This document describes the current Angular frontend under frontend/.

## 1. Frontend Scope

The frontend is currently a single-page shell centered on the explorer experience and account/auth modals.

Key traits:

- Standalone Angular components (no NgModule-based structure)
- Signal-based local state for interactive UI pieces
- Service-based HTTP clients for auth, account, explorer, draft import, and user databases
- SignalR integration for live draft-import progress

## 2. Runtime and Build

Source of dependencies/scripts: frontend/package.json

Core stack:

- Angular 21
- RxJS 7
- @microsoft/signalr 10
- jwt-decode 4
- chess.js 1.4 (client-side legality checks and SAN conversion)
- Stockfish 18 (WASM engine, fetched by a script rather than depended on - see below)

Scripts:

- npm run start -> ng serve
- npm run build -> ng build
- npm run test -> ng test

Note:

- Angular signals simplify local component state compared to larger store setup for current scope.
- SignalR package aligns with backend hub for real-time progress updates.
- `scripts/fetch-engine.mjs` downloads the four Stockfish lite files into `frontend/.engine/`
  (git-ignored) and angular.json copies them to `engine/` in the build output. It runs from
  `postinstall`, or by hand with `npm run engine:fetch`, and is a no-op once the files verify.
  Files are pinned by version *and* SHA-256: a changed byte fails the build rather than
  shipping quietly. `STOCKFISH_MIRROR` overrides the source for an internal or offline build.
- This replaced an `npm i stockfish` dependency. That package ships every build it has,
  including two 113 MB full-strength ones no browser can sensibly download: 167 MB over the
  wire and 248 MB on disk for the 14 MB actually used. It matters because this app is built on
  the same machine that serves it.
- The engine is never imported into a bundle: it is fetched at runtime, and only when a user
  switches it on.
- `ng serve` sends `Cross-Origin-Opener-Policy: same-origin` and
  `Cross-Origin-Embedder-Policy: require-corp`. Those two headers are what make
  `SharedArrayBuffer` available, and without it the engine falls back to a single-threaded
  build. **A production host must send them too** or analysis loses multi-threading. The COEP
  header is also why the board's piece images carry `crossorigin="anonymous"`: they come from
  upload.wikimedia.org, which sends CORS headers but no Cross-Origin-Resource-Policy.

## 3. Application Entry and Global Providers

### 3.1 App component responsibilities

Primary root component: frontend/src/app/app.ts

The root App component:

1. Renders explorer + sidebar in normal mode.
2. Switches to special auth views based on URL pathname:
   - /reset-password
   - /confirm-email
   - /confirm-email-change
3. Parses confirmation/reset query params directly from URL.
4. Handles confirmation flows via auth/account services.

Note:

- Routes array is empty (app.routes.ts), so special paths are controlled directly by App component logic.
- This keeps flow simple during current app stage but centralizes URL-state logic in one component.

### 3.2 Global providers

Configured in frontend/src/app/app.config.ts:

- provideBrowserGlobalErrorListeners
- provideHttpClient with authInterceptor
- provideRouter(routes) where routes is currently []

Note:

- Ensures every API request to /api/* automatically receives Authorization header when token exists.

## 4. Authentication and Session Architecture

### 4.1 Auth API service

frontend/src/app/core/auth/auth-api.service.ts

Endpoints consumed:

- /api/auth/register
- /api/auth/login
- /api/auth/guest-session
- /api/auth/forgot-password
- /api/auth/reset-password
- /api/auth/resend-confirmation
- /api/auth/change-pending-email
- /api/auth/confirm-email

All services use a relative `/api` base URL, so local development needs a dev-server proxy
to the backend.

### 4.2 Auth state service

frontend/src/app/core/auth/auth-state.service.ts

Responsibilities:

- Holds current user in signal state
- Exposes computed properties:
  - currentUser
  - isAuthenticated
  - userName
- Stores/loads session through AuthSessionService
- Decodes JWT payload to AuthUser (userId, userName, email)

JWT claim fallback logic:

- userId from sub or nameidentifier URI claim
- userName from name URI claim or unique_name

Note:

- Backend emits both standard JWT and ClaimTypes claims; frontend accepts both to remain robust.

### 4.3 Session persistence

frontend/src/app/core/auth/auth-session.service.ts

Storage keys:

- `chessxiv.auth.token` / `chessxiv.auth.expiresAtUtc` in **localStorage** (registered user)
- `chessxiv.guest.token` / `chessxiv.guest.expiresAtUtc` in **sessionStorage** (guest)

`getAccessToken()` returns the user token when there is one, otherwise the guest token, so
the interceptor and SignalR work unchanged for both.

Guest tokens live in sessionStorage on purpose: closing the tab drops the token, which makes
the guest's uploaded draft unreachable at once. The backend then sweeps it once idle.

Signing in or out clears the guest session, so a draft never carries across identities.

Session validity:

- token exists
- expiresAtUtc exists
- expiresAtUtc parses to valid date
- expiry > now

Note:

- Session survives page reload and browser restart.
- Simpler than cookie-based auth for current SPA + Bearer token model.

### 4.4 Auth interceptor

frontend/src/app/core/auth/auth.interceptor.ts

Behavior:

- Reads token from AuthSessionService.
- Adds Authorization header only when request pathname starts with /api/.

Note:

- Prevents token leakage to non-API third-party calls.

## 5. Account Integration

Service: frontend/src/app/core/auth/account-api.service.ts

Endpoints used:

- GET /api/account/summary
- POST /api/account/change-email
- POST /api/account/change-password
- POST /api/account/delete
- POST /api/account/confirm-email-change

Sidebar account UX implementation:

- Sidebar component lazily loads account summary only when user menu opens.
- Forms for email/password/delete are local reactive forms with basic validation.
- API error payload parsing supports string payloads and object payloads with Errors/errors arrays.

Note:

- Avoids unnecessary account request on every app load when user menu may never be opened.

## 6. Explorer Page Architecture

Main page: frontend/src/app/features/explorer/pages/explorer-page/explorer-page.component.ts

This component orchestrates:

- Import flow (draft and direct save to DB)
- Database selection and persistence
- Board state and move-tree refresh
- Filters and paginated game list
- Replay selection and move navigation
- Layout resizing

State management style:

- Heavily signal-based for interactive UI state
- Some imperative methods coordinate asynchronous API calls with firstValueFrom/forkJoin

Note:

- Feature is highly interactive with tightly coupled states (board FEN, selected game, filters, source mode).
- Reduces cross-component synchronization complexity at current project stage.

## 7. Draft Import and Save Flow (Frontend)

### 7.1 Draft import API service

frontend/src/app/features/explorer/services/draft-import-api.service.ts

Calls:

- POST /api/pgn/drafts/import-file (multipart; returns 202, progress arrives over SignalR)
- POST /api/pgn/import-to-database-file (multipart)
- POST /api/pgn/drafts/promote
- GET /api/pgn/drafts/import-progress
- GET /api/pgn/drafts/games
- GET /api/pgn/drafts/games/{id}
- DELETE /api/pgn/drafts

### 7.2 Real-time progress

Service: draft-import-progress.service.ts

Behavior:

- Creates HubConnection to /hubs/import-progress.
- Uses accessTokenFactory from AuthStateService token.
- Subscribes to draftImportProgress hub event and pushes updates through BehaviorSubject.
- Supports connect/disconnect/reset.

Note:

- New subscribers immediately receive the latest progress snapshot.

### 7.3 Save-to-database flow in page component

When user saves imported draft:

1. If creating a new DB, call UserDatabasesApiService.create first.
2. Call draftImportApi.promoteDraft with target database id.
3. Reload database list.
4. Open the selected database and update UI state.

Note:

- Promotion target must exist and belong to user before backend accepts request.

## 8. Explorer Board and Move Tree Integration

Service: explorer-board-api.service.ts

Endpoints:

- POST /api/games/explorer/position/move
- POST /api/games/explorer/move-tree

Request model supports:

- Board FEN context
- Source selection (user database or staging)
- Optional filter payload mirroring game filter concepts
- Optional searchByPosition/filterFen for secondary filtering

Note:

- Move frequencies and outcomes require aggregation across many stored games.

## 8a. Local Engine Analysis

Service: stockfish-engine.service.ts | Component: engine-panel.component.ts

Stockfish 18 runs in the visitor's own browser, in a Web Worker. Analysis is open-ended and
CPU-bound, so a single backend cannot run it for every visitor at once; the trade is a ~7 MB
engine download on first use, which is why nothing is fetched until the engine is switched on.

Structure:

- The service is `providedIn: 'root'` on purpose. The board component is destroyed and rebuilt
  when the page enters focus mode, and a component-scoped engine would re-download and
  re-initialise on every such switch.
- The panel renders inside the board panel and takes only a FEN. It holds no analysis state.
- The board passes `null` while in Set Position mode, which suspends analysis rather than
  searching every half-built position.

Engine builds:

- Multi-threaded (`stockfish-18-lite.js`) when `crossOriginIsolated` is true.
- Single-threaded (`stockfish-18-lite-single.js`) otherwise, with a hint in the settings
  explaining why threads are unavailable. Measured locally: ~7.5M nodes/s against ~1.5M.

Display:

- The evaluation bar and the variation list are toggled separately, and either can be used
  without the other. Both default to on and are remembered.
- The bar renders beside the board (not in the strip) because it only means anything next to
  the squares. It follows board flip and reuses the opening tree's win/draw/loss colours,
  since both answer "how much of this belongs to White".
- With the list hidden, `MultiPV` drops to 1: only the first line feeds the bar, so searching
  for the other four is effort nothing displays. The user's chosen line count is kept and
  restored when the list comes back.
- A whole line is one click target, and a click plays its *first* move only. Playing a line to
  its end would jump the board several plies from what the user is looking at; one move at a
  time advances the position and re-analyses from there, which is how a line actually gets
  explored. The move goes through the same backend validation as a dragged piece - the panel
  emits SAN and never touches the position itself.

Options:

- `MultiPV` is exposed as "Lines", 1 to 5, default 3.
- `Threads` defaults to half the machine's cores and is capped at `hardwareConcurrency`.
- `Hash` is exposed as "Memory (MB)", default 128, capped at 1024. Stockfish advertises a 32 TB
  maximum, which is meaningless in a browser: the WASM heap tops out at 2 GB.
- Every other option the engine declares is rendered generically from its `uci` response, by
  type, so a future build's options appear without a code change. Jargon names are relabelled
  (`UCI_Elo` reads as "Target rating"). They all sit in one flat list; button-typed options
  (Clear Hash) are separated out to a row of actions at the bottom, next to Reset to defaults,
  because they do something once rather than hold a value.
- Options with no observable effect here are withheld: `Ponder` needs a GUI that plays games,
  `Move Overhead` and `nodestime` only shape time management on a clock, `UCI_Chess960` needs
  a board that understands Chess960 castling, and `UCI_ShowWDL` produces a win/draw/loss
  estimate that nothing renders.
- `Skill Level` is withheld as a duplicate. Stockfish derives its internal level from
  `UCI_Elo` whenever `UCI_LimitStrength` is on and ignores `Skill Level` entirely
  (`Skill(int skill_level, int uci_elo)`: `if (uci_elo) level = f(elo); else level =
  skill_level;`), so exposing both offers two controls where one silently wins. The Elo pair
  is kept because its units mean something.
- `EvalFile` and `EvalFileSmall` are withheld for a harder reason. They name an NNUE file to
  load from disk, and a WASM build has no filesystem - the network is compiled into the .wasm.
  Sending either, even at its own declared default, throws inside the worker and kills the
  engine. Only options whose value differs from the engine's default are sent at all.

Protocol discipline:

- `position` and `setoption` are only legal while the engine is idle. A change made mid-search
  sends `stop` and waits for the answering `bestmove` before restarting.
- Board navigation is debounced (120 ms), so holding an arrow key starts one search, not one
  per keypress.
- Scores are normalised to White's perspective; the engine reports them from the side to move's,
  which flips sign every ply.
- `lowerbound`/`upperbound` info lines are ignored - they are provisional, not evaluations.

Settings and the on/off state persist in localStorage under `chessxiv.engine-settings.v1`. A
stored "on" only pre-selects the switch; the worker starts when a board is actually on screen.

## 9. User Databases Frontend Integration

Service: user-databases-api.service.ts

Used endpoints:

- GET /api/user-databases (one list for guests and signed-in users alike)
- POST /api/user-databases
- PUT /api/user-databases/{id}
- DELETE /api/user-databases/{id}
- POST /api/user-databases/{id}/bookmark, DELETE /api/user-databases/{id}/bookmark
- POST /api/user-databases/{id}/games/from-selection
- GET /api/user-databases/{id}/games
- GET /api/user-databases/{id}/games/{gameId}

Behavior details:

- Builds query params dynamically by removing undefined/null/empty values.
- Reuses sort/filter model aligned with draft game listing.

Note:

- Keeps UX consistent when switching between imported draft games and saved database games.

## 10. Routing and URL Model (Current State)

Current routes: frontend/src/app/app.routes.ts exports empty array.

Implication:

- App currently behaves as a route-less SPA shell with URL checks in root component for confirmation/reset screens.

## 11. API Base URL

All services use a relative `/api` base URL. The per-service `resolveBaseUrl()` helpers that
used to switch on hostname were dead code and have been removed, so local development needs a
dev-server proxy pointing `/api` at the backend.

## 12. UX and Error Handling Patterns

Observed current patterns:

- Optimistic UI reset on auth/logout and import transitions.
- Generic, user-safe fallback messages for unknown errors.
- Detailed extraction of backend Errors/errors arrays where available.
- Account and auth flows prefer explicit user guidance (e.g., "Check your email inbox...").

Note:

- Backend often returns structured or plain-text errors depending on endpoint; frontend has resilient parsing for both.

## 13. File Reference Index

Core app:

- frontend/src/app/app.ts
- frontend/src/app/app.html
- frontend/src/app/app.config.ts
- frontend/src/app/app.routes.ts

Auth/account:

- frontend/src/app/core/auth/auth-api.service.ts
- frontend/src/app/core/auth/auth-state.service.ts
- frontend/src/app/core/auth/auth-session.service.ts
- frontend/src/app/core/auth/auth.interceptor.ts
- frontend/src/app/core/auth/account-api.service.ts

Explorer feature:

- frontend/src/app/features/explorer/pages/explorer-page/explorer-page.component.ts
- frontend/src/app/features/explorer/services/draft-import-api.service.ts
- frontend/src/app/features/explorer/services/draft-import-progress.service.ts
- frontend/src/app/features/explorer/services/explorer-board-api.service.ts
- frontend/src/app/features/explorer/services/user-databases-api.service.ts
- frontend/src/app/features/explorer/services/stockfish-engine.service.ts
- frontend/src/app/features/explorer/services/engine.models.ts
- frontend/src/app/features/explorer/components/engine-panel/engine-panel.component.ts

Shared UI:

- frontend/src/app/shared/components/sidebar/sidebar.ts
- frontend/src/app/shared/components/login-modal/login-modal.ts
- frontend/src/app/shared/components/about-modal/about-modal.ts


## 14. Guests and view resets

`ExplorerPageComponent` runs one effect on the authentication signal. Any change of session
resets the *entire* view — database list, loaded games, current database name, selected game,
move rows and the move-tree cache — before loading anything for the new caller. Resetting only
part of it is what previously left a signed-out visitor looking at a database header with no
games behind it.

When there is no signed-in user, the effect first calls `AuthStateService.ensureGuestSession()`,
so a visitor can upload and explore a PGN without an account. Saving is gated on
`isRegisteredUser`; the UI offers "Sign in to save" instead of hiding the flow.

## 15. Move-tree caching

`ExplorerBoardApiService` memoises move-tree responses keyed by (source, database, filters,
position), with LRU eviction. A move tree is a pure function of those inputs, so the only
thing that can invalidate it is a change to the pool of games: a completed import, a save, a
database deletion, a cleared draft, or a change of session. `invalidateMoveTreeCache()` is
called at each of those points.

This is what stops the expensive start-position tree from being recomputed every time the
user opens a game, which resets the board to ply 0.

## 16. Saving and adding games

`GamesListComponent` hosts one modal used in two modes:

- **Save imported games** — promotes the whole draft into a new or existing database.
- **Add to database** — copies the current selection into one of the user's databases.

"Add" defaults to *every game matching the active filters*, not just the visible page; the
server resolves that set. Tick-boxes in the games table narrow it to an explicit selection.
Either way the backend skips games already present rather than duplicating them.

## 17. Position search

The Filters → Board section searches for the position on the board, and lets the two fields
that are part of a position's identity but not of its piece placement be varied:

- **Castling rights** — four chips (K/Q/k/q), disabled when the king or rook has left its
  home square and the right therefore cannot exist.
- **En-passant square** — only squares a pawn could actually capture on are offered, matching
  the backend rule. A target nobody can take is not part of the position.

Both override the corresponding FEN field, and the "Position searched for" box shows the
resulting FEN, so what is being searched for is always visible. `board-fen.utils.ts` holds the
FEN parsing and the availability rules.

Two match modes:

- **Same position, any move order** (default) — finds transpositions.
- **Same position, same move number** — additionally pins the ply.
