# TCG Signal

[![CI](https://github.com/poker-kid-100717/tcg/actions/workflows/ci.yml/badge.svg)](https://github.com/poker-kid-100717/tcg/actions/workflows/ci.yml)

**Live:** https://tcg-portfolio-sample.app

TCG Signal is a Pokémon card market-intelligence MVP. It starts with the same card/set lookup people expect from a price guide, then answers the harder questions:

- What is the current market reference?
- How trustworthy is that number?
- Is the market moving or noisy?
- Are there recent sold comps?
- How liquid does the observed market look?
- What happens to a deal after tax, shipping and selling costs?
- Did a watched printing cross a price or movement threshold?
- Does the forecasting model actually beat simply predicting “no change”?

The application never invents transaction counts or sale prices. When transaction-level data is unavailable, liquidity is shown as **Unknown** rather than estimated from unrelated fields.

## MVP

### Master Set completion
- Build a complete set checklist from every known card/printing variant.
- Track owned vs. missing printings, completion percentage, owned market value, and estimated remaining cost.
- Missing cards link out to TCGplayer; TCG Signal does not sell cards.
- Missing cards can be added to the existing price watchlist with a default target.
- Pro includes a grounded AI Set Advisor powered by Cloudflare Workers AI. The advisor receives only server-generated collection/market facts and falls back to deterministic recommendations if inference is unavailable.



### Free / public foundation

- Pokémon set browser and card search.
- Current raw TCGplayer pricing by printing.
- Daily local price snapshots in Postgres.
- 24-hour, 7-day and 30-day market movers.
- Most-valuable cards.
- Sustained downtrend detection using regression.
- Sleeper / supply-gap signals.
- Card-level price history.
- Direct links to TCGplayer.

### TCG Signal product layer

- **Market Intelligence**
  - 0–100 confidence score.
  - explicit confidence band.
  - data freshness.
  - 7-day / 30-day change.
  - observed volatility.
  - provider provenance.
  - liquidity label and explanation.
  - recent sold comps when Scrydex is configured.
  - “why this confidence?” explanation generated from deterministic metrics.
- **Watchlist**
  - exact card printing.
  - below-price target.
  - above-price target.
  - percentage-move target.
  - baseline captured when a card is watched.
- **In-app alerts**
  - evaluated after each successful daily snapshot.
  - crossing logic prevents repeated alerts while a price remains on the same side of a threshold.
  - idempotent event keys prevent duplicates.
- **Personal dashboard**
  - watched-printing count.
  - tracked market total.
  - unread alerts.
  - biggest watchlist movers.
- **Deal Analyzer**
  - asking price.
  - acquisition shipping.
  - tax / other cost.
  - expected sale price.
  - selling-cost preset or custom percentage.
  - outbound shipping.
  - estimated proceeds.
  - estimated net after entered costs.
  - break-even sale price.
- **Pro / billing shell**
  - Stripe Checkout.
  - Stripe Customer Portal.
  - verified Stripe webhook handling.
  - backend subscription entitlements.
  - if Stripe is not configured, the site intentionally runs in **Founding Preview** mode with Pro features unlocked so the MVP remains fully testable.

### 30-day Outlook

The existing ML.NET forecasting system is retained, but it is not the MVP's core value proposition.

- FastTree gradient-boosted regression.
- 27 engineered market/card features.
- time-separated validation.
- model is published only when it beats a no-change baseline.
- per-card prediction range and deterministic feature-contribution explanations.
- realized checkpoints are scored later against actual prices.
- cold start requires roughly 74 days of usable history at the default 30-day horizon.

## Data-provider strategy

### 1. Pokémon TCG API — compatibility/catalog source

The existing application still uses the Pokémon TCG API for:

- sets.
- cards.
- images.
- TCGplayer URL and current TCGplayer price fields.
- daily snapshot ingestion.

This integration is intentionally isolated behind `PokemonTcgClient`.

The provider has announced deprecation, so new product work should not increase coupling to it.

### 2. Scrydex — forward-looking market-intelligence provider

Scrydex is the preferred enrichment/migration target.

When these two secrets are configured:

- `SCRYDEX_API_KEY`
- `SCRYDEX_TEAM_ID`

Market Intelligence can use:

- raw Near Mint price history.
- provider market price.
- recent historical sold listings.
- listing source.
- sold date.
- sold price.
- original listing URL when supplied.

The app currently requests only the card/printing needed for a Pro intelligence view rather than pulling the entire catalog repeatedly. The local daily Postgres snapshot remains valuable for cost control, trend calculations and model training.

Scrydex is optional. If it is unavailable or not configured, the API falls back to local TCGplayer snapshots and clearly labels sold-comp/liquidity information as unavailable.

Relevant docs:

- https://scrydex.com/docs
- https://scrydex.com/docs/pokemon/price-history
- https://scrydex.com/docs/pokemon/listings
- https://scrydex.com/docs/getting-started/prices

## Architecture

```text
Browser
  |
  v
Cloudflare Worker
  |-- static React/Vite assets
  |-- /api/* + /health -> Cloudflare Container
  |
  v
ASP.NET Core 10 API
  |-- PokemonTcgClient --------> Pokémon TCG API (catalog / compatibility)
  |-- ScrydexClient -----------> Scrydex (optional premium enrichment)
  |-- StripeBillingService ----> Stripe (optional billing)
  |
  v
Neon PostgreSQL
  |-- cards
  |-- price_snapshots
  |-- snapshot_runs
  |-- prediction_runs
  |-- price_predictions
  |-- app_users
  |-- subscriptions
  |-- watchlist_items
  `-- alert_events

11:15 UTC Cloudflare Cron -> POST /internal/snapshots
                              |
                              `-> evaluate watchlist alerts after successful snapshot

11:45 UTC Cloudflare Cron -> POST /internal/predictions
```

The Worker does **not** publicly forward `/internal/*`.

## Stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite, TanStack Query, React Router, Tailwind CSS, Chart.js |
| API | ASP.NET Core 10 minimal APIs |
| Data | PostgreSQL / Neon, EF Core 10, Npgsql |
| Compatibility provider | Pokémon TCG API |
| Premium provider | Scrydex (optional) |
| Billing | Stripe Checkout + Customer Portal + signed webhooks (optional) |
| ML | ML.NET 5 FastTree |
| Edge / hosting | Cloudflare Worker + Cloudflare Container |
| CI/CD | GitHub Actions |
| Tests | xUnit + Testcontainers, Vitest, Worker tests |

## Identity and entitlements

The MVP does not ask for a password.

A first-party secure device session is created with:

- a random 256-bit token.
- only the SHA-256 token hash stored in Postgres.
- HttpOnly cookie.
- Secure cookie in production.
- SameSite=Lax.

This is intentionally an MVP identity model. It is enough for persisted watchlists, alerts and a billing customer mapping without shipping homemade password authentication.

For a multi-device consumer launch, replace this with a production OIDC provider and migrate the device profile into the authenticated account.

### Pro behavior

If all Stripe configuration values are present, Pro access is derived from backend subscription state.

Recognized active states:

- `active`
- `trialing`

If Stripe is not configured, every device receives Pro access in **Founding Preview** mode.

## API highlights

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/sets` | Set browser |
| GET | `/api/sets/{id}` | Set detail |
| GET | `/api/cards/{id}` | Card + current pricing/history |
| GET | `/api/cards?q=` | Card search |
| GET | `/api/cards/{id}/intelligence?variant=` | Market Confidence + liquidity + sold comps |
| GET | `/api/market/movers` | Market movers |
| GET | `/api/market/downtrend` | Sustained decline signal |
| GET | `/api/market/sleepers` | Listing-gap signal |
| GET | `/api/predictions` | Published model predictions |
| GET | `/api/predictions/model` | Model validation / track record |
| POST | `/api/session` | Get/create device profile |
| GET/POST | `/api/watchlist` | Watchlist |
| PUT/DELETE | `/api/watchlist/{id}` | Watch threshold management |
| GET | `/api/alerts` | In-app alert feed |
| GET | `/api/dashboard` | Personalized dashboard |
| POST | `/api/billing/checkout` | Stripe Checkout |
| POST | `/api/billing/portal` | Stripe Customer Portal |
| POST | `/api/billing/webhook` | Signed Stripe events |
| GET | `/health` | Database/migration readiness |
| GET | `/health/live` | Process liveness |

## Local development

Requires:

- .NET 10 SDK.
- Node 22.12+.
- Docker.

```bash
docker compose up -d
dotnet run --project backend

cd frontend
npm ci
npm run dev
```

Record a local snapshot:

```bash
curl -X POST http://localhost:5259/internal/snapshots
```

### Optional local Scrydex configuration

Use .NET configuration/environment variables:

```text
Scrydex__ApiKey
Scrydex__TeamId
```

### Optional local Stripe configuration

```text
Billing__StripeSecretKey
Billing__StripeWebhookSecret
Billing__ProMonthlyPriceId
Billing__ProAnnualPriceId
Billing__SiteUrl
```

If those values are absent, the UI operates in Founding Preview mode.

## GitHub / Cloudflare deployment

Required GitHub secrets:

| Secret | Purpose |
|---|---|
| `CLOUDFLARE_API_TOKEN` | Deploy Worker / Container / routes |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account |
| `DATABASE_URL` | Neon PostgreSQL connection string |

Optional data-provider secrets:

| Secret | Purpose |
|---|---|
| `POKEMONTCG_API_KEY` | Higher legacy Pokémon TCG API rate limit |
| `SCRYDEX_API_KEY` | Scrydex market intelligence |
| `SCRYDEX_TEAM_ID` | Scrydex team |

Optional billing secrets:

| Secret | Purpose |
|---|---|
| `STRIPE_SECRET_KEY` | Stripe server API |
| `STRIPE_WEBHOOK_SECRET` | Verify webhooks |
| `STRIPE_PRO_MONTHLY_PRICE_ID` | Monthly Pro Price |
| `STRIPE_PRO_ANNUAL_PRICE_ID` | Annual Pro Price |

The deployment workflow always deploys the core app. Optional secrets are uploaded only when they exist, so missing Stripe or Scrydex credentials do not break deployment.

## CI

```bash
dotnet test PokemonTcgMarketplace.sln
cd frontend && npm test && npm run build
cd .. && npm test && npm run typecheck
```

GitHub Actions also checks for pending EF Core model changes.

## Data integrity rules

TCG Signal deliberately prefers an explicit unknown over false precision.

- no fabricated sold counts.
- no fabricated liquidity.
- no silent replacement of one variant with another.
- exact printing is retained in watchlists.
- Scrydex raw intelligence uses Near Mint raw history for the requested variant.
- graded and raw sold listings are not mixed for raw liquidity metrics.
- source/provenance is displayed.
- stale observations are flagged.
- confidence explanations are deterministic.
- ML predictions are withheld when validation fails to beat the baseline.
- no promise that a card will appreciate or sell at a displayed value.

## Security notes

- production credentials are Cloudflare secrets, never frontend variables.
- Stripe webhook signatures are verified server-side.
- Stripe redirects do not grant Pro access.
- all watchlist/alert mutations are scoped to the current device user.
- internal cron endpoints are not forwarded by the Worker.
- the API container runs non-root and stores no local state.
- historic credentials previously committed to this repository should be considered compromised and rotated.

## MVP follow-ups

These are intentionally outside the current MVP definition:

1. Complete catalog migration from the deprecated Pokémon TCG API to Scrydex.
2. OIDC account login / cross-device profile sync.
3. Email or push alert delivery.
4. Collection quantities and true portfolio liquidation analysis.
5. Graded-card Market Intelligence UI.
6. Vision/scanner flow.
7. Dealer / bulk workflow.
8. Persistent Scrydex cache for high-traffic operation.
