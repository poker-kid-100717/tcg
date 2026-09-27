import { useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';

import { useMarketStatus, useMe, useSession } from '../api/hooks';
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

/** Primary navigation. Signals, Outlook and Master Sets are linked from Market and the dashboard. */
const NAV: [string, string][] = [
  ['/', 'Home'],
  ['/sets', 'Sets'],
  ['/market', 'Market'],
  ['/watchlist', 'Watchlist'],
  ['/deal', 'Deal Analyzer'],
  ['/dashboard', 'Dashboard'],
];

const navClass = ({ isActive }: { isActive: boolean }) =>
  `shrink-0 whitespace-nowrap rounded-md px-3 py-1.5 text-sm font-semibold transition ${isActive ? 'bg-white/15 text-white' : 'text-white/75 hover:text-white'}`;

export function Layout() {
  const status = useMarketStatus();
  const session = useSession();
  const me = useMe();
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
          <nav aria-label="Main" className="-mx-1 flex max-w-full items-center gap-1 overflow-x-auto">
            {NAV.map(([to, label]) => (
              <NavLink key={to} to={to} end={to === '/'} className={navClass}>
                {label}
              </NavLink>
            ))}
            {session.data?.hasStoreFinder && (
              <NavLink to="/available-in-stores" className={navClass}>
                Stores
              </NavLink>
            )}
          </nav>
          <SearchForm className="order-last w-full lg:order-none lg:ml-auto lg:w-64" />
          <div className="flex items-center gap-2">
            {session.data?.isPro ? (
              <span className="rounded-full bg-pokemon-yellow px-2.5 py-1 text-[11px] font-extrabold text-pokemon-pokeblue">PRO</span>
            ) : (
              <Link to="/pro" className="rounded-full bg-pokemon-yellow px-3 py-1.5 text-xs font-extrabold text-pokemon-pokeblue">
                Get Pro
              </Link>
            )}
            <NavLink to="/account" className={navClass}>
              {me.data?.signedIn ? 'Account' : 'Sign in'}
            </NavLink>
          </div>
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
            Market data, not financial advice. Not affiliated with Nintendo, The Pokémon Company or TCGplayer.
          </p>
          <nav aria-label="More" className="flex flex-wrap gap-4 font-semibold">
            <Link to="/signals" className="hover:text-slate-900">Signals</Link>
            <Link to="/outlook" className="hover:text-slate-900">Outlook</Link>
            <Link to="/master-sets" className="hover:text-slate-900">Master Sets</Link>
            <Link to="/pro" className="hover:text-slate-900">Pricing</Link>
            <Link to="/about" className="hover:text-slate-900">Methodology</Link>
            <a href="https://github.com/poker-kid-100717/tcg" className="hover:text-slate-900">Source</a>
          </nav>
        </div>
      </footer>
    </div>
  );
}
