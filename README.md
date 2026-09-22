# Pokémon TCG Marketplace

A full-stack marketplace for Pokémon trading cards: browse the full card catalog,
track price history and investment potential, and manage a cart, wishlist and
order history behind real authentication.

- **Backend**: ASP.NET Core 8 Web API, EF Core over SQLite, JWT auth (BCrypt password hashing)
- **Frontend**: React 19 (Create React App), Tailwind CSS, Chart.js, React Router

## Features

- **Card catalog** - browse, filter and search the full Pokémon card database (via the public [Pokémon TCG API](https://docs.pokemontcg.io/))
- **Set explorer** - browse cards by set, with set metadata and symbols
- **Price history & investment analysis** - per-card price trend chart and a computed investment-potential rating (rarity, price growth, set rotation, popularity)
- **Accounts** - register/login issuing a JWT, passwords hashed with BCrypt, never stored or transmitted in the clear
- **Cart** - guest-friendly, persisted in the browser via `localStorage`
- **Checkout, orders & wishlist** - require sign-in; persisted server-side per user in SQLite so they survive a page refresh, a different device, or a backend restart

## Tech stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 8, EF Core 8, SQLite, JWT Bearer auth, BCrypt.Net, Swagger |
| Frontend | React 19, React Router 7, Tailwind CSS, Chart.js, Axios |
| Testing | xUnit (backend), Jest + React Testing Library (frontend) |
| CI | GitHub Actions - builds and tests both projects on every push/PR to `main` |

## Architecture

```
backend/            ASP.NET Core Web API
  Controllers/       Auth, Cards, Sets, Data, Orders, Wishlist
  Services/          UserService, TokenService, PokemonTcgService
  Models/            User, Order, OrderItem, WishlistItem
  Data/               AppDbContext + EF Core migrations
backend.Tests/       xUnit tests for the services and controllers above

frontend/
  src/pages/          Route-level components (Shop, Cart, Checkout, Orders, ...)
  src/components/     Reusable UI (Header, CardGrid, PriceChart, ...)
  src/services/        api.js (public card catalog), authService/ordersService/
                        wishlistService (backend), cartService (local cart)
```

**Why SQLite, not SQL Server or a hosted database?** The original scaffold used
EF Core's `InMemoryDatabase` provider, which throws away all data on every
restart - registrations, orders and wishlists all vanished the moment the
process stopped. SQLite is a real, ACID-compliant relational database that
needs zero external infrastructure: `dotnet run` creates and migrates a single
`pokemontcg.db` file next to the project, so anyone cloning this repo can run
it immediately without installing or provisioning a database server. EF Core
migrations (`backend/Migrations/`) manage the schema, exactly as they would
against SQL Server or PostgreSQL in a larger deployment - swapping the
provider later is a one-line change in `Program.cs`.

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

- [.NET SDK 8.0](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) and npm

### Backend

```bash
cd backend
dotnet restore
dotnet run
```

The API listens on `http://localhost:5259` by default (see
`backend/Properties/launchSettings.json`) and serves Swagger UI at `/swagger`
in Development. On first run it applies EF Core migrations and seeds two demo
users:

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
the backend, since it's persisted in `backend/pokemontcg.db`.

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
- CORS is currently wide open (`AllowAnyOrigin`) to keep local development
  simple; a real deployment should restrict it to the frontend's actual
  origin.
- This repository's history (prior to the cleanup commit) contains a
  previously-committed cloud credentials file and a `.env` with a database
  connection string. They have been removed from the working tree and a
  `.gitignore` now prevents recurrence, but they remain reachable in the git
  history on GitHub pending a decision on rewriting it.
