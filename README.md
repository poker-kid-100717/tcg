# Pokémon TCG Marketplace

**Live:** https://tcg.portfolio-joshdavis.app · **API docs:** https://tcg.portfolio-joshdavis.app/api/docs

A full-stack marketplace for Pokémon trading cards: browse the full card catalog,
track price history and investment potential, and manage a cart, wishlist and
order history behind real authentication. It runs entirely on Cloudflare: the
React app is served from the edge, and the ASP.NET Core 10 API runs as a
Cloudflare Container backed by managed PostgreSQL.

- **Backend**: ASP.NET Core 10 Web API, EF Core 10 over PostgreSQL (Npgsql), JWT auth (BCrypt password hashing)
- **Frontend**: React 19, Tailwind CSS, Chart.js, React Router
- **Hosting**: Cloudflare Workers (static assets + routing) and Cloudflare Containers (API), managed Postgres

## Features

- **Card catalog** - browse, filter and search the full Pokémon card database (via the public [Pokémon TCG API](https://docs.pokemontcg.io/))
- **Set explorer** - browse cards by set, with set metadata and symbols
- **Price history & investment analysis** - per-card price trend chart and a computed investment-potential rating (rarity, price growth, set rotation, popularity)
- **Accounts** - register/login issuing a JWT, passwords hashed with BCrypt, never stored or transmitted in the clear
- **Cart** - guest-friendly, persisted in the browser via `localStorage`
- **Checkout, orders & wishlist** - require sign-in; persisted server-side per user in PostgreSQL so they survive a page refresh, a different device, or a container restart

## Tech stack

| Layer | Technology |
|---|---|
| Edge | Cloudflare Workers (TypeScript), Workers Static Assets, `@cloudflare/containers` |
| Backend | ASP.NET Core 10, EF Core 10, Npgsql, JWT Bearer auth, BCrypt.Net, Swashbuckle (OpenAPI), ASP.NET Core health checks |
| Database | PostgreSQL 17 (Docker Compose locally, managed Postgres such as Neon in production) |
| Frontend | React 19, React Router 7, Tailwind CSS, Chart.js, Axios |
| Testing | xUnit (backend), Jest + React Testing Library (frontend) |
| CI/CD | GitHub Actions: build/test on every PR; test-gated deploy to Cloudflare on every push to `main`, followed by a production smoke test |

## Architecture

### Runtime topology

```
                         https://tcg.portfolio-joshdavis.app
                                        │
                                        ▼
┌────────────────────────── Cloudflare edge ───────────────────────────┐
│                                                                      │
│   Workers Static Assets ◄── /, /cards/..., /static/*  (React build)  │
│             │                                                        │
│             │ /api/*, /healthz  (run_worker_first)                   │
│             ▼                                                        │
│   Worker  cloudflare/src/index.ts                                    │
│             │  getContainer(env.TCG_API, "api").fetch(request)       │
│             ▼                                                        │
│   Durable Object "TcgApi"  ──starts/stops──►  Container (port 8080)  │
│     injects secrets as env vars:              ASP.NET Core 10 API    │
│     ConnectionStrings__DefaultConnection      backend/Dockerfile     │
│     JwtSettings__Key                                 │               │
└──────────────────────────────────────────────────────┼───────────────┘
                                                       │ Npgsql (TLS)
                                                       ▼
                                             Managed PostgreSQL
                                   (users, orders, order items, wishlist)

   Browser ──► api.pokemontcg.io   (public card/set data, fetched directly)
```

- **Same origin for everything.** The Worker serves the SPA and forwards only
  `/api/*` and `/healthz` to the container, so there's no CORS surface and the
  frontend just calls the relative path `/api`.
- **Stateless compute, stateful database.** The container scales to zero after
  15 idle minutes (`sleepAfter`) and its disk doesn't survive a restart, so all
  state lives in Postgres. On start-up the API applies any pending EF Core
  migrations and seeds demo users if the database is empty.
- **One warm instance.** Requests go to a single named container instance
  (`"api"`). The API is stateless, so scaling out means switching to
  `getRandom(env.TCG_API, n)`. `max_instances` is already set to 2.
- **Secrets stay out of the image.** `DATABASE_URL` and `JWT_SIGNING_KEY` are
  Worker secrets. The `TcgApi` container class passes them in as environment
  variables using ASP.NET Core's `Section__Key` convention, so the API reads
  them through ordinary `IConfiguration`.

### Code layout

```
cloudflare/          Edge layer
  wrangler.jsonc      Worker, static assets, container + Durable Object config, custom domain
  src/index.ts        Router (assets vs. API) and the TcgApi Container class

backend/             ASP.NET Core 10 Web API (built into the container image)
  Controllers/        Auth, Cards, Sets, Data, Orders, Wishlist
  Services/           UserService, TokenService, PokemonTcgService
  Models/             User, Order, OrderItem, WishlistItem
  Data/               AppDbContext + DbInitializer (migrate + seed)
  Migrations/         EF Core migrations (PostgreSQL)
  Dockerfile          Multi-stage build → non-root aspnet:10.0 runtime
backend.Tests/       xUnit tests for the services and controllers above

frontend/            React 19 SPA
  src/pages/          Route-level components (Shop, Cart, Checkout, Orders, ...)
  src/components/     Reusable UI (Header, CardGrid, PriceChart, ...)
  src/services/       api.js (public card catalog), authService/ordersService/
                      wishlistService (backend), cartService (local cart)
  public/_headers     Security + cache headers applied by Workers Static Assets

docker-compose.yml   Local Postgres + API container
```

### Request flow: checkout

1. The React app calls `POST /api/orders` with the JWT from `httpClient.js`.
2. Workers Static Assets doesn't match `/api/*`, so the Worker runs and forwards
   the request to the `TcgApi` Durable Object. The Durable Object starts the
   container if it's asleep and proxies to port 8080.
3. ASP.NET Core validates the JWT, and `OrdersController` resolves the user from
   the `NameIdentifier` claim, never from the request body.
4. `UserService` writes the order and its items to Postgres through EF Core in
   a single `SaveChanges`, so the order and its items commit together or not at
   all.

### Design decisions

**Why PostgreSQL?** The original scaffold used EF Core's `InMemoryDatabase`
provider, which throws away every registration, order and wishlist when the
process stops. A file-based database would fail the same way here, because the
API runs in a container that scales to zero and loses its disk on restart. So
state lives in managed Postgres, and EF Core migrations
(`backend/Migrations/`) own the schema and are applied automatically on
start-up. Locally, `docker compose` runs the same Postgres engine, so dev and
production execute identical SQL.

**Why Cloudflare Containers rather than a separate API host?** It keeps the
whole product on one domain and one deploy. `wrangler deploy` builds
`backend/Dockerfile`, pushes the image, rolls out the container and uploads the
Worker and static assets together. The frontend is served from Cloudflare's
edge for free, and the API only uses compute while it's handling traffic. The
cost is a .NET cold start (a few seconds) on the first request after an idle
period, which is acceptable for a portfolio workload. Raising `sleepAfter`
reduces it.

**Why does the frontend call the public Pokémon TCG API directly for card
data, when the backend also has `CardsController`/`SetsController`?** Card and
set data is public, read-only and doesn't need authentication, so the
frontend fetches it straight from `api.pokemontcg.io` - one fewer network hop,
and the browser can cache responses itself. The backend's `CardsController`,
`SetsController` and `DataController` proxy the same API with server-side
caching (`IMemoryCache`, via `PokemonTcgService`), which is what you'd want if
this were deployed behind an API key that must stay server-side, or if you
wanted to shield the frontend from the third-party API's rate limits. Both
paths are real, tested code; the frontend's choice of the direct path is a
deliberate simplification for a project with no API key to protect.

**Auth**: `AuthController` issues a JWT on register/login (`TokenService`),
signed with a key that is **never committed** - see below. `OrdersController`
and `WishlistController` are `[Authorize]`-protected and resolve the calling
user from the JWT's `NameIdentifier` claim, so a user can only ever see or
modify their own orders and wishlist.

## Running locally

### Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/) and npm
- [Docker](https://docs.docker.com/get-docker/) (for PostgreSQL)

### Option A: API in Docker (closest to production)

```bash
docker compose up -d --build     # Postgres + API container on http://localhost:5259
cd frontend && npm install && npm start
```

### Option B: API from source (for debugging)

```bash
docker compose up -d postgres    # just the database
cd backend && dotnet run         # http://localhost:5259, uses appsettings.Development.json
cd frontend && npm install && npm start
```

The API serves Swagger UI at `http://localhost:5259/api/docs` and a
database-aware health check at `/healthz`. On first run it applies EF Core
migrations and seeds two demo users (these also work on the live site):

| Email | Password |
|---|---|
| `ash@pokemon.com` | `pikachu123` |
| `gary@pokemon.com` | `blastoise456` |

The React app opens on `http://localhost:3000` and talks to
`http://localhost:5259/api` (override with `REACT_APP_API_URL`; see
`frontend/.env.example`). Production builds default to the same-origin `/api`.

**Configuration**: the API needs two settings, both supplied as environment
variables outside Development:

| Setting | Environment variable | Local default |
|---|---|---|
| Postgres connection string | `ConnectionStrings__DefaultConnection` | `appsettings.Development.json` → compose Postgres |
| JWT signing key | `JwtSettings__Key` | fixed dev-only key in Development |

If either is missing outside Development, the app fails fast at startup with a
clear error. It never silently falls back.

**Adding a migration**:

```bash
dotnet tool install --global dotnet-ef
cd backend && dotnet ef migrations add <Name>
```

## Deployment (Cloudflare)

Every push to `main` runs `.github/workflows/deploy-cloudflare.yml`:

1. Backend tests, frontend tests and the production React build.
2. Worker typecheck.
3. `wrangler deploy` from `cloudflare/`, which builds and pushes the API image,
   rolls out the container, uploads the Worker and static assets, binds the
   `tcg.portfolio-joshdavis.app` custom domain and sets the Worker secrets.
4. Smoke test against production: `/healthz` returns `Healthy` (after a
   cold start and migrations), the SPA loads, and the OpenAPI document is served.

### One-time setup

1. **Database**: create a Postgres database (e.g. a free [Neon](https://neon.tech)
   project) and build an Npgsql connection string:
   `Host=<host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require`
2. **Cloudflare**: the `portfolio-joshdavis.app` zone must be on the account.
   Containers require the Workers Paid plan. Create an API token that can edit
   Workers scripts, Containers, Workers routes and DNS for that zone (the
   *Edit Cloudflare Workers* template plus Containers access covers it).
3. **GitHub repository secrets**: `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`,
   `DATABASE_URL`, `JWT_SIGNING_KEY` (generate with `openssl rand -base64 48`).
4. Push to `main`, or run the workflow manually.

Manual deploy from a machine with Docker running:

```bash
cd frontend && npm ci && npm run build
cd ../cloudflare && npm ci
npx wrangler secret put DATABASE_URL
npx wrangler secret put JWT_SIGNING_KEY
npx wrangler deploy
```

## Tests

### Backend

```bash
dotnet test PokemonTcgMarketplace.sln
```

Covers `TokenService` (JWT issue/validate round-trip, tampered-signature
rejection), `UserService` (password hashing, wishlist and order persistence,
per-user isolation), `AuthController` (register/login success and failure
paths), `OrdersController` (create + list orders for the authenticated user)
and `PokemonTcgService` (response parsing and its in-memory caching, against a
stubbed `HttpMessageHandler` - no real network calls).

### Frontend

```bash
cd frontend
npm test
```

Covers the pure formatting/utility functions and `authService`'s handling of
successful and failed login/register calls, asserting in particular that
neither the plaintext password nor the JWT ever end up in the persisted user
profile.

## Security notes

- Passwords are hashed with BCrypt (`BCrypt.Net-Next`) before they ever reach
  the database; the API never stores or returns a plaintext password.
- The JWT signing key is read from configuration/environment, not hardcoded -
  see "JWT signing key" above.
- In production the browser only ever talks to one origin
  (`tcg.portfolio-joshdavis.app`); the Worker forwards `/api/*` to the
  container, so CORS never comes into play. The API's permissive CORS policy
  exists only for local development (React on :3000, API on :5259).
- Production secrets (`DATABASE_URL`, `JWT_SIGNING_KEY`) live as Cloudflare
  Worker secrets and are injected into the container as environment variables
  at start-up. They are never baked into the image or committed.
- The API container runs as the non-root `app` user from the official
  `mcr.microsoft.com/dotnet/aspnet` image.
- Static assets are served with `nosniff`, `DENY` framing and a strict
  referrer policy via `frontend/public/_headers`.
- This repository's history (prior to the cleanup commit) contains a
  previously-committed cloud credentials file and a `.env` with a database
  connection string. They have been removed from the working tree and a
  `.gitignore` now prevents recurrence, but they remain reachable in the git
  history on GitHub pending a decision on rewriting it.
