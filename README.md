# Pokémon TCG Price Guide

[![CI](https://github.com/poker-kid-100717/tcg/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/tcg/actions/workflows/ci.yml)

**Live: [tcg-portfolio-sample.app](https://tcg-portfolio-sample.app)**

A price guide and set guide for the Pokémon Trading Card Game. It shows the TCGplayer market price of every card in
every set, records prices daily to build a price history and a list of the week's biggest movers, and links each card
to its TCGplayer listing with a **Shop now** button. It doesn't sell anything itself.

- **Sets:** every set, grouped by series. Each set page lists every card with its market price, the set's total
  market value and most valuable card, with sorting (collector number, price, name), a rarity filter and a
  find-in-set box. Sort and rarity are kept in the URL, so a filtered view can be shared.
- **Cards:** the TCGplayer market, low, mid and high price for each printing (holofoil, reverse holo, first
  edition…), a price-history chart built from the daily snapshots, the card's details, and **Shop now on
  TCGplayer**.
- **Market:** the biggest gains and drops over 24 hours, 7 days or 30 days, the most valuable cards right now, and two
  signals:
  - **Trending down:** cards in a steady decline over 30 days (not one bad day), each with a sparkline.
  - **Sleepers:** quiet cards with nothing listed near what they sell for. The cheapest TCGplayer listing is 10%+ above
    the market price while the market price has barely moved, which often comes before the price catches up.
- **Search:** by card name, newest sets first.

## Stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite, TanStack Query, React Router 7, Tailwind CSS, Chart.js |
| API | ASP.NET Core 10 minimal APIs, EF Core 10 on PostgreSQL (Npgsql), HybridCache, `Microsoft.Extensions.Http.Resilience` |
| Data | [Pokémon TCG API](https://docs.pokemontcg.io) for cards, sets and TCGplayer prices; Postgres (Neon) for the daily price snapshots |
| Hosting | Cloudflare Worker (static site, routing, Cron Trigger) and a Cloudflare Container running the API |
| Tests | xUnit + Testcontainers (real Postgres), Vitest + Testing Library, Worker routing tests |

## Architecture

```
Browser ──► Cloudflare Worker (worker/index.ts)
              ├── /*                 → React build (static assets, SPA fallback)
              └── /api/*, /health    → Cloudflare Container: ASP.NET Core 10 API ──┬──► Pokémon TCG API (cards, sets, prices)
                                                                                    └──► Neon Postgres (price snapshots)
Cron Trigger (daily) ──► Worker.scheduled ──► POST /internal/snapshots on the container
```

```
backend/
  Pricing/
    PokemonTcgClient.cs      Typed HttpClient for the Pokémon TCG API: paging, query building and escaping
    PriceGuideService.cs     Read side: sets, set detail, card history, search, movers, top cards, down-trend, sleepers
    PriceSnapshotService.cs  Daily job: records every card's TCGplayer prices with bulk upserts
    PriceGuideEndpoints.cs   Minimal API endpoints, plus the 502 handler for upstream outages
  Data/                      EF Core model and migrations, startup migration with retry, readiness middleware
frontend/src/
  api/                       Typed API client and TanStack Query hooks
  pages/                     Home, Sets, Set, Card, Search, Market, How it works
  components/                Layout, card tile, Shop link, price history chart, movers table, sparkline, market signals
worker/index.ts              Routing, container binding, daily Cron Trigger
```

### API

| Method | Route | Returns |
|---|---|---|
| `GET` | `/api/sets` | Every set, newest first |
| `GET` | `/api/sets/{id}` | The set, its stats (total market value, most valuable card) and every card with its price |
| `GET` | `/api/cards/{id}` | Card details, prices by printing, TCGplayer link and up to a year of daily price history |
| `GET` | `/api/cards?q=&page=` | Cards whose name starts with `q` |
| `GET` | `/api/market/movers?days=7` | Biggest gains and drops between the latest snapshot and the one `days` earlier |
| `GET` | `/api/market/top` | Most valuable cards in the latest snapshot |
| `GET` | `/api/market/downtrend?days=30` | Cards falling steadily over the window, with their daily prices |
| `GET` | `/api/market/sleepers` | Cards whose cheapest listing is well above their market price while the price stays flat |
| `GET` | `/api/market/status` | When prices were last recorded and how many days of history exist |
| `GET` | `/health`, `/health/live` | Readiness (database and migrations) and liveness |
| `POST` | `/internal/snapshots` | Runs the price snapshot. Called by the Cron Trigger; the Worker never forwards `/internal` from the internet |

### Design decisions

- **Link out to TCGplayer instead of a cart and checkout.** TCGplayer already has the inventory, sellers and
  checkout. Each card's Shop now button opens its TCGplayer listing (the Pokémon TCG API's
  `prices.pokemontcg.io/tcgplayer/{id}` link, which redirects to the product page).
- **Card data goes through the API.** The Pokémon TCG API key stays on the server; HybridCache keeps popular sets
  and cards from being refetched for every visitor; the resilience handler retries transient failures with
  timeouts sized for 250-card pages; and an upstream outage returns a clear `502` instead of a `500`.
- **Daily snapshots on a Cron Trigger.** The container sleeps after 10 minutes idle, so an in-process timer
  wouldn't fire reliably. The Worker's `scheduled` handler wakes the container once a day and calls
  `/internal/snapshots`. A second trigger while a run is in progress gets `409`.
- **Bulk upserts with Postgres `unnest`.** A run pages through every card, 250 at a time, and writes each page
  with one `INSERT … SELECT FROM unnest(…) ON CONFLICT DO UPDATE` per table. The whole day is a few dozen
  statements, and rerunning a day overwrites that day's rows instead of duplicating them.
- **Only meaningful prices are kept.** Printings under $0.50 (`Snapshots:MinMarketPrice`) aren't recorded, and
  snapshots older than 400 days are deleted after each run, which keeps the table inside a free Neon database.
  Movers ignore prices under $2, so a few cents on a common doesn't top the list, and each card appears once
  (its biggest move).
- **Trends are fitted in Postgres, not eyeballed from two dates.** Trending down runs a least-squares fit over each
  printing's daily prices with `regr_slope` and `regr_r2` in one SQL query. A card qualifies only if the line slopes
  down, fits well (r² ≥ 0.6, so a single spike or dip doesn't count), has at least 5 days of prices and dropped 10% or
  more. Sleepers compare the cheapest listing (TCGplayer *low*) with the market price (recent sales), ignore gaps over
  3× as likely bad data, and require the market price to be within 15% of 30 days ago. The page states both rules and
  that they're signals, not financial advice.
- **Readiness that tells the truth.** `/health` fails until migrations have applied, requests wait for database
  initialization after a cold start, and startup retries the migration with backoff if Neon is still waking.

## Running locally

Requires the .NET 10 SDK, Node 22.12+ and Docker.

```bash
docker compose up -d                              # Postgres 17 on localhost:5432
dotnet run --project backend                      # API on http://localhost:5259 (Swagger at /swagger)
curl -X POST http://localhost:5259/internal/snapshots   # record today's prices (takes a minute or two)

cd frontend && npm ci && npm run dev              # http://localhost:3000, proxies /api to the API
```

Optionally set `PokemonTcgApi__ApiKey` (free from [dev.pokemontcg.io](https://dev.pokemontcg.io)) for a higher
rate limit. Price history and movers need at least two snapshots on different days.

## Deployment

On push to `main`, `.github/workflows/deploy-cloudflare.yml` builds the frontend, runs `wrangler deploy` (which
builds and pushes the API container image), uploads the Worker secrets, and smoke-tests the production domain:
`/health`, a page, `/api/sets` and `/api/market/status`. `ci.yml` runs every test suite and the EF migration
drift check on pull requests and pushes.

| GitHub secret | Value |
|---|---|
| `CLOUDFLARE_API_TOKEN` | Token with Workers Scripts, Containers and Routes write access |
| `CLOUDFLARE_ACCOUNT_ID` | Workers & Pages → Account ID |
| `DATABASE_URL` | Neon connection URL (pasted as-is; the API converts `postgresql://` URLs) |

Optional: `npx wrangler secret put TCG_POKEMONTCG_API_KEY` to give the API a Pokémon TCG API key. The
`JWT_KEY` secret from the marketplace version is no longer used and can be deleted.

Upgrading from the marketplace version: the `PriceGuide` migration drops the old users, orders, order items and
wishlist tables and creates `cards`, `price_snapshots` and `snapshot_runs`.

## Tests

```bash
dotnet test PokemonTcgMarketplace.sln   # API against a real Postgres (Testcontainers) and a stubbed Pokémon TCG API
cd frontend && npm test                 # Vitest + Testing Library
npm ci && npm test                      # Worker routing and the Cron Trigger (repo root)
```

- **API:**
  - Set detail follows upstream pagination and computes the set's stats.
  - Search escapes user input, so a quote can't add clauses to the upstream query.
  - Two snapshot runs a week apart produce price history, movers and top cards, and a same-day rerun doesn't
    duplicate rows.
  - Trending down picks a steady decline, but not a noisy one with the same net drop, one with too few prices or
    one under $2.
  - Sleepers need a real listing gap, a flat 30 days and a plausible gap; new cards without 30 days of history
    still count.
  - Upstream outages return `502`.
  - Existing tests cover migrations, readiness and connection-string parsing.
- **Frontend:**
  - The card page shows every printing and a Shop now link to the card's TCGplayer listing, and handles 404s and
    outages.
  - The set page sorts (unpriced cards last), filters by rarity and finds cards by name or number.
  - The Market page shows trending-down cards with their sparklines and sleepers with their listing gap, and
    explains an empty list while history is short.
- **Worker:** API paths reach the container, `/internal` never does, and the scheduled handler triggers the
  snapshot.

## Security notes

- The API has no accounts and stores no personal data; the only secrets are the database URL and the optional
  Pokémon TCG API key, both Worker secrets passed to the container at start.
- The container runs as a non-root user and keeps no state.
- This repository's history (before the cleanup commit) contains a previously committed cloud credentials file and
  a `.env` with a database connection string. They are gone from the working tree, but still reachable in the git
  history on GitHub; rotate those credentials.
