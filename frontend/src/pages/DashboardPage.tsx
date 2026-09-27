import { Link } from 'react-router-dom';

import { useDashboard, useReadAlert, useReadAllAlerts } from '../api/hooks';
import { ErrorState, Loading } from '../components/States';
import { formatDate, formatPrice } from '../lib/format';

export default function DashboardPage() {
  const dashboard = useDashboard();
  const read = useReadAlert();
  const readAll = useReadAllAlerts();

  if (dashboard.isPending) return <Loading label="Loading your dashboard…" />;
  if (dashboard.error) return <ErrorState error={dashboard.error} onRetry={() => dashboard.refetch()} />;

  const data = dashboard.data;
  const trackedValue = data.watchlist.reduce((sum, item) => sum + (item.currentPrice ?? 0), 0);
  const movers = [...data.watchlist]
    .filter((item) => item.currentPrice && item.baselinePrice)
    .sort((a, b) => {
      const aa = Math.abs((a.currentPrice! / a.baselinePrice! - 1) * 100);
      const bb = Math.abs((b.currentPrice! / b.baselinePrice! - 1) * 100);
      return bb - aa;
    })
    .slice(0, 6);

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="grid gap-2">
          <p className="eyebrow">TCG Signal</p>
          <h1 className="text-3xl sm:text-4xl">Your market dashboard</h1>
          <p className="max-w-2xl text-slate-600">The cards and thresholds you care about, instead of a generic market feed.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {data.account.hasStoreFinder && (
            <Link to="/available-in-stores" className="rounded-full bg-pokemon-yellow px-3 py-1.5 text-sm font-extrabold text-pokemon-pokeblue">
              Available in Stores
            </Link>
          )}
          <span className="rounded-full bg-white px-3 py-1.5 text-sm font-semibold text-slate-700 ring-1 ring-slate-200">
            {data.account.isPro ? 'Pro' : data.account.hasStoreFinder ? 'Store Finder' : 'Free'} · {data.account.mode}
          </span>
        </div>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        <Stat label="Watched printings" value={String(data.watchlist.length)} />
        <Stat label="Unread alerts" value={String(data.unreadAlerts)} />
        <Stat label="Tracked market total" value={formatPrice(trackedValue)} note="One copy of each watched printing; not a collection valuation." />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <section className="panel overflow-hidden">
          <div className="flex items-center justify-between gap-3 border-b border-slate-200 px-5 py-4">
            <div>
              <h2 className="text-lg">Alerts</h2>
              <p className="text-xs text-slate-500">Created only when a watched threshold is crossed.</p>
            </div>
            {data.unreadAlerts > 0 && (
              <button type="button" className="btn-ghost text-sm" disabled={readAll.isPending} onClick={() => readAll.mutate()}>
                Mark all read
              </button>
            )}
          </div>
          {data.alerts.length === 0 ? (
            <p className="p-5 text-sm text-slate-600">No alert events yet. Set thresholds on your watchlist and daily snapshots will evaluate them.</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {data.alerts.map((alert) => (
                <li key={alert.id} className={`grid gap-1 p-4 ${alert.readAt ? 'bg-white' : 'bg-blue-50/50'}`}>
                  <div className="flex items-start justify-between gap-3">
                    <p className="text-sm font-semibold text-slate-900">{alert.message}</p>
                    {!alert.readAt && (
                      <button type="button" className="text-xs font-semibold text-pokemon-pokeblue hover:underline" onClick={() => read.mutate(alert.id)}>
                        Mark read
                      </button>
                    )}
                  </div>
                  <p className="text-xs text-slate-500">{formatDate(alert.createdAt, 'short')}</p>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="panel overflow-hidden">
          <div className="border-b border-slate-200 px-5 py-4">
            <h2 className="text-lg">Watchlist movers</h2>
            <p className="text-xs text-slate-500">Movement from the price recorded when you started watching.</p>
          </div>
          {movers.length === 0 ? (
            <p className="p-5 text-sm text-slate-600">Add cards to your watchlist to build a personalized market view.</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {movers.map((item) => {
                const change = (item.currentPrice! / item.baselinePrice! - 1) * 100;
                return (
                  <li key={item.id} className="flex items-center justify-between gap-3 p-4">
                    <div>
                      <Link to={`/cards/${item.cardId}`} className="font-semibold text-slate-900 hover:text-pokemon-pokeblue">{item.cardName}</Link>
                      <p className="text-xs text-slate-500">{item.variantLabel} · {formatPrice(item.currentPrice)}</p>
                    </div>
                    <span className={`font-bold tabular-nums ${change >= 0 ? 'gain' : 'loss'}`}>{change >= 0 ? '+' : ''}{change.toFixed(1)}%</span>
                  </li>
                );
              })}
            </ul>
          )}
          <div className="border-t border-slate-200 p-4">
            <Link to="/watchlist" className="text-sm font-semibold text-pokemon-pokeblue hover:underline">Manage watchlist →</Link>
          </div>
        </section>
      </div>

      <section className="grid gap-4 sm:grid-cols-3">
        <Link to="/deal" className="panel grid gap-2 p-5 hover:border-pokemon-blue">
          <p className="eyebrow">At a card show?</p>
          <h2 className="text-lg">Analyze a deal</h2>
          <p className="text-sm text-slate-600">Compare the asking price with market and estimated exit costs.</p>
        </Link>
        <Link to="/market" className="panel grid gap-2 p-5 hover:border-pokemon-blue">
          <p className="eyebrow">Market</p>
          <h2 className="text-lg">See current signals</h2>
          <p className="text-sm text-slate-600">Movers, sleepers, sustained downtrends and supply gaps.</p>
        </Link>
        <Link to="/outlook" className="panel grid gap-2 p-5 hover:border-pokemon-blue">
          <p className="eyebrow">Model</p>
          <h2 className="text-lg">30-day outlook</h2>
          <p className="text-sm text-slate-600">Predictions publish only after the model beats a no-change baseline.</p>
        </Link>
      </section>
    </div>
  );
}

function Stat({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="panel p-5">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-3xl font-bold tabular-nums text-slate-900">{value}</p>
      {note && <p className="mt-1 text-xs text-slate-500">{note}</p>}
    </div>
  );
}
