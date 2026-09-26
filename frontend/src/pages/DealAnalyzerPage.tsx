import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useCard, useSearch, useSession } from '../api/hooks';
import type { CardSummary } from '../api/types';
import { Loading } from '../components/States';
import { formatPrice } from '../lib/format';

const money = (value: number) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
const num = (value: string) => (value.trim() === '' ? 0 : Number(value));

export default function DealAnalyzerPage() {
  const session = useSession();
  const [params, setParams] = useSearchParams();
  const [query, setQuery] = useState('');
  const search = useSearch(query, 1);
  const [selected, setSelected] = useState<CardSummary | null>(null);
  const selectedId = selected?.id ?? params.get('card') ?? '';
  const card = useCard(selectedId);
  const [variant, setVariant] = useState('');
  const [purchase, setPurchase] = useState('');
  const [buyerShipping, setBuyerShipping] = useState('0');
  const [tax, setTax] = useState('0');
  const [sellingShipping, setSellingShipping] = useState('5');
  const [feeMode, setFeeMode] = useState<'online' | 'local' | 'custom'>('online');
  const [customFee, setCustomFee] = useState('13');
  const [saleOverride, setSaleOverride] = useState('');

  useEffect(() => {
    const first = card.data?.prices.find((p) => p.market !== null) ?? card.data?.prices[0];
    setVariant(first?.variant ?? '');
  }, [card.data?.id]);

  const price = card.data?.prices.find((p) => p.variant === variant);
  const market = price?.market ?? 0;
  const feePercent = feeMode === 'online' ? 13 : feeMode === 'local' ? 0 : Math.max(0, num(customFee));
  const expectedSale = saleOverride.trim() === '' ? market : Math.max(0, num(saleOverride));
  const acquisition = Math.max(0, num(purchase)) + Math.max(0, num(buyerShipping)) + Math.max(0, num(tax));
  const fees = expectedSale * feePercent / 100;
  const proceeds = expectedSale - fees - Math.max(0, num(sellingShipping));
  const net = proceeds - acquisition;
  const breakEven = feePercent >= 100 ? null : (acquisition + Math.max(0, num(sellingShipping))) / (1 - feePercent / 100);
  const marketDelta = market > 0 && num(purchase) > 0 ? (num(purchase) / market - 1) * 100 : null;

  const results = useMemo(() => search.data?.cards.slice(0, 6) ?? [], [search.data]);

  if (session.isPending) return <Loading label="Loading Deal Analyzer…" />;
  if (!session.data?.isPro) return <Upgrade />;

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="grid gap-2">
        <p className="eyebrow">TCG Signal Pro</p>
        <h1 className="text-3xl sm:text-4xl">Deal Analyzer</h1>
        <p className="max-w-3xl text-slate-600">
          Put the asking price next to the actual market reference, then include your real acquisition and selling costs.
          The result is deal math—not a promise about what the card will sell for.
        </p>
      </header>

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
                onClick={() => { setSelected(result); setQuery(result.name); setParams({ card: result.id }, { replace: true }); }}
                className="flex items-center gap-3 rounded-lg border border-slate-200 p-2 text-left hover:border-pokemon-blue"
              >
                {result.imageUrl && <img src={result.imageUrl} alt="" className="h-16 w-12 rounded object-cover" />}
                <span>
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
          <section className="panel grid gap-5 p-5">
            <div className="flex items-start gap-4">
              {card.data.imageUrl && <img src={card.data.imageUrl} alt="" className="h-28 w-20 rounded-lg object-cover" />}
              <div>
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
              <MoneyInput label="Seller asking price" value={purchase} onChange={setPurchase} />
              <MoneyInput label="Your inbound shipping" value={buyerShipping} onChange={setBuyerShipping} />
              <MoneyInput label="Tax / other acquisition cost" value={tax} onChange={setTax} />
              <MoneyInput label="Your outbound shipping" value={sellingShipping} onChange={setSellingShipping} />
              <MoneyInput label="Expected sale price override" value={saleOverride} onChange={setSaleOverride} placeholder={market ? market.toFixed(2) : '0.00'} />
              <label className="grid gap-1 text-sm font-semibold">
                Selling cost preset
                <select className="input" value={feeMode} onChange={(e) => setFeeMode(e.target.value as typeof feeMode)}>
                  <option value="online">Online marketplace estimate · 13%</option>
                  <option value="local">Local cash estimate · 0%</option>
                  <option value="custom">Custom percentage</option>
                </select>
              </label>
              {feeMode === 'custom' && (
                <label className="grid gap-1 text-sm font-semibold">
                  Custom selling cost %
                  <input className="input" type="number" min="0" max="100" step="0.1" inputMode="decimal" value={customFee} onChange={(e) => setCustomFee(e.target.value)} />
                </label>
              )}
            </div>
            <p className="text-xs text-slate-500">
              Fee presets are editable estimates, not marketplace quotes. Verify the platform’s current fees before transacting.
            </p>
          </section>

          <aside className="panel grid content-start gap-4 p-5 lg:sticky lg:top-4 lg:self-start">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Current market reference</p>
              <p className="price text-3xl">{formatPrice(market || null)}</p>
              {marketDelta !== null && (
                <p className={`text-sm font-semibold ${marketDelta <= 0 ? 'gain' : 'loss'}`}>
                  Asking price is {Math.abs(marketDelta).toFixed(1)}% {marketDelta <= 0 ? 'below' : 'above'} market reference
                </p>
              )}
            </div>
            <dl className="grid gap-2 text-sm">
              <Line label="Total acquisition cost" value={money(acquisition)} />
              <Line label="Expected sale used" value={money(expectedSale)} />
              <Line label={`Selling costs (${feePercent.toFixed(1)}%)`} value={money(fees)} />
              <Line label="Outbound shipping" value={money(Math.max(0, num(sellingShipping)))} />
              <Line label="Estimated proceeds" value={money(proceeds)} />
              <div className="mt-2 border-t border-slate-200 pt-3">
                <Line label="Estimated net after entered costs" value={money(net)} strong tone={net >= 0 ? 'gain' : 'loss'} />
              </div>
              <Line label="Break-even sale price" value={breakEven === null ? '—' : money(breakEven)} />
            </dl>
          </aside>
        </div>
      )}
    </div>
  );
}

function MoneyInput({ label, value, onChange, placeholder = '0.00' }: { label: string; value: string; onChange: (v: string) => void; placeholder?: string }) {
  return (
    <label className="grid gap-1 text-sm font-semibold">
      {label}
      <div className="relative">
        <span className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400">$</span>
        <input className="input pl-7" type="number" min="0" step="0.01" inputMode="decimal" value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
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

function Upgrade() {
  return (
    <div className="container-custom py-12">
      <div className="panel mx-auto grid max-w-2xl gap-4 p-8 text-center">
        <p className="eyebrow">TCG Signal Pro</p>
        <h1 className="text-3xl">Deal Analyzer is a Pro tool</h1>
        <p className="text-slate-600">Compare an asking price with the market reference and model the costs that actually affect your exit.</p>
        <Link to="/pro" className="btn mx-auto bg-pokemon-pokeblue text-white">See Pro</Link>
      </div>
    </div>
  );
}
