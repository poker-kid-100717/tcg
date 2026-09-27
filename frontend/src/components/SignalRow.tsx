import { Link } from 'react-router-dom';

import type { SignalItem } from '../api/types';
import { formatDate } from '../lib/format';

export const SIGNAL_LABELS: Record<string, string> = {
  Momentum: 'Momentum',
  Acceleration: 'Acceleration',
  UnusualMove: 'Unusual move',
  VolatilityExpansion: 'Volatility expansion',
  New30DayHigh: 'New 30-day high',
  New30DayLow: 'New 30-day low',
  ThinSupply: 'Thin supply',
  Sleeper: 'Sleeper',
  SustainedDowntrend: 'Sustained downtrend',
};

const valueLabel = (s: { value: number; unit: string }) =>
  `${s.value > 0 && s.unit === '%' ? '+' : ''}${s.value}${s.unit === 'pts' ? ' pts' : s.unit}`;

/** One stored market signal: which card, the rule's number, the reason, and the evidence behind it. */
export function SignalRow({ signal }: { signal: SignalItem }) {
  return (
    <li className="grid gap-1 p-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <Link to={`/cards/${signal.cardId}`} className="min-w-0 font-semibold text-slate-900 hover:text-pokemon-pokeblue">
          {signal.cardName} <span className="font-normal text-slate-500">· {signal.variantLabel}</span>
        </Link>
        <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-bold text-slate-700">
          {signal.name} {valueLabel(signal)}
        </span>
      </div>
      <p className="text-sm text-slate-600">{signal.reason}</p>
      <p className="text-xs text-slate-500">
        {signal.lookbackDays}-day lookback · confidence {signal.confidence}/100 · {signal.source} · {formatDate(signal.asOf)}
      </p>
    </li>
  );
}
