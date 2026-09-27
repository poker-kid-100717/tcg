import { Link } from 'react-router-dom';

import { useDashboard, useMe, useReadAlert, useReadAllAlerts } from '../api/hooks';
import { FreshnessChip, ProBadge, ProLock, SignInPrompt } from '../components/Gates';
import { SIGNAL_LABELS, SignalRow } from '../components/SignalRow';
import { ErrorState, Loading } from '../components/States';
import { formatDate, formatPrice } from '../lib/format';

const pct = (value: number | null) => (value === null ? '—' : `${value > 0 ? '+' : ''}${value.toFixed(1)}%`);

export default function DashboardPage() {
  const me = useMe();
  const signedIn = Boolean(me.data?.signedIn);
  const dashboard = useDashboard(signedIn);
  const read = useReadAlert();
  const readAll = useReadAllAlerts();

  if (me.isPending) return <Loading label="Loading your dashboard…" />;
  if (!signedIn) return <SignInPrompt title="Your market dashboard" detail="Sign in to see your watchlist movers, alerts, and the signals on the cards you follow." />;
  if (dashboard.error) return <ErrorState error={dashboard.error} onRetry={() => dashboard.refetch()} />;
  if (!dashboard.data) return <Loading label="Loading your dashboard…" />;

  const data = dashboard.data;
  const trackedValue = data.watchlist.reduce((sum, item) => sum + (item.currentPrice ?? 0), 0);

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="grid gap-2">
          <p className="eyebrow">TCG Signal</p>
          <h1 className="text-3xl sm:text-4xl">Your market dashboard</h1>
          <p className="max-w-2xl text-slate-600">The cards and rules you care about, instead of a generic market feed.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <FreshnessChip freshness={data.market?.freshness} />
          <span className="rounded-full bg-white px-3 py-1.5 text-sm font-semibold text-slate-700 ring-1 ring-slate-200">
            {data.account.isPro ? 'Pro' : data.account.hasStoreFinder ? 'Store Finder' : 'Free'}
          </span>
        </div>
      </header>

      {data.account.paymentIssue && (
        <p role="alert" className="rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-900">
          Your last payment didn’t go through. <Link to="/account" className="font-semibold underline">Update billing</Link> to keep Pro.
        </p>
      )}

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Stat label="Watched printings" value={String(data.watchlist.length)} />
        <Stat label="Unread alerts" value={String(data.unreadAlerts)} />
        <Stat label="Targets hit · 7 days" value={String(data.targetsHit7Days)} />
        <Stat label="Tracked market total" value={formatPrice(trackedValue)} note="One copy of each watched printing." />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <section className="panel overflow-hidden">
          <div className="border-b border-slate-200 px-5 py-4">
            <h2 className="text-lg">Watchlist movers</h2>
            <p className="text-xs text-slate-500">Largest 7-day moves in market reference among your watched printings.</p>
          </div>
          {data.movers.length === 0 ? (
            <p className="p-5 text-sm text-slate-600">Add cards to your watchlist to build a personal market view.</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {data.movers.map((m) => (
                <li key={m.watchlistItemId} className="flex items-center justify-between gap-3 p-4">
                  <div className="min-w-0">
                    <Link to={`/cards/${m.cardId}`} className="font-semibold text-slate-900 hover:text-pokemon-pokeblue">{m.cardName}</Link>
                    <p className="text-xs text-slate-500">{formatPrice(m.market)} · 30d {pct(m.change30Percent)}</p>
                  </div>
                  <span className={`font-bold tabular-nums ${(m.change7Percent ?? 0) >= 0 ? 'gain' : 'loss'}`}>{pct(m.change7Percent)}</span>
                </li>
              ))}
            </ul>
          )}
          <div className="border-t border-slate-200 p-4">
            <Link to="/watchlist" className="text-sm font-semibold text-pokemon-pokeblue hover:underline">Manage watchlist →</Link>
          </div>
        </section>

        {data.signalsLocked ? (
          <ProLock
            compact
            title="New signals on your cards"
            detail="See when a card you watch starts a sustained downtrend, makes an unusual move, hits a new 30-day high or low, or looks like a sleeper — each with the rule and the numbers behind it."
          />
        ) : (
          <section className="panel overflow-hidden">
            <div className="flex items-center gap-2 border-b border-slate-200 px-5 py-4">
              <h2 className="text-lg">New signals on your cards</h2>
              <ProBadge />
            </div>
            {data.watchedSignals.length === 0 ? (
              <p className="p-5 text-sm text-slate-600">No signals on your watched printings today.</p>
            ) : (
              <ul className="divide-y divide-slate-100">{data.watchedSignals.map((s) => <SignalRow key={`${s.cardId}-${s.variant}-${s.kind}`} signal={s} />)}</ul>
            )}
            <div className="border-t border-slate-200 p-4">
              <Link to="/signals" className="text-sm font-semibold text-pokemon-pokeblue hover:underline">Open the signal center →</Link>
            </div>
          </section>
        )}
      </div>

      <section className="panel overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 px-5 py-4">
          <div>
            <h2 className="text-lg">Alerts</h2>
            <p className="text-xs text-slate-500">Created only when one of your rules becomes true.</p>
          </div>
          {data.unreadAlerts > 0 && (
            <button type="button" className="btn-ghost text-sm" disabled={readAll.isPending} onClick={() => readAll.mutate()}>
              Mark all read
            </button>
          )}
        </div>
        {data.alerts.length === 0 ? (
          <p className="p-5 text-sm text-slate-600">
            {data.account.isPro ? 'No alerts yet. Set rules on your watchlist; each daily price update checks them.' : 'Alerts are part of Pro.'}
          </p>
        ) : (
          <ul className="divide-y divide-slate-100">
            {data.alerts.map((alert) => (
              <li key={alert.id} className={`grid gap-1 p-4 ${alert.readAt ? 'bg-white' : 'bg-blue-50/50'}`}>
                <div className="flex items-start justify-between gap-3">
                  <p className="text-sm font-semibold text-slate-900">{alert.message}</p>
                  {!alert.readAt && (
                    <button type="button" className="shrink-0 text-xs font-semibold text-pokemon-pokeblue hover:underline" onClick={() => read.mutate(alert.id)}>
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

      {data.market && (
        <section className="panel grid gap-3 p-5">
          <h2 className="text-lg">Market today</h2>
          <p className="text-sm text-slate-600">
            {data.market.printingsPriced.toLocaleString()} printings priced · {data.market.up7.toLocaleString()} up and {data.market.down7.toLocaleString()} down
            more than 5% over 7 days (cards $2 and up).
          </p>
          {Object.keys(data.market.signalCounts).length > 0 && (
            <ul className="flex flex-wrap gap-2 text-xs">
              {Object.entries(data.market.signalCounts).map(([kind, count]) => (
                <li key={kind} className="rounded-full bg-slate-100 px-2.5 py-1 font-semibold text-slate-700">{SIGNAL_LABELS[kind] ?? kind}: {count}</li>
              ))}
            </ul>
          )}
        </section>
      )}

      <section className="grid gap-4 sm:grid-cols-3">
        <Link to="/deal" className="panel grid gap-2 p-5 hover:border-pokemon-blue">
          <p className="eyebrow">At a card show?</p>
          <h2 className="text-lg">Analyze a deal</h2>
          <p className="text-sm text-slate-600">Compare the asking price with the market reference and your real costs.</p>
        </Link>
        <Link to="/signals" className="panel grid gap-2 p-5 hover:border-pokemon-blue">
          <p className="eyebrow">Signal center</p>
          <h2 className="text-lg">Today’s signals</h2>
          <p className="text-sm text-slate-600">Momentum, unusual moves, new highs and lows, sleepers and downtrends.</p>
        </Link>
        <Link to="/master-sets" className="panel grid gap-2 p-5 hover:border-pokemon-blue">
          <p className="eyebrow">Collecting</p>
          <h2 className="text-lg">Master sets</h2>
          <p className="text-sm text-slate-600">Track completion and what the rest would cost.</p>
        </Link>
      </section>
    </div>
  );
}

function Stat({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="panel p-4 sm:p-5">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-2xl font-bold tabular-nums text-slate-900 sm:text-3xl">{value}</p>
      {note && <p className="mt-1 text-xs text-slate-500">{note}</p>}
    </div>
  );
}
