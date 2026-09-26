# TCG Collector: Pokémon card collection tracker

[![CI](https://github.com/poker-kid-100717/tcg/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/tcg/actions/workflows/ci.yml)

**Live: [tcg-portfolio-sample.app](https://tcg-portfolio-sample.app)**

A collection tracker for the Pokémon Trading Card Game, built on a full price guide. Collectors add the cards they own
(printing, condition, quantity, what they paid) and see what the collection is really worth: not one inflated number
but three: **market value**, **value in the condition they actually have**, and **what it would net after TCGplayer
seller fees**. Every price carries a **confidence rating** (how recently it moved, how many days of data, how far the
cheapest listing sits from it), so a stale $400 price isn't taken at face value. It doesn't sell anything itself;
cards link to their TCGplayer listing.

- **My collection (home):** summary tiles (market, condition-adjusted, net if sold, gain on cost), a 90-day value
  chart, heads-ups (consider selling / watch / hold, from steady down-trends, gains on cost and the price model), every
  holding with inline quantity/condition/cost edits, and set progress with the **cost to finish each set**. Starts
  with no sign-up: the first add creates a guest collection, which can be saved with an email later (signing in from
  another device merges the guest's cards). A one-click sample collection shows it off. CSV export is spreadsheet-safe.
- **Set goals and master sets:** pick a goal for any set (even before owning a card of it): **main set** (up to the
  printed total), **full set** (secret rares included) or **master set** (every card in every printing it comes in:
  normal, reverse holo, holo…). Each goal shows owned/total and what the rest costs; a master set lists every printing
  still needed with one-click add.
- **Wishlist:** target prices, a suggested target from the low end of 90 days of prices, and a flag when the market
  price or cheapest listing reaches it.

- **Sets:** every set, grouped by series. Signed-in collectors see which cards they have, can filter to the cards
  they still need, add a card in one click, and see what the rest of the set costs (main set and with secret rares).
  Each set page lists every card with its market price, the set's total
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
- **Outlook:** a machine-learning forecast of every card's price 30 days out, with a likely range and the factors
  behind it, plus how well the model has done: its error against "no change" on held-out weeks, what it relies on,
  and a track record of past predictions checked against real prices. Each card page shows its own outlook.
- **Search:** by card name, newest sets first.

## Stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite, TanStack Query, React Router 7, Tailwind CSS, Chart.js |
| API | ASP.NET Core 10 minimal APIs, EF Core 10 on PostgreSQL (Npgsql), HybridCache, `Microsoft.Extensions.Http.Resilience` |
| ML | ML.NET 5 FastTree (gradient-boosted regression trees) with per-prediction feature contributions |
| Data | [Pokémon TCG API](https://docs.pokemontcg.io) for cards, sets and TCGplayer prices; Postgres (Neon) for the daily price snapshots |
| Hosting | Cloudflare Worker (static site, routing, Cron Trigger) and a Cloudflare Container running the API |
| Tests | xUnit + Testcontainers (real Postgres), Vitest + Testing Library, Worker routing tests |

## Architecture

```
Browser ──► Cloudflare Worker (worker/index.ts)
              ├── /*                 → React build (static assets, SPA fallback)
              └── /api/*, /health    → Cloudflare Container: ASP.NET Core 10 API ──┬──► Pokémon TCG API (cards, sets, prices)
                                                                                    └──► Neon Postgres (price snapshots)
Cron Trigger (11:15 UTC) ──► Worker.scheduled ──► POST /internal/snapshots on the container
Cron Trigger (11:45 UTC) ──► Worker.scheduled ──► POST /internal/predictions (retrain, validate, predict, score past runs)
```

```
backend/
  Pricing/
    PokemonTcgClient.cs      Typed HttpClient for the Pokémon TCG API: paging, query building and escaping
    PriceGuideService.cs     Read side: sets, set detail, card history, search, movers, top cards, down-trend, sleepers
    PriceSnapshotService.cs  Daily job: records every card's TCGplayer prices with bulk upserts
    PriceGuideEndpoints.cs   Minimal API endpoints, plus the 502 handler for upstream outages
    Predictions/
      PriceFeatures.cs       The 27 model inputs, group premiums, and a plain-English line for each
      PriceModel.cs          ML.NET FastTree training, prediction and feature contributions
      PredictionService.cs   Daily job: feature query, time-split validation, publish rule, track record
      PredictionEndpoints.cs Outlook, per-card and model-summary endpoints
  Data/                      EF Core model and migrations, startup migration with retry, readiness middleware
frontend/src/
  api/                       Typed API client and TanStack Query hooks
  pages/                     Home, Sets, Set, Card, Search, Market, Outlook, How it works
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
| `GET` | `/api/predictions?direction=up` | Cards the model predicts will rise (or `down`), with range and reasons |
| `GET` | `/api/cards/{id}/predictions` | The card's prediction for each printing |
| `GET` | `/api/predictions/model` | The current model: status, held-out error vs "no change", feature importance, track record |
| `GET` | `/api/market/status` | When prices were last recorded, how many days of history exist, and the price source |
| `GET` | `/api/account` | Who's signed in (guest or saved account) |
| `POST` | `/api/account/guest`, `/register`, `/login`, `/logout`; `DELETE /api/account` | Guest-first accounts: cookie session, rate-limited sign-in with lockout |
| `GET` | `/api/collection` | Summary, holdings with values and price confidence, 90-day value history, set progress, heads-ups |
| `POST`/`PATCH`/`DELETE` | `/api/collection/items[/{id}]` | Add (stacks matching copies, averaging cost), edit, remove |
| `GET` | `/api/collection/cards/{id}`, `/api/collection/sets/{id}` | Copies of a card you own; a set checklist with the missing cards' prices and cost to complete |
| `PUT`/`DELETE` | `/api/collection/goals/{setId}` | Start, re-target (`MainSet`, `FullSet`, `MasterSet`) or stop a set goal |
| `POST` | `/api/collection/sample` | Fills an empty collection with ~24 real cards |
| `GET` | `/api/collection/export.csv` | The collection as CSV |
| `GET`/`POST`/`DELETE` | `/api/wishlist[/{id}]` | Wishlist with target prices |
| `GET` | `/health`, `/health/live` | Readiness (database and migrations) and liveness |
| `POST` | `/internal/snapshots` | Runs the price snapshot. Called by the Cron Trigger; the Worker never forwards `/internal` from the internet |
| `POST` | `/internal/predictions` | Retrains the model and refreshes predictions. Called by the second Cron Trigger |

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
- **Bulk upserts with Postgres `unnest`, in one transaction.** A run pages through every card, 250 at a time, and
  writes each page with one `INSERT … SELECT FROM unnest(…) ON CONFLICT DO UPDATE` per table, all inside one
  transaction, so a failed run leaves nothing half-written. The whole day is a few dozen
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
- **Predictions from a trained, tested model, not a guess.**
  - **Target:** the log of each printing's price ratio 30 days ahead, predicted by gradient-boosted regression
    trees (ML.NET FastTree). Trees handle features on mixed scales and their interactions (a 1st Edition holo from
    1999 behaves nothing like last month's reverse holo), and they can say how much each feature moved one prediction.
  - **Features (27):** price level; 7, 30 and 90-day change; volatility; the 30-day trend and how steady it is; the
    cheapest, median and highest listing against the market price; set age, size and era; secret rare; rank and
    share of value in its set; rarity, Pokémon and artist premiums (how their cards sell against the typical card
    that day, shrunk toward zero for small groups); how many cards feature the Pokémon; Trainer, Energy and rule-box
    flags; and the printing. Pokémon popularity is measured from prices, so it updates itself.
  - **Validation without leakage:** samples are taken weekly. A sample's outcome is its first price in the 5 days
    from the horizon date. A date counts as labelled only once its whole outcome window has passed. The most recent 20% of labelled dates are held out, and training uses only
    dates whose whole outcome window closed before that period started. Features use only what was known on the
    sample date: a Pokémon's printings are counted from set release dates up to then, and validation error is
    measured on real (unclipped) returns.
  - **Only complete days:** a snapshot run is one database transaction, so a run that fails part-way records
    nothing, and the prediction run is skipped while a snapshot is running or after one failed; the previous
    predictions stay up. Rarity, Pokémon and artist premiums are computed in SQL over every priced printing that
    day, before any sampling. Each printing is predicted from its most recent price in the last week.
  - **Bounded memory:** rows are capped per sample date inside the SQL query (a stable pseudo-random subset, taken
    after set ranks are computed on the full day), so the container never loads more than
    `Predictions:MaxTrainingRows` labelled rows. The latest day is never capped.
  - **Publish rule:** the model has to beat predicting "no change" on the held-out weeks by at least 2%, or its
    predictions are withheld and the page says why. Each prediction's range comes from the 10th and 90th
    percentile of held-out errors.
  - **Track record:** one run a week keeps its predictions (with its own horizon, so rows stay correct if the
    horizon setting changes). Once their 30 days plus the 5-day outcome window are
    up, the next run scores them against actual prices and drops the rows. Other runs' rows are dropped once a newer run publishes, which keeps
    the table small.
  - **Cold start:** training and testing a 30-day model needs about 74 days of prices; until then the Outlook page
    says how many days it has.
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

Optional: `npx wrangler secret put TCG_POKEMONTCG_API_KEY` to give the API a Pokémon TCG API key.

**TCGplayer Developer API (optional):** set both `npx wrangler secret put TCG_TCGPLAYER_PUBLIC_KEY` and
`npx wrangler secret put TCG_TCGPLAYER_PRIVATE_KEY` (locally: `Tcgplayer__PublicKey` / `Tcgplayer__PrivateKey`). With
them, the daily snapshot matches every set to its TCGplayer group and every card to its product, takes the day's
market/low/mid/high prices straight from TCGplayer, links cards to their product pages, and the footer switches to
TCGplayer's required attribution. Without them, prices are TCGplayer's market prices as republished daily by the
Pokémon TCG API. Nothing else changes, so the keys can be added or removed at any time. The
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
  - Predictions, on 60 days of synthetic history where one Pokémon climbs and another slides: no history gives
    "insufficient history"; then the model trains, beats "no change" on held-out days, ranks the climbers top and
    the sliders bottom, keeps each prediction inside its range, explains it, and scores an older checkpoint.
  - Existing tests cover migrations, readiness and connection-string parsing.
- **Frontend:**
  - The card page shows every printing and a Shop now link to the card's TCGplayer listing, and handles 404s and
    outages.
  - The set page sorts (unpriced cards last), filters by rarity and finds cards by name or number.
  - The Outlook page lists predictions with their top reason and the model's results, and explains an empty
    state; the card page shows each printing's outlook with its reasons.
  - The Market page shows trending-down cards with their sparklines and sleepers with their listing gap, and
    explains an empty list while history is short.
- **Worker:** API paths reach the container, `/internal` never does, and the two Cron Triggers start the snapshot
  and the prediction run.

## Security notes

- The API has no accounts and stores no personal data; the only secrets are the database URL and the optional
  Pokémon TCG API key, both Worker secrets passed to the container at start.
- The container runs as a non-root user and keeps no state.
- This repository's history (before the cleanup commit) contains a previously committed cloud credentials file and
  a `.env` with a database connection string. They are gone from the working tree, but still reachable in the git
  history on GitHub; rotate those credentials.
