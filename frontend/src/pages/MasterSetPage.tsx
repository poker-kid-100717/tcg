import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { useAddWatch, useDeleteMasterSet, useMasterSet, useMasterSetAdvisor, useSession, useUpdateMasterSetItem } from '../api/hooks';
import type { MasterSetItem } from '../api/types';
import { ErrorState, Loading } from '../components/States';
import { formatPrice } from '../lib/format';

type Filter = 'missing' | 'all' | 'owned';

export default function MasterSetPage() {
  const id = Number(useParams().id ?? 0);
  const set = useMasterSet(id);
  const update = useUpdateMasterSetItem();
  const remove = useDeleteMasterSet();
  const watch = useAddWatch();
  const advisor = useMasterSetAdvisor();
  const session = useSession();
  const navigate = useNavigate();
  const [filter, setFilter] = useState<Filter>('missing');

  const items = useMemo(() => {
    if (!set.data) return [];
    return set.data.items.filter((item) => filter === 'all' || (filter === 'owned' ? item.ownedQuantity > 0 : item.ownedQuantity === 0));
  }, [set.data, filter]);

  if (set.isPending) return <Loading label="Loading master set…" />;
  if (set.error) return <ErrorState error={set.error} onRetry={() => set.refetch()} />;

  const data = set.data;
  const mark = (item: MasterSetItem, owned: boolean) =>
    update.mutate({
      id,
      input: {
        cardId: item.cardId,
        variant: item.variant,
        ownedQuantity: owned ? 1 : 0,
        condition: owned ? (item.condition ?? 'NM') : null,
        acquiredPrice: owned ? item.acquiredPrice : null,
      },
    });

  const watchMissing = (item: MasterSetItem) =>
    watch.mutate({
      cardId: item.cardId,
      variant: item.variant,
      cardName: item.cardName,
      setName: data.summary.setName,
      imageUrl: item.imageUrl,
      targetBelow: item.currentMarketPrice ? Math.round(item.currentMarketPrice * 0.9 * 100) / 100 : null,
      targetAbove: null,
      movePercent: 10,
    });

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <nav className="text-sm text-slate-500"><Link to="/master-sets" className="hover:underline">Master Sets</Link> / {data.summary.setName}</nav>

      <header className="grid gap-5 lg:grid-cols-[1fr_auto] lg:items-end">
        <div className="grid gap-2">
          <p className="eyebrow">{data.summary.setSeries}</p>
          <h1 className="text-3xl sm:text-4xl">{data.summary.setName}</h1>
          <p className="text-slate-600">
            {data.summary.ownedPrintings} of {data.summary.requiredPrintings} required printings owned · {data.summary.completionPercent.toFixed(1)}% complete
          </p>
          <div className="h-3 max-w-2xl overflow-hidden rounded-full bg-slate-100">
            <div className="h-full bg-pokemon-pokeblue" style={{ width: `${Math.min(100, data.summary.completionPercent)}%` }} />
          </div>
        </div>
        <button
          type="button"
          className="btn-ghost text-sm text-red-700"
          disabled={remove.isPending}
          onClick={async () => {
            if (!window.confirm('Delete this master set tracker?')) return;
            await remove.mutateAsync(id);
            navigate('/master-sets');
          }}
        >
          Delete tracker
        </button>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        <Stat label="Owned market value" value={formatPrice(data.summary.ownedMarketValue)} />
        <Stat label="Estimated cost to finish" value={formatPrice(data.summary.missingMarketCost)} />
        <Stat label="Missing printings" value={String(data.summary.requiredPrintings - data.summary.ownedPrintings)} />
      </div>

      <section className="panel grid gap-4 p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="eyebrow">Cloudflare Workers AI</p>
            <h2 className="text-xl">AI Set Advisor</h2>
            <p className="text-sm text-slate-600">Uses your actual missing-card data and deterministic market signals; it never invents card prices.</p>
          </div>
          {session.data?.isPro ? (
            <button type="button" className="btn bg-pokemon-pokeblue text-white" disabled={advisor.isPending} onClick={() => advisor.mutate(id)}>
              {advisor.isPending ? 'Analyzing…' : 'Ask AI Set Advisor'}
            </button>
          ) : (
            <Link to="/pro" className="btn bg-pokemon-pokeblue text-white">Unlock AI with Pro</Link>
          )}
        </div>
        {advisor.data && (
          <div className="rounded-xl bg-slate-50 p-4">
            <p className="whitespace-pre-wrap text-sm leading-6 text-slate-700">{advisor.data.text}</p>
            <p className="mt-3 text-xs text-slate-500">
              {advisor.data.generatedByAi ? `${advisor.data.provider} · ${advisor.data.model}` : 'Deterministic fallback'}
            </p>
          </div>
        )}
        {advisor.error && <p className="text-sm text-red-700">{advisor.error.message}</p>}
      </section>

      <section className="grid gap-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="text-2xl">Checklist</h2>
            <p className="text-sm text-slate-500">Every known card/printing variant for this set.</p>
          </div>
          <div className="flex rounded-lg bg-slate-100 p-1">
            {(['missing', 'all', 'owned'] as Filter[]).map((value) => (
              <button key={value} type="button" onClick={() => setFilter(value)}
                className={`rounded-md px-3 py-1.5 text-sm font-semibold capitalize ${filter === value ? 'bg-white shadow-sm' : 'text-slate-600'}`}>
                {value}
              </button>
            ))}
          </div>
        </div>

        <div className="grid gap-3">
          {items.map((item) => (
            <article key={`${item.cardId}:${item.variant}`} className="panel grid gap-4 p-4 sm:grid-cols-[64px_1fr_auto] sm:items-center">
              {item.imageUrl ? <img src={item.imageUrl} alt="" className="h-20 w-14 rounded object-cover" /> : <div className="h-20 w-14 rounded bg-slate-100" />}
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <Link to={`/cards/${item.cardId}`} className="font-bold text-slate-900 hover:text-pokemon-pokeblue">{item.cardName}</Link>
                  <span className="text-xs text-slate-500">#{item.cardNumber} · {item.variantLabel}</span>
                  <span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${item.signal === 'Buy candidate' ? 'bg-emerald-50 text-emerald-800' : item.signal === 'Watch' ? 'bg-amber-50 text-amber-800' : 'bg-slate-100 text-slate-700'}`}>
                    {item.signal}
                  </span>
                </div>
                <p className="mt-1 text-sm text-slate-600">{item.signalReason}</p>
                <p className="mt-1 text-sm font-semibold">
                  {formatPrice(item.currentMarketPrice)}
                  {item.change30Percent !== null && <span className={`ml-2 ${item.change30Percent >= 0 ? 'gain' : 'loss'}`}>{item.change30Percent >= 0 ? '+' : ''}{item.change30Percent.toFixed(1)}% / 30d</span>}
                </p>
              </div>
              <div className="flex flex-wrap gap-2 sm:justify-end">
                <button type="button" className="btn-ghost text-sm" disabled={update.isPending} onClick={() => mark(item, item.ownedQuantity === 0)}>
                  {item.ownedQuantity > 0 ? '✓ Owned' : 'Mark owned'}
                </button>
                {item.ownedQuantity === 0 && item.currentMarketPrice !== null && (
                  <button type="button" className="btn-ghost text-sm" disabled={watch.isPending} onClick={() => watchMissing(item)}>Watch −10%</button>
                )}
                {item.ownedQuantity === 0 && item.tcgplayerUrl && (
                  <a href={item.tcgplayerUrl} target="_blank" rel="noopener noreferrer" className="btn bg-pokemon-pokeblue text-sm text-white">Buy on TCGplayer ↗</a>
                )}
              </div>
            </article>
          ))}
          {items.length === 0 && <p className="panel p-8 text-center text-slate-500">No cards match this view.</p>}
        </div>
      </section>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return <div className="panel p-5"><p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p><p className="mt-1 text-3xl font-bold">{value}</p></div>;
}
