import { Link } from 'react-router-dom';

import { useIntelligence, useSession } from '../api/hooks';
import { formatDate, formatPrice } from '../lib/format';
import { ErrorState, Loading } from './States';

const pct = (value: number | null) => (value === null ? '—' : `${value > 0 ? '+' : ''}${value.toFixed(1)}%`);

export function MarketIntelligencePanel({ cardId, variant }: { cardId: string; variant: string }) {
  const session = useSession();
  const intelligence = useIntelligence(cardId, variant, Boolean(session.data?.isPro));

  if (session.isPending) return <Loading label="Loading Market Intelligence…" />;

  if (!session.data?.isPro) {
    return (
      <section className="panel grid gap-3 p-5">
        <div className="flex items-center gap-2">
          <h2 className="text-lg">Market Intelligence</h2>
          <span className="rounded-full bg-pokemon-yellow px-2 py-0.5 text-[11px] font-extrabold text-pokemon-pokeblue">PRO</span>
        </div>
        <p className="max-w-2xl text-sm text-slate-600">
          See how trustworthy the current price data is, what changed over 7 and 30 days, volatility, spread, freshness,
          and exactly what the app does not know.
        </p>
        <Link to="/pro" className="btn w-fit bg-pokemon-pokeblue text-white hover:brightness-110">
          Unlock Market Intelligence
        </Link>
      </section>
    );
  }

  if (intelligence.isPending) return <Loading label="Calculating Market Intelligence…" />;
  if (intelligence.error) return <ErrorState error={intelligence.error} onRetry={() => intelligence.refetch()} />;
  if (!intelligence.data) return null;

  const data = intelligence.data;
  return (
    <section aria-labelledby="market-intelligence-heading" className="panel grid gap-5 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 id="market-intelligence-heading" className="text-lg">Market Intelligence</h2>
            <span className="rounded-full bg-pokemon-yellow px-2 py-0.5 text-[11px] font-extrabold text-pokemon-pokeblue">PRO</span>
          </div>
          <p className="mt-1 text-sm text-slate-500">{data.variantLabel} · {data.source}</p>
        </div>
        <div className="text-right">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Data confidence</p>
          <p className="text-2xl font-bold text-slate-900">{data.confidenceScore}<span className="text-sm text-slate-400">/100</span></p>
          <p className="text-xs font-semibold text-slate-600">{data.confidenceBand}</p>
        </div>
      </div>

      {data.isStale && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">
          This pricing observation is stale. Treat it as context, not a current quote.
        </p>
      )}

      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Metric label="Market reference" value={formatPrice(data.market)} />
        <Metric label="7-day change" value={pct(data.change7Percent)} />
        <Metric label="30-day change" value={pct(data.change30Percent)} />
        <Metric label="Volatility" value={pct(data.volatilityPercent)} />
        <Metric label="Current low" value={formatPrice(data.low)} />
        <Metric label="Median sold · 30d" value={formatPrice(data.medianSold30Days)} />
        <Metric label="Sold comps · 30d" value={data.soldComps30Days === null ? '—' : String(data.soldComps30Days)} />
        <Metric label="Daily observations" value={String(data.observationDays)} />
      </dl>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Liquidity</p>
          <p className="mt-1 font-bold text-slate-900">{data.liquidity}</p>
          <p className="mt-1 text-xs leading-5 text-slate-600">{data.liquidityReason}</p>
        </div>
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Why this confidence?</p>
          <ul className="mt-1 grid gap-1 text-xs leading-5 text-slate-600">
            {data.reasons.map((reason) => <li key={reason}>• {reason}</li>)}
          </ul>
        </div>
      </div>

      {data.recentSoldComps.length > 0 && (
        <div className="overflow-hidden rounded-lg border border-slate-200">
          <div className="border-b border-slate-200 bg-slate-50 px-4 py-3">
            <p className="text-sm font-semibold text-slate-900">Recent sold comps</p>
            <p className="text-xs text-slate-500">Raw sales returned by the configured market-data provider.</p>
          </div>
          <ul className="divide-y divide-slate-100">
            {data.recentSoldComps.map((comp) => (
              <li key={comp.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm">
                <div className="min-w-0">
                  <p className="font-semibold text-slate-900">{formatPrice(comp.price)} · {comp.source}</p>
                  <p className="max-w-2xl truncate text-xs text-slate-500">{comp.title ?? 'Sold listing'} · {formatDate(comp.soldAt)}</p>
                </div>
                {comp.url && (
                  <a href={comp.url} target="_blank" rel="noreferrer" className="text-xs font-semibold text-pokemon-pokeblue hover:underline">
                    View sale ↗
                  </a>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      <p className="text-xs text-slate-500">
        As of {formatDate(data.asOf)}. Confidence measures the quality and consistency of the available pricing observations;
        it is not a claim that the card will sell for a specific amount.
      </p>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-slate-200 p-3">
      <dt className="text-xs text-slate-500">{label}</dt>
      <dd className="mt-1 font-semibold tabular-nums text-slate-900">{value}</dd>
    </div>
  );
}
