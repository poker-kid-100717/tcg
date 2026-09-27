import { useState } from 'react';
import { Link } from 'react-router-dom';

import { useDeleteWatch, useSession, useUpdateWatch, useWatchlist } from '../api/hooks';
import type { WatchlistItem } from '../api/types';
import { ErrorState, Loading } from '../components/States';
import { formatPrice } from '../lib/format';

export default function WatchlistPage() {
  const session = useSession();
  const watchlist = useWatchlist();

  if (session.isPending || watchlist.isPending) return <Loading label="Loading watchlist…" />;
  if (watchlist.error) return <ErrorState error={watchlist.error} onRetry={() => watchlist.refetch()} />;

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="grid gap-2">
          <p className="eyebrow">Personal market</p>
          <h1 className="text-3xl sm:text-4xl">Watchlist</h1>
          <p className="max-w-2xl text-slate-600">
            Track the exact printing you care about and trigger alerts when the market crosses your price or movement thresholds.
          </p>
        </div>
        {!session.data?.isPro && <Link to="/pro" className="btn bg-pokemon-yellow text-pokemon-pokeblue">Unlock unlimited watches</Link>}
      </header>

      {watchlist.data.length === 0 ? (
        <div className="panel grid gap-3 p-8 text-center">
          <h2 className="text-xl">Nothing watched yet</h2>
          <p className="text-slate-600">Open a card, choose the printing you care about, and tap “Watch price.”</p>
          <Link to="/search?q=Pikachu" className="btn mx-auto bg-pokemon-pokeblue text-white">Find a card</Link>
        </div>
      ) : (
        <div className="grid gap-4">
          {watchlist.data.map((item) => <WatchRow key={item.id} item={item} />)}
        </div>
      )}
    </div>
  );
}

function WatchRow({ item }: { item: WatchlistItem }) {
  const update = useUpdateWatch();
  const remove = useDeleteWatch();
  const [below, setBelow] = useState(item.targetBelow?.toString() ?? '');
  const [above, setAbove] = useState(item.targetAbove?.toString() ?? '');
  const [move, setMove] = useState(item.movePercent?.toString() ?? '10');
  const change = item.currentPrice && item.baselinePrice ? (item.currentPrice / item.baselinePrice - 1) * 100 : null;
  const parse = (value: string) => value.trim() === '' ? null : Number(value);

  return (
    <article className="panel grid gap-4 p-4 sm:grid-cols-[72px_1fr]">
      <Link to={`/cards/${item.cardId}`} className="mx-auto sm:mx-0">
        {item.imageUrl ? <img src={item.imageUrl} alt="" className="h-24 w-[68px] rounded-lg object-cover" /> : <div className="h-24 w-[68px] rounded-lg bg-slate-200" />}
      </Link>
      <div className="grid gap-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <Link to={`/cards/${item.cardId}`} className="font-bold text-slate-900 hover:text-pokemon-pokeblue">{item.cardName}</Link>
            <p className="text-sm text-slate-500">{item.setName} · {item.variantLabel}</p>
          </div>
          <div className="text-right">
            <p className="price text-xl">{formatPrice(item.currentPrice)}</p>
            {change !== null && <p className={`text-xs font-semibold ${change >= 0 ? 'gain' : 'loss'}`}>{change >= 0 ? '+' : ''}{change.toFixed(1)}% since watched</p>}
          </div>
        </div>

        <div className="grid gap-3 sm:grid-cols-3">
          <SmallInput label="Alert below $" value={below} onChange={setBelow} />
          <SmallInput label="Alert above $" value={above} onChange={setAbove} />
          <SmallInput label="Alert on move %" value={move} onChange={setMove} />
        </div>

        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            className="btn bg-pokemon-pokeblue text-white"
            disabled={update.isPending}
            onClick={() => update.mutate({ id: item.id, input: { targetBelow: parse(below), targetAbove: parse(above), movePercent: parse(move), enabled: true } })}
          >
            {update.isPending ? 'Saving…' : 'Save alerts'}
          </button>
          <button type="button" className="btn-ghost" disabled={remove.isPending} onClick={() => remove.mutate(item.id)}>Remove</button>
        </div>
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
