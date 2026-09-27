import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useAnalyzeDeal, useCard, useDealPresets, useMe, useSearch } from '../api/hooks';
import type { CardSummary, DealAnalysis } from '../api/types';
import { FreshnessChip, ProBadge, ProLock } from '../components/Gates';
import { Loading } from '../components/States';
import { formatPrice } from '../lib/format';

const money = (value: number) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
const optional = (value: string) => (value.trim() === '' ? null : Math.max(0, Number(value)));

export default function DealAnalyzerPage() {
  const me = useMe();
  const isPro = Boolean(me.data?.account?.isPro);

  if (me.isPending) return <Loading label="Loading Deal Analyzer…" />;

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="grid gap-2">
        <p className="eyebrow flex items-center gap-2">Deal Analyzer <ProBadge /></p>
        <h1 className="text-3xl sm:text-4xl">Is this a good deal?</h1>
        <p className="max-w-3xl text-slate-600">
          Put the asking price next to the market reference, then include what it really costs to buy and sell. The result is
          deal math on your numbers — not a promise of what the card will sell for.
        </p>
      </header>
      {isPro ? (
        <Analyzer />
      ) : (
        <ProLock
          title="Deal Analyzer is a Pro tool"
          detail="Built for card shows and marketplace listings: compare an asking price with the market reference and see your net after fees and shipping."
          points={['Asking price vs market reference, with data confidence and freshness', 'Fee presets for common marketplaces, or your own', 'Break-even sale price and net after every entered cost', 'A summary you can copy or share']}
        />
      )}
    </div>
  );
}

function Analyzer() {
  const [params, setParams] = useSearchParams();
  const [query, setQuery] = useState('');
  const search = useSearch(query, 1);
  const [selected, setSelected] = useState<CardSummary | null>(null);
  const selectedId = selected?.id ?? params.get('card') ?? '';
  const card = useCard(selectedId);
  const presets = useDealPresets(true);
  const analyze = useAnalyzeDeal();

  const [variant, setVariant] = useState('');
  const [asking, setAsking] = useState('');
  const [inbound, setInbound] = useState('');
  const [tax, setTax] = useState('');
  const [outbound, setOutbound] = useState('5');
  const [presetId, setPresetId] = useState('');
  const [customPercent, setCustomPercent] = useState('13');
  const [customFixed, setCustomFixed] = useState('0');
  const [sale, setSale] = useState('');

  useEffect(() => {
    const first = card.data?.prices.find((p) => p.market !== null) ?? card.data?.prices[0];
    setVariant(first?.variant ?? '');
    analyze.reset();
  }, [card.data?.id]);

  useEffect(() => {
    if (!presetId && presets.data?.length) setPresetId(presets.data[0].id);
  }, [presets.data, presetId]);

  const results = useMemo(() => search.data?.cards.slice(0, 6) ?? [], [search.data]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!card.data || !variant || asking.trim() === '') return;
    analyze.mutate({
      cardId: card.data.id,
      variant,
      askingPrice: Math.max(0, Number(asking)),
      inboundShipping: optional(inbound),
      tax: optional(tax),
      outboundShipping: optional(outbound),
      presetId,
      customPercentFee: presetId === 'custom' ? optional(customPercent) : null,
      customFixedFee: presetId === 'custom' ? optional(customFixed) : null,
      expectedSalePrice: optional(sale),
    });
  };

  return (
    <>
      <section className="panel grid gap-4 p-5">
        <label className="grid gap-1 text-sm font-semibold">
          Find a card
          <input className="input" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Pikachu, Charizard, Umbreon…" />
        </label>
        {query.trim().length >= 2 && results.length > 0 && (
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {results.map((result) => (
              <button
                type="button"
                key={result.id}
                onClick={() => { setSelected(result); setQuery(''); setParams({ card: result.id }, { replace: true }); }}
                className="flex items-center gap-3 rounded-lg border border-slate-200 p-2 text-left hover:border-pokemon-blue"
              >
                {result.imageUrl && <img src={result.imageUrl} alt="" className="h-16 w-12 rounded object-cover" />}
                <span className="min-w-0">
                  <span className="block font-semibold text-slate-900">{result.name}</span>
                  <span className="block text-xs text-slate-500">{result.setName} · #{result.number}</span>
                  <span className="block text-sm font-semibold">{formatPrice(result.marketPrice)}</span>
                </span>
              </button>
            ))}
          </div>
        )}
      </section>

      {selectedId && card.isPending && <Loading label="Loading card pricing…" />}

      {card.data && (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(320px,0.8fr)]">
          <form onSubmit={submit} className="panel grid content-start gap-5 p-5">
            <div className="flex items-start gap-4">
              {card.data.imageUrl && <img src={card.data.imageUrl} alt="" className="h-28 w-20 rounded-lg object-cover" />}
              <div className="min-w-0">
                <h2 className="text-xl">{card.data.name}</h2>
                <p className="text-sm text-slate-500">{card.data.set.name} · #{card.data.number}</p>
                <Link to={`/cards/${card.data.id}`} className="mt-2 inline-block text-sm font-semibold text-pokemon-pokeblue hover:underline">
                  Open card page →
                </Link>
              </div>
            </div>

            <label className="grid gap-1 text-sm font-semibold">
              Printing
              <select className="input" value={variant} onChange={(e) => setVariant(e.target.value)}>
                {card.data.prices.map((p) => <option key={p.variant} value={p.variant}>{p.label} · {formatPrice(p.market)}</option>)}
              </select>
            </label>

            <div className="grid gap-4 sm:grid-cols-2">
              <MoneyInput label="Asking price" value={asking} onChange={setAsking} required />
              <MoneyInput label="Shipping to you" value={inbound} onChange={setInbound} />
              <MoneyInput label="Tax / other buying cost" value={tax} onChange={setTax} />
              <MoneyInput label="Your shipping when you sell" value={outbound} onChange={setOutbound} />
              <MoneyInput label="Expected sale price (optional)" value={sale} onChange={setSale} placeholder="Market reference" />
              <label className="grid gap-1 text-sm font-semibold">
                Selling fees
                <select className="input" value={presetId} onChange={(e) => setPresetId(e.target.value)}>
                  {presets.data?.map((p) => (
                    <option key={p.id} value={p.id}>{p.name} · {p.percentFee}%{p.fixedFee ? ` + ${money(p.fixedFee)}` : ''}</option>
                  ))}
                  <option value="custom">Custom</option>
                </select>
              </label>
              {presetId === 'custom' && (
                <>
                  <label className="grid gap-1 text-sm font-semibold">
                    Custom fee %
                    <input className="input" type="number" min="0" max="100" step="0.01" inputMode="decimal" value={customPercent} onChange={(e) => setCustomPercent(e.target.value)} />
                  </label>
                  <MoneyInput label="Custom fixed fee per sale" value={customFixed} onChange={setCustomFixed} />
                </>
              )}
            </div>
            <p className="text-xs text-slate-500">
              {presets.data?.find((p) => p.id === presetId)?.note ?? 'Your own fee estimate.'} Fee presets are estimates; check the platform’s current fees before you sell.
            </p>
            <button type="submit" className="btn w-fit bg-pokemon-pokeblue text-white" disabled={analyze.isPending || asking.trim() === ''}>
              {analyze.isPending ? 'Analyzing…' : 'Analyze deal'}
            </button>
            {analyze.error && <p className="text-sm text-red-700">{analyze.error.message}</p>}
          </form>

          <aside className="lg:sticky lg:top-4 lg:self-start" aria-live="polite">
            {analyze.data ? <Result deal={analyze.data} /> : (
              <div className="panel p-5 text-sm text-slate-600">Enter the asking price and your costs, then analyze.</div>
            )}
          </aside>
        </div>
      )}
    </>
  );
}

function Result({ deal }: { deal: DealAnalysis }) {
  const [copied, setCopied] = useState(false);
  const share = async () => {
    try {
      if (navigator.share) await navigator.share({ title: 'TCG Signal deal check', text: deal.summary });
      else {
        await navigator.clipboard.writeText(deal.summary);
        setCopied(true);
      }
    } catch {
      // The user closed the share sheet.
    }
  };
  const m = deal.math;

  return (
    <div className="panel grid gap-4 p-5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Market reference</p>
          <p className="price text-3xl">{formatPrice(deal.marketReference)}</p>
          <p className="text-xs text-slate-500">{deal.referenceSource} · {deal.confidenceBand} confidence{deal.confidenceScore !== null ? ` (${deal.confidenceScore}/100)` : ''}</p>
        </div>
        <FreshnessChip freshness={deal.freshness} />
      </div>
      <p className={`rounded-lg px-3 py-2 text-sm font-semibold ${deal.askVsMarketPercent !== null && deal.askVsMarketPercent <= -10 ? 'bg-emerald-50 text-emerald-800' : deal.askVsMarketPercent !== null && deal.askVsMarketPercent >= 10 ? 'bg-red-50 text-red-800' : 'bg-slate-50 text-slate-700'}`}>
        {deal.position}{deal.askVsMarketPercent !== null ? ` · asking is ${Math.abs(deal.askVsMarketPercent).toFixed(1)}% ${deal.askVsMarketPercent <= 0 ? 'below' : 'above'}` : ''}
      </p>
      <dl className="grid gap-2 text-sm">
        <Line label="All-in cost to buy" value={money(m.acquisition)} />
        <Line label="Sale price used" value={money(m.expectedSale)} />
        <Line label={`Selling fees (${deal.fees.name})`} value={`−${money(m.sellingFees)}`} />
        <Line label="Your shipping" value={`−${money(m.outboundShipping)}`} />
        <Line label="Proceeds" value={money(m.proceeds)} />
        <div className="mt-2 border-t border-slate-200 pt-3">
          <Line label="Net after entered costs" value={money(m.net)} strong tone={m.net >= 0 ? 'gain' : 'loss'} />
        </div>
        {m.netMarginPercent !== null && <Line label="Return on cost" value={`${m.netMarginPercent.toFixed(1)}%`} />}
        <Line label="Break-even sale price" value={m.breakEvenSalePrice === null ? '—' : money(m.breakEvenSalePrice)} />
      </dl>
      {deal.warnings.length > 0 && (
        <ul className="grid gap-1 rounded-lg bg-amber-50 p-3 text-xs text-amber-900">
          {deal.warnings.map((w) => <li key={w}>• {w}</li>)}
        </ul>
      )}
      <div className="grid gap-2 rounded-lg border border-slate-200 p-3">
        <p className="text-xs text-slate-600">{deal.summary}</p>
        <button type="button" className="btn-ghost w-fit text-sm" onClick={share}>{copied ? 'Copied' : 'Share summary'}</button>
      </div>
    </div>
  );
}

function MoneyInput({ label, value, onChange, placeholder = '0.00', required = false }: {
  label: string; value: string; onChange: (v: string) => void; placeholder?: string; required?: boolean;
}) {
  return (
    <label className="grid gap-1 text-sm font-semibold">
      {label}
      <div className="relative">
        <span className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400">$</span>
        <input className="input pl-7" type="number" min="0" step="0.01" inputMode="decimal" required={required} value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
      </div>
    </label>
  );
}

function Line({ label, value, strong = false, tone = '' }: { label: string; value: string; strong?: boolean; tone?: string }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <dt className={strong ? 'font-semibold text-slate-900' : 'text-slate-500'}>{label}</dt>
      <dd className={`tabular-nums ${strong ? 'text-lg font-bold' : 'font-semibold'} ${tone}`}>{value}</dd>
    </div>
  );
}
