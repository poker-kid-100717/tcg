const layers = [
  { name: 'React + Vite', note: 'Set guide, card pages and search; TanStack Query caches API responses in the browser' },
  { name: 'Cloudflare Worker', note: 'Serves the site, forwards /api to the container, and runs the daily price snapshot on a Cron Trigger' },
  { name: 'ASP.NET Core 10 API', note: 'Cloudflare Container; proxies and caches the Pokémon TCG API, serves price history, and trains the ML.NET price model daily' },
  { name: 'PostgreSQL (Neon)', note: 'Daily TCGplayer price snapshots per card and printing, for history, movers, trends and sleepers' },
];

const decisions = [
  {
    choice: 'Prices link out to TCGplayer instead of an in-app checkout',
    instead: 'a cart, orders and accounts',
    why: 'This is a price guide, not a store. TCGplayer already has the inventory, sellers and checkout, so each card has a Shop now button that opens its TCGplayer listing. The app stays focused on what it adds: prices across every set, and how they move over time.',
  },
  {
    choice: 'Card data goes through the API, not straight from the browser to the Pokémon TCG API',
    instead: 'calling api.pokemontcg.io from the front end',
    why: 'The API keeps the Pokémon TCG API key on the server, caches responses (HybridCache) so popular sets and cards aren’t refetched for every visitor, retries transient failures, and answers a clear 502 if the upstream API is down.',
  },
  {
    choice: 'A Cloudflare Cron Trigger starts the daily price snapshot',
    instead: 'a timer inside the API',
    why: 'The API container sleeps after 10 minutes without traffic, so an in-process timer wouldn’t run reliably. The Worker’s scheduled handler wakes the container once a day and calls an internal endpoint the Worker never exposes publicly.',
  },
  {
    choice: 'Each page of prices is written with one bulk upsert per table (Postgres unnest)',
    instead: 'saving tens of thousands of rows through the ORM one by one',
    why: 'A run covers every card in 250-card pages, so a few dozen statements write the whole day. Snapshots are keyed by card, printing and day, which makes rerunning a day safe: it overwrites instead of duplicating.',
  },
  {
    choice: 'Price predictions come from a model trained on the recorded prices (ML.NET gradient-boosted trees), and are only shown when they beat “no change”',
    instead: 'asking a language model to guess, or publishing whatever a model outputs',
    why: 'The model learns from each card’s price history and what the card is: the Pokémon and how its cards sell, rarity, printing, set age and size, rank in its set, artist, and how listings compare with sales. It is tested on the most recent weeks it never trained on, and its predictions stay hidden unless it beats simply predicting no change. Each prediction lists the factors that moved it, and once a week’s predictions reach their date they are scored against real prices on the Outlook page.',
  },
];

export default function AboutPage() {
  return (
    <div className="container-custom max-w-4xl py-10">
      <p className="eyebrow mb-2">How it works</p>
      <h1 className="mb-4 text-3xl sm:text-4xl">Where the prices come from</h1>
      <p className="mb-10 max-w-2xl text-slate-600">
        Card details and TCGplayer market prices come from the Pokémon TCG API. Once a day the app records every card&apos;s
        prices, which is where the price history and the movers, trends and sleepers come from. Everything runs on Cloudflare.
      </p>

      <ol className="mb-12 grid gap-3 md:grid-cols-4">
        {layers.map((layer) => (
          <li key={layer.name} className="panel p-4">
            <h2 className="mb-1 text-base">{layer.name}</h2>
            <p className="text-sm text-slate-600">{layer.note}</p>
          </li>
        ))}
      </ol>

      <h2 className="mb-6 text-2xl">Design decisions</h2>
      <div className="grid gap-5">
        {decisions.map((d) => (
          <section key={d.choice} className="panel p-5">
            <h3 className="text-lg">{d.choice}</h3>
            <p className="mb-2 text-sm text-slate-500">instead of {d.instead}</p>
            <p className="text-slate-700">{d.why}</p>
          </section>
        ))}
      </div>
    </div>
  );
}
