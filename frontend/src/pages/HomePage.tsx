import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';

import { useMovers, useSets, useTopCards } from '../api/hooks';
import { CardTile } from '../components/CardTile';
import { MoversTable } from '../components/MoversTable';
import { formatDate } from '../lib/format';

export default function HomePage() {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const sets = useSets();
  const top = useTopCards();
  const movers = useMovers(7);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (query.trim().length >= 2) navigate(`/search?q=${encodeURIComponent(query.trim())}`);
  };

  const hasMovers = !!movers.data?.from;

  return (
    <>
      <section className="bg-pokemon-pokeblue pb-14 text-white">
        <div className="container-custom grid gap-6 pt-10 sm:pt-14">
          <p className="text-xs font-semibold tracking-wider text-pokemon-yellow uppercase">Pokémon market intelligence</p>
          <h1 className="max-w-3xl text-4xl leading-tight text-white sm:text-5xl">
            What is this card actually worth — and how much should you trust that number?
          </h1>
          <p className="max-w-2xl text-lg text-white/80">
            TCG Signal combines card pricing, history, market confidence, sold comps when available, watch alerts,
            deal math, and a validated price outlook. Start with the card; then inspect the evidence behind the number.
          </p>
          <form role="search" onSubmit={submit} className="flex max-w-xl gap-2">
            <label htmlFor="hero-search" className="sr-only">
              Search cards
            </label>
            <input
              id="hero-search"
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search by card name, e.g. Umbreon"
              className="h-12 flex-1 rounded-lg border-0 px-4 text-base text-slate-900 focus:ring-2 focus:ring-pokemon-yellow focus:outline-none"
            />
            <button type="submit" className="btn-shop h-12 px-6">
              Search
            </button>
          </form>
        </div>
      </section>

      <div className="container-custom grid gap-12 py-10">
        <section className="grid gap-4 md:grid-cols-3">
          <Link to="/deal" className="panel grid gap-2 p-5 transition hover:border-pokemon-blue">
            <p className="eyebrow">Buying or selling?</p>
            <h2 className="text-xl">Analyze the deal</h2>
            <p className="text-sm leading-6 text-slate-600">Put an asking price next to market reference, fees, tax, shipping and break-even math.</p>
          </Link>
          <Link to="/watchlist" className="panel grid gap-2 p-5 transition hover:border-pokemon-blue">
            <p className="eyebrow">Watch exact printings</p>
            <h2 className="text-xl">Set your thresholds</h2>
            <p className="text-sm leading-6 text-slate-600">Get in-app alerts after daily snapshots cross the prices or movement levels you care about.</p>
          </Link>
          <Link to="/pro" className="panel grid gap-2 p-5 transition hover:border-pokemon-blue">
            <p className="eyebrow">Market Intelligence</p>
            <h2 className="text-xl">See the evidence</h2>
            <p className="text-sm leading-6 text-slate-600">Confidence, freshness, volatility, liquidity and recent sold comps when the premium provider is configured.</p>
          </Link>
        </section>
        {sets.data && (
          <section className="grid gap-4">
            <div className="flex items-baseline justify-between">
              <h2 className="text-2xl">Latest sets</h2>
              <Link to="/sets" className="text-sm font-semibold text-pokemon-blue hover:underline">
                All sets →
              </Link>
            </div>
            <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              {sets.data.slice(0, 6).map((set) => (
                <li key={set.id}>
                  <Link
                    to={`/sets/${set.id}`}
                    className="panel flex h-full flex-col items-center gap-3 p-4 text-center transition hover:border-pokemon-blue"
                  >
                    {set.logoUrl ? (
                      <img src={set.logoUrl} alt="" loading="lazy" className="h-14 w-full object-contain" />
                    ) : (
                      <span className="h-14" aria-hidden="true" />
                    )}
                    <span className="line-clamp-1 text-sm font-semibold text-slate-900">{set.name}</span>
                    <span className="text-xs text-slate-500">{formatDate(set.releaseDate, 'short')}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}

        <section className="grid gap-4">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h2 className="text-2xl">This week&apos;s movers</h2>
            <Link to="/market" className="text-sm font-semibold text-pokemon-blue hover:underline">
              Market overview →
            </Link>
          </div>
          {hasMovers ? (
            <div className="grid gap-4 md:grid-cols-2">
              <MoversTable title="Biggest gains" moves={movers.data!.gainers.slice(0, 5)} tone="gain" />
              <MoversTable title="Biggest drops" moves={movers.data!.losers.slice(0, 5)} tone="loss" />
            </div>
          ) : (
            <p className="panel p-6 text-slate-600">
              Price moves appear once a week of daily price history has been recorded.
            </p>
          )}
        </section>

        {top.data && top.data.length > 0 && (
          <section className="grid gap-4">
            <h2 className="text-2xl">Most valuable cards right now</h2>
            <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
              {top.data.slice(0, 6).map((card) => (
                <li key={`${card.cardId}-${card.variantLabel}`}>
                  <CardTile
                    id={card.cardId}
                    name={card.name}
                    number={card.number}
                    imageUrl={card.imageUrl}
                    price={card.market}
                    subtitle={`${card.setName} · ${card.variantLabel}`}
                  />
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </>
  );
}
