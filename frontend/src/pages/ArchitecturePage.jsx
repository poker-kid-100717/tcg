import React from 'react';

const layers = [
  { name: 'React frontend', note: 'Pages, components, services (Tailwind CSS) — public card data fetched straight from the Pokémon TCG API' },
  { name: 'Cloudflare Worker', note: 'Serves the React build from the edge and routes /api/* to the API container on the same origin' },
  { name: 'ASP.NET Core 10 API', note: 'Auth, Cards, Sets, Data, Orders, Wishlist controllers — JWT-authenticated, runs as a Cloudflare Container' },
  { name: 'PostgreSQL + EF Core', note: 'Managed Postgres with EF Core migrations applied on start-up; Docker Compose Postgres locally' }
];

const decisions = [
  {
    choice: 'PostgreSQL with real EF Core migrations',
    instead: 'the original EF Core InMemoryDatabase provider (and a file-based database)',
    why: "The scaffold this was rebuilt from wiped every registration, order, and wishlist on every process restart — InMemory throws its data away by design. A file-based database would have the same problem in production: the API runs as a Cloudflare Container that scales to zero, and a container's disk does not survive a restart. State therefore lives in managed PostgreSQL, the container stays stateless, and the schema is owned by EF Core migrations that the API applies on start-up. Locally, docker compose brings up the same Postgres engine, so dev and production run identical SQL."
  },
  {
    choice: 'One Cloudflare Worker in front of both the static app and the API container',
    instead: 'separate hosts for the frontend and backend with CORS between them',
    why: "The Worker serves the React build straight from Cloudflare's edge and forwards only /api/* and /healthz to the ASP.NET Core container. Everything is same-origin, so there is no CORS surface, the frontend needs no environment-specific API URL, and TLS terminates at the edge. Secrets (database URL, JWT signing key) live on the Worker and are injected into the container as ASP.NET Core configuration at start-up, never baked into the image."
  },
  {
    choice: 'The frontend calls the public Pokémon TCG API directly for card and set data',
    instead: 'always routing card/set reads through this backend’s own CardsController/SetsController proxy',
    why: "Card and set data is public and read-only, so the frontend fetches it straight from api.pokemontcg.io — one fewer network hop, and the browser caches responses itself. The backend's proxy (with IMemoryCache-backed caching in PokemonTcgService) still exists and is fully tested — it's what you'd use if this sat behind an API key that had to stay server-side, or to shield the frontend from the third party's rate limits. Both paths are real; the frontend's direct call is a deliberate simplification for a project with no API key to protect."
  },
  {
    choice: 'JWT-authenticated, server-verified identity on every user-scoped endpoint',
    instead: "the original scaffold's mock auth — a hardcoded array of users with plaintext passwords compared in the browser",
    why: 'Auth was faked client-side in the scaffold this was rebuilt from: "logging in" just matched a password against an array sitting in the frontend bundle. Every real permission check here — seeing your own orders, modifying your own wishlist — now happens server-side, resolving the calling user from a verified JWT claim, not from anything the client claims about itself.'
  }
];

export default function ArchitecturePage() {
  return (
    <div className="container-custom py-12 max-w-4xl">
      <p className="text-sm font-semibold text-primary-600 uppercase tracking-wide mb-2">How it&rsquo;s built</p>
      <h1 className="mb-4">Architecture &amp; the reasoning behind it</h1>
      <p className="text-gray-600 mb-10 max-w-2xl">
        This app was rebuilt from a duplicated, disconnected AI-generated scaffold into one real
        ASP.NET Core 10 backend and one React frontend. Below is the shape it ended up in, and the
        specific tradeoffs behind the choices that mattered &mdash; not just what pattern was used, but
        what it replaced and why.
      </p>

      <div className="flex flex-col md:flex-row gap-3 mb-12">
        {layers.map((layer, i) => (
          <React.Fragment key={layer.name}>
            <div className="card p-5 flex-1">
              <h3 className="font-heading font-bold text-gray-900 mb-1">{layer.name}</h3>
              <p className="text-sm text-gray-600">{layer.note}</p>
            </div>
            {i < layers.length - 1 && (
              <div className="hidden md:flex items-center text-gray-300 text-2xl font-bold" aria-hidden="true">&rarr;</div>
            )}
          </React.Fragment>
        ))}
      </div>

      <h2 className="mb-6">Design decisions</h2>
      <div className="space-y-6">
        {decisions.map((d) => (
          <div key={d.choice} className="border-l-4 border-primary-600 pl-5">
            <h3 className="font-heading font-bold text-gray-900">{d.choice}</h3>
            <p className="text-sm italic text-gray-500 mt-1 mb-2">instead of {d.instead}</p>
            <p className="text-gray-600 leading-relaxed">{d.why}</p>
          </div>
        ))}
      </div>

      <p className="mt-12 text-sm text-gray-500">
        Full write-up, test suite, and CI: {' '}
        <a
          href="https://github.com/poker-kid-100717/tcg"
          target="_blank"
          rel="noreferrer"
          className="text-primary-600 font-semibold hover:underline"
        >
          github.com/poker-kid-100717/tcg
        </a>
      </p>
    </div>
  );
}
