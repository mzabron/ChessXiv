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

Scripts:

- npm run start -> ng serve
- npm run build -> ng build
- npm run test -> ng test

Note:

- Angular signals simplify local component state compared to larger store setup for current scope.
- SignalR package aligns with backend hub for real-time progress updates.

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
- /api/auth/forgot-password
- /api/auth/reset-password
- /api/auth/resend-confirmation
- /api/auth/change-pending-email
- /api/auth/confirm-email

Base URL strategy:

- localhost/127.0.0.1/::1 -> http://<host>:5027/api
- non-local -> /api

Note:

- Enables local development against backend API port without extra environment config.

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

- chessxiv.auth.token
- chessxiv.auth.expiresAtUtc

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

- POST /api/pgn/drafts/import
- POST /api/pgn/drafts/promote
- POST /api/pgn/import-to-database
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

## 9. User Databases Frontend Integration

Service: user-databases-api.service.ts

Used endpoints:

- GET /api/user-databases/mine
- GET /api/user-databases/bookmarks
- POST /api/user-databases
- DELETE /api/user-databases/{id}
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

## 11. Local API Base URL Resolution

Most services follow:

- if hostname is localhost/127.0.0.1/::1 -> explicit http://host:5027 path
- else -> relative /api path

Note:

- Services can operate independently without centralized environment injection.
- Avoids accidental hard-coded localhost paths in production.

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

Shared UI:

- frontend/src/app/shared/components/sidebar/sidebar.ts
- frontend/src/app/shared/components/login-modal/login-modal.ts
- frontend/src/app/shared/components/about-modal/about-modal.ts
