import { useState } from 'react';
import { Link } from 'react-router-dom';

import { useDeleteWatch, useMe, useUpdateWatch, useWatchlist } from '../api/hooks';
import type { WatchlistItem } from '../api/types';
import { ProBadge, SignInPrompt } from '../components/Gates';
import { ErrorState, Loading } from '../components/States';
import { formatPrice } from '../lib/format';

export const FREE_WATCHLIST_LIMIT = 3;

export default function WatchlistPage() {
  const me = useMe();
  const signedIn = Boolean(me.data?.signedIn);
  const watchlist = useWatchlist(signedIn);

  if (me.isPending) return <Loading label="Loading watchlist…" />;
  if (!signedIn) {
    return <SignInPrompt title="Your watchlist" detail="Sign in to track the exact printings you care about. Free accounts watch up to 3 cards; Pro adds unlimited watches and alerts." />;
  }
  if (watchlist.error) return <ErrorState error={watchlist.error} onRetry={() => watchlist.refetch()} />;
  if (!watchlist.data) return <Loading label="Loading watchlist…" />;

  const isPro = Boolean(me.data?.account?.isPro);
  const items = watchlist.data;

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="grid gap-2">
          <p className="eyebrow">Personal market</p>
          <h1 className="text-3xl sm:text-4xl">Watchlist</h1>
          <p className="max-w-2xl text-slate-600">
            Track the exact printing you care about{isPro ? ' and get alerted when the market crosses your price, movement or signal rules.' : '.'}
          </p>
        </div>
        {!isPro && (
          <div className="grid gap-1 sm:text-right">
            <p className="text-sm font-semibold text-slate-700">{items.length} of {FREE_WATCHLIST_LIMIT} free watches used</p>
            <Link to="/pro" className="btn bg-pokemon-yellow text-pokemon-pokeblue">Unlimited watches and alerts</Link>
          </div>
        )}
      </header>

      {items.length === 0 ? (
        <div className="panel grid gap-3 p-8 text-center">
          <h2 className="text-xl">Nothing watched yet</h2>
          <p className="text-slate-600">Open a card, choose the printing you care about, and tap “Watch price.”</p>
          <Link to="/search?q=Pikachu" className="btn mx-auto bg-pokemon-pokeblue text-white">Find a card</Link>
        </div>
      ) : (
        <div className="grid gap-4">
          {items.map((item) => <WatchRow key={item.id} item={item} isPro={isPro} />)}
        </div>
      )}
    </div>
  );
}

function WatchRow({ item, isPro }: { item: WatchlistItem; isPro: boolean }) {
  const update = useUpdateWatch();
  const remove = useDeleteWatch();
  const [below, setBelow] = useState(item.targetBelow?.toString() ?? '');
  const [above, setAbove] = useState(item.targetAbove?.toString() ?? '');
  const [move, setMove] = useState(item.movePercent?.toString() ?? '');
  const [sleeper, setSleeper] = useState(item.alertSleeper);
  const [trendingDown, setTrendingDown] = useState(item.alertTrendingDown);
  const [unusual, setUnusual] = useState(item.alertUnusualMove);
  const [saved, setSaved] = useState(false);
  const change = item.currentPrice && item.baselinePrice ? (item.currentPrice / item.baselinePrice - 1) * 100 : null;
  const parse = (value: string) => (value.trim() === '' ? null : Number(value));

  const save = () => {
    setSaved(false);
    update.mutate(
      {
        id: item.id,
        input: {
          targetBelow: parse(below), targetAbove: parse(above), movePercent: parse(move), enabled: true,
          alertSleeper: sleeper, alertTrendingDown: trendingDown, alertUnusualMove: unusual,
        },
      },
      { onSuccess: () => setSaved(true) },
    );
  };

  return (
    <article className="panel grid gap-4 p-4 sm:grid-cols-[72px_1fr]">
      <Link to={`/cards/${item.cardId}`} className="mx-auto sm:mx-0">
        {item.imageUrl ? <img src={item.imageUrl} alt="" className="h-24 w-[68px] rounded-lg object-cover" /> : <div className="h-24 w-[68px] rounded-lg bg-slate-200" />}
      </Link>
      <div className="grid min-w-0 gap-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <Link to={`/cards/${item.cardId}`} className="font-bold text-slate-900 hover:text-pokemon-pokeblue">{item.cardName}</Link>
            <p className="text-sm text-slate-500">{item.setName} · {item.variantLabel}</p>
          </div>
          <div className="text-right">
            <p className="price text-xl">{formatPrice(item.currentPrice)}</p>
            {change !== null && <p className={`text-xs font-semibold ${change >= 0 ? 'gain' : 'loss'}`}>{change >= 0 ? '+' : ''}{change.toFixed(1)}% since watched</p>}
          </div>
        </div>

        {isPro ? (
          <>
            <div className="grid gap-3 sm:grid-cols-3">
              <SmallInput label="Alert at or below $" value={below} onChange={setBelow} />
              <SmallInput label="Alert at or above $" value={above} onChange={setAbove} />
              <SmallInput label="Alert on move % (since watched)" value={move} onChange={setMove} />
            </div>
            <fieldset className="grid gap-2 sm:grid-cols-3">
              <legend className="mb-1 text-xs font-semibold text-slate-600">Signal alerts</legend>
              <Toggle label="Looks like a sleeper" checked={sleeper} onChange={setSleeper} />
              <Toggle label="Sustained downtrend" checked={trendingDown} onChange={setTrendingDown} />
              <Toggle label="Unusual daily move" checked={unusual} onChange={setUnusual} />
            </fieldset>
            <div className="flex flex-wrap items-center gap-2">
              <button type="button" className="btn bg-pokemon-pokeblue text-white" disabled={update.isPending} onClick={save}>
                {update.isPending ? 'Saving…' : 'Save alerts'}
              </button>
              <button type="button" className="btn-ghost" disabled={remove.isPending} onClick={() => remove.mutate(item.id)}>Remove</button>
              {saved && <span className="text-sm text-emerald-700" role="status">Saved</span>}
            </div>
            <p className="text-xs text-slate-500">Alerts are checked after each daily price update and fire once when a rule becomes true (at most once a day per rule).</p>
          </>
        ) : (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2">
            <p className="flex items-center gap-2 text-sm text-slate-600">
              <ProBadge /> Price targets, move alerts and signal alerts
            </p>
            <button type="button" className="btn-ghost" disabled={remove.isPending} onClick={() => remove.mutate(item.id)}>Remove</button>
          </div>
        )}
        {update.error && <p className="text-sm text-red-700">{update.error.message}</p>}
      </div>
    </article>
  );
}

function SmallInput({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label className="grid gap-1 text-xs font-semibold text-slate-600">
      {label}
      <input className="input" type="number" min="0" step="0.01" inputMode="decimal" value={value} onChange={(e) => onChange(e.target.value)} />
    </label>
  );
}

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="flex min-h-11 items-center gap-2 rounded-lg border border-slate-200 px-3 text-sm">
      <input type="checkbox" className="h-4 w-4" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  );
}
