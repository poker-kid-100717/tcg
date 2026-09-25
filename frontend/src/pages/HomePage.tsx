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
          <p className="text-xs font-semibold tracking-wider text-pokemon-yellow uppercase">Pokémon TCG price guide</p>
          <h1 className="max-w-3xl text-4xl leading-tight text-white sm:text-5xl">
            What is your card worth today?
          </h1>
          <p className="max-w-2xl text-lg text-white/80">
            TCGplayer market prices for every card in every set, with a daily price history. Found the one you want?
            Every card links straight to its TCGplayer listing.
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
