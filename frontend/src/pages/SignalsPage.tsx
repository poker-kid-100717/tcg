import { useState } from 'react';

import { useMe, useSignals } from '../api/hooks';
import { FreshnessChip, ProBadge, ProLock } from '../components/Gates';
import { SignalRow } from '../components/SignalRow';
import { ErrorState, Loading } from '../components/States';

const KINDS: { id: string; label: string }[] = [
  { id: '', label: 'All signals' },
  { id: 'Momentum', label: 'Momentum' },
  { id: 'Acceleration', label: 'Acceleration' },
  { id: 'UnusualMove', label: 'Unusual move' },
  { id: 'VolatilityExpansion', label: 'Volatility expansion' },
  { id: 'New30DayHigh', label: 'New 30-day high' },
  { id: 'New30DayLow', label: 'New 30-day low' },
  { id: 'ThinSupply', label: 'Thin supply' },
  { id: 'Sleeper', label: 'Sleeper' },
  { id: 'SustainedDowntrend', label: 'Sustained downtrend' },
];

export default function SignalsPage() {
  const me = useMe();
  const isPro = Boolean(me.data?.account?.isPro);
  const [kind, setKind] = useState('');
  const [minConfidence, setMinConfidence] = useState(50);
  const signals = useSignals(kind, minConfidence, isPro);

  if (me.isPending) return <Loading label="Loading signals…" />;

  return (
    <div className="container-custom grid gap-6 py-8 sm:py-10">
      <header className="grid gap-2">
        <p className="eyebrow flex items-center gap-2">Signal center <ProBadge /></p>
        <h1 className="text-3xl sm:text-4xl">Today’s market signals</h1>
        <p className="max-w-3xl text-slate-600">
          Each signal is a fixed rule over the recorded market reference — what the prices did, with the lookback, the numbers
          and how much data backs it. Signals describe the past; none is a prediction or a recommendation to buy or sell.
        </p>
      </header>

      {!isPro ? (
        <ProLock
          title="The signal center is part of Pro"
          detail="Every priced card is checked after each daily update against nine published rules."
          points={['Momentum and acceleration', 'Unusual daily moves and volatility expansion', 'New 30-day highs and lows', 'Sleepers, thin supply and sustained downtrends']}
        />
      ) : (
        <>
          <div className="panel flex flex-wrap items-end gap-4 p-4">
            <label className="grid gap-1 text-sm font-semibold">
              Signal
              <select className="input" value={kind} onChange={(e) => setKind(e.target.value)}>
                {KINDS.map((k) => <option key={k.id} value={k.id}>{k.label}{k.id && signals.data?.counts[k.id] ? ` (${signals.data.counts[k.id]})` : ''}</option>)}
              </select>
            </label>
            <label className="grid gap-1 text-sm font-semibold">
              Minimum confidence
              <select className="input" value={minConfidence} onChange={(e) => setMinConfidence(Number(e.target.value))}>
                <option value={0}>Any</option>
                <option value={50}>Medium and up (50+)</option>
                <option value={75}>High (75+)</option>
              </select>
            </label>
            <div className="ml-auto"><FreshnessChip freshness={signals.data?.freshness} /></div>
          </div>

          {signals.error ? (
            <ErrorState error={signals.error} onRetry={() => signals.refetch()} />
          ) : !signals.data ? (
            <Loading label="Loading signals…" />
          ) : signals.data.signals.length === 0 ? (
            <p className="panel p-6 text-sm text-slate-600">No signals match these filters for the latest price update.</p>
          ) : (
            <ul className="panel divide-y divide-slate-100">
              {signals.data.signals.map((s) => <SignalRow key={`${s.cardId}-${s.variant}-${s.kind}`} signal={s} />)}
            </ul>
          )}

          {signals.data && (
            <details className="panel p-5 text-sm">
              <summary className="cursor-pointer font-semibold">How each signal is defined</summary>
              <dl className="mt-3 grid gap-2">
                {Object.entries(signals.data.rules).map(([id, rule]) => (
                  <div key={id}>
                    <dt className="font-semibold text-slate-900">{KINDS.find((k) => k.id === id)?.label ?? id}</dt>
                    <dd className="text-slate-600">{rule}</dd>
                  </div>
                ))}
              </dl>
            </details>
          )}
        </>
      )}
    </div>
  );
}
