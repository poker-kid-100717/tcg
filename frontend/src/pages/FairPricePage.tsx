import { useState } from 'react';

import { useSealed } from '../api/hooks';
import type { SealedProduct } from '../api/types';
import { ErrorState, Loading } from '../components/States';
import { judgeMarket, judgePrice, percentOver, type Tone } from '../lib/fairPrice';
import { formatDate, formatPrice } from '../lib/format';

const toneClass: Record<Tone, string> = {
  good: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  ok: 'bg-sky-50 text-sky-800 ring-sky-200',
  caution: 'bg-amber-50 text-amber-900 ring-amber-200',
  bad: 'bg-red-50 text-red-800 ring-red-200',
  neutral: 'bg-slate-50 text-slate-700 ring-slate-200',
};

export default function FairPricePage() {
  const [type, setType] = useState('');
  const [query, setQuery] = useState('');
  const [all, setAll] = useState(false);
  const sealed = useSealed({ type, q: query.trim().length >= 2 ? query.trim() : undefined, all });

  return (
    <div className="container-custom grid gap-6 py-8 sm:py-10">
      <header className="grid max-w-3xl gap-2">
        <p className="eyebrow">Fair price</p>
        <h1 className="text-3xl sm:text-4xl">Is that sealed product worth the price?</h1>
        <p className="text-slate-600">
          Every Elite Trainer Box, booster bundle and box with its retail price (MSRP) next to what it sells for now. Type in
          a price you've been quoted at a shop, a card show or online to see how it compares.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3">
        <label className="grid gap-1 text-sm font-semibold text-slate-700">
          Search
          <input
            className="input w-64"
            type="search"
            placeholder="Set or product, e.g. 151"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </label>
        <label className="grid gap-1 text-sm font-semibold text-slate-700">
          Product type
          <select className="input w-56" value={type} onChange={(e) => setType(e.target.value)}>
            <option value="">All types</option>
            {sealed.data?.types.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <label className="flex h-10 items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} />
          Include products before 2023
        </label>
      </div>

      {sealed.isPending ? (
        <Loading label="Loading sealed prices…" />
      ) : sealed.error ? (
        <ErrorState error={sealed.error} onRetry={() => sealed.refetch()} />
      ) : sealed.data.products.length === 0 ? (
        <p className="panel p-6 text-slate-600">
          No sealed products match yet. Prices are recorded once a day, so a new installation fills in after the first daily
          update.
        </p>
      ) : (
        <>
          <ul className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {sealed.data.products.map((product) => (
              <ProductCard key={product.productId} product={product} />
            ))}
          </ul>
          <p className="text-xs leading-5 text-slate-500">
            Market prices are TCGplayer's{sealed.data.asOf ? `, recorded ${formatDate(sealed.data.asOf, 'short')}` : ''} (via
            TCGCSV). "Typical MSRP" is the usual US retail price for that kind of product in the current era; a few special
            sets list higher. Products released before 2023 show no MSRP because retail prices were different then.
          </p>
        </>
      )}
    </div>
  );
}

function ProductCard({ product }: { product: SealedProduct }) {
  const [quoted, setQuoted] = useState('');
  const marketVerdict = judgeMarket(product.msrp, product.market);
  const quotedPrice = Number.parseFloat(quoted);
  const quotedVerdict = quoted.trim() !== '' && quotedPrice > 0 ? judgePrice(quotedPrice, product.msrp, product.market) : null;

  return (
    <li className="panel grid content-start gap-4 p-5">
      <div className="flex gap-4">
        {product.imageUrl && (
          <img src={product.imageUrl} alt="" loading="lazy" className="h-20 w-20 shrink-0 rounded-lg bg-slate-50 object-contain" />
        )}
        <div className="grid min-w-0 content-start gap-1">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            {product.productType} · {product.setName}
          </p>
          <h2 className="text-base leading-snug">{product.name}</h2>
        </div>
      </div>

      <dl className="grid grid-cols-2 gap-3 text-sm">
        <div>
          <dt className="text-slate-500">{product.msrpSource === 'listed' ? 'MSRP' : 'Typical MSRP'}</dt>
          <dd className="price text-lg text-slate-900">{product.msrp !== null ? formatPrice(product.msrp) : 'Not known'}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Market now</dt>
          <dd className="price text-lg text-slate-900">{product.market !== null ? formatPrice(product.market) : 'No sales yet'}</dd>
        </div>
      </dl>

      {marketVerdict && product.market !== null && product.msrp !== null && (
        <p className={`rounded-lg px-3 py-2 text-sm ring-1 ${toneClass[marketVerdict.tone]}`}>
          <strong>{marketVerdict.label}:</strong> the market is{' '}
          {Math.abs(percentOver(product.market, product.msrp))}% {product.market >= product.msrp ? 'over' : 'under'} MSRP.
        </p>
      )}

      <label className="grid gap-1.5 text-sm font-semibold text-slate-700">
        Price you're seeing
        <input
          className="input"
          type="number"
          min="0"
          step="0.01"
          inputMode="decimal"
          placeholder="e.g. 65.00"
          value={quoted}
          onChange={(e) => setQuoted(e.target.value)}
        />
      </label>
      {quotedVerdict && (
        <p role="status" className={`rounded-lg px-3 py-2 text-sm ring-1 ${toneClass[quotedVerdict.tone]}`}>
          <strong>{quotedVerdict.label}.</strong> {quotedVerdict.advice}
        </p>
      )}

      {product.tcgplayerUrl && (
        <a href={product.tcgplayerUrl} target="_blank" rel="noreferrer" className="text-sm font-semibold text-pokemon-blue">
          See listings on TCGplayer
        </a>
      )}
    </li>
  );
}
