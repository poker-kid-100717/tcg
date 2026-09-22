# Pokémon TCG Marketplace

A full-stack marketplace for Pokémon trading cards: browse the full card catalog,
track price history and investment potential, and manage a cart, wishlist and
order history behind real authentication.

- **Backend**: ASP.NET Core 10 Web API, EF Core over PostgreSQL (Npgsql), JWT auth (BCrypt password hashing)
- **Frontend**: React 19 (Create React App), Tailwind CSS, Chart.js, React Router
- **Hosting**: Cloudflare - a Worker serves the React build and proxies `/api/*` to the API running in a Cloudflare Container

## Features

- **Card catalog** - browse, filter and search the full Pokémon card database (via the public [Pokémon TCG API](https://docs.pokemontcg.io/))
- **Set explorer** - browse cards by set, with set metadata and symbols
- **Price history & investment analysis** - per-card price trend chart and a computed investment-potential rating (rarity, price growth, set rotation, popularity)
- **Accounts** - register/login issuing a JWT, passwords hashed with BCrypt, never stored or transmitted in the clear
- **Cart** - guest-friendly, persisted in the browser via `localStorage`
- **Checkout, orders & wishlist** - require sign-in; persisted server-side per user in PostgreSQL so they survive a page refresh, a different device, or a backend restart

## Tech stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, EF Core 10, PostgreSQL (Npgsql), JWT Bearer auth, BCrypt.Net, Swagger |
| Frontend | React 19, React Router 7, Tailwind CSS, Chart.js, Axios |
| Testing | xUnit (backend), Jest + React Testing Library (frontend) |
| Hosting | Cloudflare Workers (static assets) + Cloudflare Containers (.NET API), managed PostgreSQL (e.g. Neon) |
| CI/CD | GitHub Actions - builds, tests, and applies migrations to a real Postgres on every push/PR; deploys to Cloudflare after CI passes on `main` |

## Architecture

```
backend/            ASP.NET Core Web API
  Controllers/       Auth, Cards, Sets, Data, Orders, Wishlist
  Services/          UserService, TokenService, PokemonTcgService
  Models/            User, Order, OrderItem, WishlistItem
  Data/               AppDbContext + EF Core migrations
backend.Tests/       xUnit tests for the services and controllers above

cloudflare/          Worker: serves frontend/build, forwards /api/* to the
                      ASP.NET Core container (wrangler.jsonc, src/index.ts)

frontend/
  src/pages/          Route-level components (Shop, Cart, Checkout, Orders, ...)
  src/components/     Reusable UI (Header, CardGrid, PriceChart, ...)
  src/services/        api.js (public card catalog), authService/ordersService/
                        wishlistService (backend), cartService (local cart)
```

**Why PostgreSQL?** The original scaffold used EF Core's `InMemoryDatabase`
provider, which throws away all data on every restart. A local SQLite file
fixed that on a laptop, but the API now runs in a Cloudflare Container, whose
disk is ephemeral - a SQLite file there would be wiped on every restart or
redeploy. PostgreSQL lives outside the container: `docker compose up -d` runs
one locally, and production points at a managed Postgres (such as Neon) through
a single connection-string secret. EF Core migrations (`backend/Migrations/`)
manage the schema and are applied automatically on startup.

**How is it hosted?** Everything is served from one Cloudflare origin. The
Worker in `cloudflare/` serves the React build as static assets and forwards
only `/api/*` to the ASP.NET Core API, which runs as a Docker image
(`backend/Dockerfile`) on Cloudflare Containers. Because the browser only talks
to one origin, production needs no CORS and the frontend simply calls `/api`.
The container sleeps after 15 minutes without traffic and starts again on the
next request (a few seconds of cold start).

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
- [Node.js 20+](https://nodejs.org/) and npm
- [Docker](https://www.docker.com/) (for the local PostgreSQL database)

### Backend

```bash
docker compose up -d      # PostgreSQL on localhost:5432 (see docker-compose.yml)
cd backend
dotnet restore
dotnet run
```

The API listens on `http://localhost:5259` by default (see
`backend/Properties/launchSettings.json`) and serves Swagger UI at `/swagger`
in Development. On startup it applies EF Core migrations to the local Postgres
database and seeds two demo users:

| Email | Password |
|---|---|
| `ash@pokemon.com` | `pikachu123` |
| `gary@pokemon.com` | `blastoise456` |

**JWT signing key**: in `Development`, a fixed non-production key is used
automatically so the project runs with zero setup. For anything beyond local
development, set a real key via an environment variable instead of editing
`appsettings.json`:

```bash
export JwtSettings__Key="a long, random, environment-specific secret"
# or, for local secret storage instead of an env var:
dotnet user-secrets set "JwtSettings:Key" "a long, random secret"
```

If `JwtSettings:Key` is unset outside Development, the app fails fast at
startup with a clear error rather than silently signing tokens with nothing.

### Frontend

```bash
cd frontend
npm install
npm start
```

Opens on `http://localhost:3000`. It talks to the backend via
`REACT_APP_API_URL` (see `frontend/.env.example`; defaults to
`http://localhost:5259/api`).

### Running both together

Start the backend first (so the API is listening), then the frontend in a
second terminal. Register a new account or sign in with the demo credentials
above, add a few cards to your cart, and check out - the order is created via
`POST /api/orders` and is visible on the Orders page even after you restart
the backend, since it's persisted in PostgreSQL.

## Deploying to Cloudflare

One-time setup:

1. **Cloudflare account on the Workers Paid plan** ($5/month) - required for
   Containers.
2. **A PostgreSQL database.** Neon's free tier works well: create a project and
   copy its connection details into an Npgsql connection string, e.g.
   `Host=ep-xxx.us-east-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=...;SSL Mode=Require`
3. **Worker secrets** (stored encrypted by Cloudflare, never committed):

   ```bash
   cd cloudflare
   npm install
   npx wrangler login
   npx wrangler secret put DATABASE_URL   # the Npgsql connection string above
   npx wrangler secret put JWT_KEY        # e.g. the output of: openssl rand -base64 48
   ```

   If `secret put` says the Worker doesn't exist yet, run one deploy first (below).
4. **GitHub repository secrets** (Settings -> Secrets and variables -> Actions):
   `CLOUDFLARE_API_TOKEN` (a token from the "Edit Cloudflare Workers" template
   with Containers access) and `CLOUDFLARE_ACCOUNT_ID`.

After that, every push to `main` that passes CI is deployed by
`.github/workflows/deploy.yml`. To deploy by hand (needs Docker running):

```bash
cd cloudflare
npm run build:frontend   # builds the React app with REACT_APP_API_URL=/api
npx wrangler deploy      # builds backend/Dockerfile, pushes it, deploys the Worker
```

The site is served at `https://pokemon-tcg-marketplace.<your-subdomain>.workers.dev`;
a custom domain can be attached in the Cloudflare dashboard (Workers ->
the Worker -> Settings -> Domains & Routes).

### Changing the database schema

Edit the models in `backend/Models/`, then create a migration against the local
Postgres:

```bash
dotnet tool install --global dotnet-ef
cd backend
dotnet ef migrations add <Name>
```

Migrations are applied automatically when the API starts, both locally and in
the container. CI fails if the model and migrations ever drift apart
(`dotnet ef migrations has-pending-model-changes`).

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
- In production the frontend and API share one origin, so no cross-origin
  access is granted unless `Cors:AllowedOrigins` (the Worker's
  `CORS_ALLOWED_ORIGINS` var) lists specific origins. Locally, with it unset,
  any origin is allowed so the CRA dev server can reach the API.
- The database connection string and JWT key are Cloudflare Worker secrets,
  passed to the container as environment variables.
- This repository's history (prior to the cleanup commit) contains a
  previously-committed cloud credentials file and a `.env` with a database
  connection string. They have been removed from the working tree and a
  `.gitignore` now prevents recurrence, but they remain reachable in the git
  history on GitHub pending a decision on rewriting it.
