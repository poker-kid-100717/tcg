import { useIntelligence, useSession } from '../api/hooks';
import type { MarketIntelligence } from '../api/types';
import { formatDate, formatPrice } from '../lib/format';
import { FreshnessChip, ProBadge, ProLock } from './Gates';
import { ErrorState, Loading } from './States';

const pct = (value: number | null) => (value === null ? '—' : `${value > 0 ? '+' : ''}${value.toFixed(1)}%`);

const bandTone: Record<string, string> = {
  High: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  Medium: 'bg-sky-50 text-sky-800 ring-sky-200',
  Low: 'bg-amber-50 text-amber-900 ring-amber-200',
};

/** "True Market": how much to trust the price, what moved and why, and real sales kept apart from the reference. */
export function MarketIntelligencePanel({ cardId, variant }: { cardId: string; variant: string }) {
  const session = useSession();
  const isPro = Boolean(session.data?.isPro);
  const intelligence = useIntelligence(cardId, variant, isPro);

  if (session.isPending) return <Loading label="Loading Market Intelligence…" />;

  if (!isPro) {
    return (
      <ProLock
        compact
        title="True Market"
        detail="How much to trust this card's price before you buy or sell: a 0–100 confidence score with the reasons, liquidity, signals and a plain-English account of what moved."
        points={['Confidence score and exactly what lowers it', 'Market reference kept separate from verified sold comps', '7/30/90-day changes, volatility and listing spread', 'Signals and “why is this moving?”']}
      />
    );
  }

  if (intelligence.error) return <ErrorState error={intelligence.error} onRetry={() => intelligence.refetch()} />;
  if (!intelligence.data) return <Loading label="Calculating Market Intelligence…" />;
  return <TrueMarket data={intelligence.data} />;
}

function TrueMarket({ data }: { data: MarketIntelligence }) {
  return (
    <section aria-labelledby="true-market-heading" className="panel grid gap-5 p-4 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h2 id="true-market-heading" className="text-lg">True Market</h2>
            <ProBadge />
          </div>
          <p className="mt-1 text-sm text-slate-500">{data.variantLabel} · {data.source}</p>
          <div className="mt-2"><FreshnessChip freshness={data.freshness} /></div>
        </div>
        <div className="text-right">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Market confidence</p>
          <p className="text-3xl font-bold text-slate-900">
            {data.confidenceScore ?? '—'}<span className="text-sm text-slate-400">/100</span>
          </p>
          <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-bold ring-1 ${bandTone[data.confidenceBand] ?? 'bg-slate-100 text-slate-700 ring-slate-200'}`}>
            {data.confidenceBand}
          </span>
        </div>
      </div>

      <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-700">{data.confidenceExplanation}</p>
      {data.isStale && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">This price is stale. Treat it as context, not a current quote.</p>
      )}

      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Metric label="Market reference" value={formatPrice(data.market)} />
        <Metric label="Lowest listing" value={formatPrice(data.low)} note={data.lowVsMarketPercent === null ? undefined : `${pct(data.lowVsMarketPercent)} vs market`} />
        <Metric label="7-day change" value={pct(data.change7Percent)} />
        <Metric label="30-day change" value={pct(data.change30Percent)} />
        <Metric label="90-day change" value={pct(data.change90Percent)} />
        <Metric label="Volatility" value={data.volatilityLevel} note={data.volatilityPercent === null ? undefined : `${data.volatilityPercent.toFixed(1)}% daily`} />
        <Metric label="30-day range" value={data.low30 !== null && data.high30 !== null ? `${formatPrice(data.low30)}–${formatPrice(data.high30)}` : '—'} />
        <Metric label="Prices recorded · 30d" value={String(data.observationDays)} />
      </dl>

      <div className="grid gap-3 lg:grid-cols-2">
        <Box title="Why is this moving?">
          <ul className="grid gap-1">
            {data.whyMoving.map((line) => <li key={line}>• {line}</li>)}
          </ul>
        </Box>
        <Box title="What affects the confidence score">
          {data.confidenceFactors.length === 0 ? (
            <p>Nothing is lowering it.</p>
          ) : (
            <ul className="grid gap-1">
              {data.confidenceFactors.map((f) => (
                <li key={f.name} className="flex justify-between gap-3">
                  <span>{f.detail}</span>
                  <span className="shrink-0 font-semibold tabular-nums text-slate-700">{f.penalty > 0 ? `−${f.penalty}` : 'ok'}</span>
                </li>
              ))}
            </ul>
          )}
        </Box>
      </div>

      {data.signals.length > 0 && (
        <div className="grid gap-2">
          <h3 className="text-sm font-semibold text-slate-900">Signals</h3>
          <ul className="grid gap-2 sm:grid-cols-2">
            {data.signals.map((s) => (
              <li key={s.kind} className="rounded-lg border border-slate-200 p-3 text-sm">
                <p className="font-semibold text-slate-900">
                  {s.name} <span className="text-slate-500">{s.value > 0 && s.unit === '%' ? '+' : ''}{s.value}{s.unit === 'pts' ? ' pts' : s.unit}</span>
                </p>
                <p className="text-xs text-slate-600">{s.reason}</p>
                <p className="mt-1 text-[11px] text-slate-500">{s.lookbackDays}-day lookback · as of {formatDate(s.asOf)}</p>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="grid gap-3 lg:grid-cols-2">
        <Box title="Verified sold comps">
          {data.verifiedComps.available ? (
            <dl className="grid gap-1">
              <Pair label="Sales · 30 days" value={data.verifiedComps.sales30?.toString() ?? '—'} />
              <Pair label="Median sold · 30 days" value={formatPrice(data.verifiedComps.medianSold30)} />
              <Pair label="Last sale" value={data.verifiedComps.lastSalePrice === null ? '—' : `${formatPrice(data.verifiedComps.lastSalePrice)}${data.verifiedComps.lastSaleAt ? ` on ${formatDate(data.verifiedComps.lastSaleAt)}` : ''}`} />
              <p className="mt-1 text-[11px] text-slate-500">Source: {data.verifiedComps.source}. Actual completed sales, not listings.</p>
            </dl>
          ) : (
            <p>{data.verifiedComps.explanation} The market reference above is a provider’s computed price, not a record of sales.</p>
          )}
        </Box>
        <Box title={`Liquidity: ${data.liquidity}`}>
          <p>{data.liquidityReason}</p>
        </Box>
      </div>

      {data.recentSoldComps.length > 0 && (
        <div className="overflow-hidden rounded-lg border border-slate-200">
          <p className="border-b border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-900">Recent verified sales</p>
          <ul className="divide-y divide-slate-100">
            {data.recentSoldComps.map((comp) => (
              <li key={comp.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm">
                <div className="min-w-0">
                  <p className="font-semibold text-slate-900">{formatPrice(comp.price)} · {comp.source}</p>
                  <p className="max-w-2xl truncate text-xs text-slate-500">{comp.title ?? 'Sold listing'} · {formatDate(comp.soldAt)}</p>
                </div>
                {comp.url && (
                  <a href={comp.url} target="_blank" rel="noreferrer noopener" className="text-xs font-semibold text-pokemon-pokeblue hover:underline">
                    View sale ↗
                  </a>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      <p className="text-xs text-slate-500">
        As of {formatDate(data.asOf)}. Confidence measures the quality and consistency of the data behind the price, not a
        prediction. Nothing here is financial advice or a guarantee of what a card will sell for.
      </p>
    </section>
  );
}

function Metric({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="min-w-0 rounded-lg border border-slate-200 p-3">
      <dt className="text-xs text-slate-500">{label}</dt>
      <dd className="mt-1 truncate font-semibold tabular-nums text-slate-900">{value}</dd>
      {note && <dd className="text-[11px] text-slate-500">{note}</dd>}
    </div>
  );
}

function Box({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-xs leading-5 text-slate-600">
      <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">{title}</p>
      {children}
    </div>
  );
}

function Pair({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt>{label}</dt>
      <dd className="font-semibold text-slate-900">{value}</dd>
    </div>
  );
}
