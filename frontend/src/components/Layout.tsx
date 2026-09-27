import { useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';

import { useMarketStatus, useSession } from '../api/hooks';
import { formatDate } from '../lib/format';

function SearchForm({ className = '' }: { className?: string }) {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (query.trim().length >= 2) navigate(`/search?q=${encodeURIComponent(query.trim())}`);
  };

  return (
    <form role="search" onSubmit={submit} className={`relative ${className}`}>
      <label htmlFor="site-search" className="sr-only">
        Search cards
      </label>
      <input
        id="site-search"
        type="search"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        placeholder="Search cards, e.g. Charizard"
        className="h-10 w-full rounded-lg border-0 bg-white/10 pr-3 pl-9 text-[15px] text-white placeholder:text-white/60 focus:bg-white focus:text-slate-900 focus:ring-2 focus:ring-pokemon-yellow focus:outline-none focus:placeholder:text-slate-400"
      />
      <svg viewBox="0 0 20 20" aria-hidden="true" className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 fill-none stroke-current text-white/70" strokeWidth="2">
        <circle cx="9" cy="9" r="6" />
        <path d="m14 14 4 4" strokeLinecap="round" />
      </svg>
    </form>
  );
}

const navClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-1.5 text-sm font-semibold transition ${isActive ? 'bg-white/15 text-white' : 'text-white/75 hover:text-white'}`;

export function Layout() {
  const status = useMarketStatus();
  const session = useSession();
  return (
    <div className="flex min-h-screen flex-col">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded focus:bg-white focus:px-3 focus:py-2">
        Skip to content
      </a>
      <header className="bg-pokemon-pokeblue text-white">
        <div className="container-custom flex flex-wrap items-center gap-x-6 gap-y-3 py-3">
          <Link to="/" className="flex items-center gap-2.5 font-heading text-lg font-extrabold tracking-tight">
            <span aria-hidden="true" className="grid h-8 w-8 place-items-center rounded-full bg-pokemon-yellow text-sm font-black text-pokemon-pokeblue">
              $
            </span>
            TCG Signal
          </Link>
          <nav aria-label="Main" className="flex items-center gap-1">
            <NavLink to="/sets" className={navClass}>
              Sets
            </NavLink>
            <NavLink to="/market" className={navClass}>
              Market
            </NavLink>
            <NavLink to="/outlook" className={navClass}>
              Outlook
            </NavLink>
            <NavLink to="/deal" className={navClass}>
              Deal
            </NavLink>
            <NavLink to="/watchlist" className={navClass}>
              Watchlist
            </NavLink>
            <NavLink to="/master-sets" className={navClass}>
              Master Sets
            </NavLink>
            <NavLink to="/dashboard" className={navClass}>
              Dashboard
            </NavLink>
            {session.data?.hasStoreFinder && (
              <NavLink to="/available-in-stores" className={navClass}>
                Available in Stores
              </NavLink>
            )}
          </nav>
          <SearchForm className="order-last w-full lg:order-none lg:ml-auto lg:w-64" />
          <Link
            to="/pro"
            className="rounded-full bg-pokemon-yellow px-3 py-1.5 text-xs font-extrabold text-pokemon-pokeblue"
          >
            {session.data?.isPro ? (session.data.billingConfigured ? 'PRO' : 'PRO PREVIEW') : 'GET PRO'}
          </Link>
        </div>
      </header>

      <main id="main" className="flex-1">
        <Outlet />
      </main>

      <footer className="border-t border-slate-200 bg-white">
        <div className="container-custom flex flex-wrap items-center justify-between gap-3 py-6 text-sm text-slate-500">
          <p>
            TCG Signal uses TCGplayer pricing via the Pokémon TCG API
            {status.data?.lastSnapshotAt ? `, last recorded ${formatDate(status.data.lastSnapshotAt, 'short')}` : ''}.
            Not affiliated with Nintendo, The Pokémon Company or TCGplayer.
          </p>
          <a href="https://github.com/poker-kid-100717/tcg" className="font-semibold hover:text-slate-900">
            Source on GitHub
          </a>
        </div>
      </footer>
    </div>
  );
}
