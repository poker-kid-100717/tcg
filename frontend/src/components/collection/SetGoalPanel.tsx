import { useState } from 'react';
import { Link } from 'react-router-dom';

import { useCollectionActions } from '../../api/collection';
import type { SetChecklist, SetGoalKind } from '../../api/types';
import { GOALS } from '../../lib/conditions';
import { formatPrice, formatTotal } from '../../lib/format';

const MISSING_SHOWN = 12;

/** Pick a goal for the set (main, full or master), see how far along it is, and fill a master set printing by printing. */
export function SetGoalPanel({ setId, setName, checklist }: { setId: string; setName: string; checklist?: SetChecklist }) {
  const { setGoal, removeGoal, add } = useCollectionActions();
  const [showAll, setShowAll] = useState(false);
  const goal = checklist?.goal ?? null;
  const shown: SetGoalKind = goal ?? 'MainSet';
  const progress = checklist?.progress.find((p) => p.kind === shown) ?? null;
  const percent = progress?.completion ?? 0;
  const missingPrintings = checklist?.master.missing ?? [];

  return (
    <section aria-label="Set goal" className="panel mb-8 grid grid-cols-[minmax(0,1fr)] gap-4 p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg">{goal ? `Your goal: ${GOALS.find((g) => g.value === goal)!.label.toLowerCase()}` : `Collect ${setName}`}</h2>
        <div role="group" aria-label="Goal for this set" className="flex flex-wrap gap-1 rounded-lg bg-slate-100 p-1 text-sm font-semibold">
          {GOALS.map((g) => (
            <button
              key={g.value}
              type="button"
              aria-pressed={goal === g.value}
              title={g.hint}
              disabled={setGoal.isPending}
              onClick={() => setGoal.mutate({ setId, kind: g.value })}
              className={`rounded-md px-3 py-1.5 ${goal === g.value ? 'bg-pokemon-pokeblue text-white shadow-sm' : 'text-slate-700 hover:bg-white'}`}
            >
              {g.label}
            </button>
          ))}
        </div>
      </div>
      <p className="text-sm text-slate-600">{GOALS.find((g) => g.value === shown)!.hint}.</p>

      {progress && (
        <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
          <div className="grid gap-2">
            <p className="font-semibold text-slate-900">
              {progress.owned} of {progress.total} {shown === 'MasterSet' ? 'printings' : 'cards'} ({Math.round(percent)}%)
            </p>
            <span className="h-2 overflow-hidden rounded-full bg-slate-100">
              <span className="block h-full rounded-full bg-pokemon-blue" style={{ width: `${percent}%` }} />
            </span>
          </div>
          <div className="text-sm">
            <p className="text-slate-500">Cost to finish</p>
            <p className="price text-xl text-slate-900">{formatTotal(progress.costToComplete)}</p>
            {!!progress.unpriced && <p className="text-xs text-slate-500">+ {progress.unpriced} without a price</p>}
          </div>
        </div>
      )}

      {goal === 'MasterSet' && missingPrintings.length > 0 && (
        <div className="grid gap-2">
          <h3 className="text-sm font-semibold text-slate-700">Printings you still need</h3>
          <ul className="grid grid-cols-[minmax(0,1fr)] gap-1.5 sm:grid-cols-2" aria-label="Printings you still need">
            {(showAll ? missingPrintings : missingPrintings.slice(0, MISSING_SHOWN)).map((m) => (
              <li key={`${m.cardId}-${m.variant ?? 'any'}`} className="flex items-center gap-3 rounded-lg bg-slate-50 px-3 py-2 text-sm">
                <Link to={`/cards/${encodeURIComponent(m.cardId)}`} className="min-w-0 flex-1 truncate hover:text-pokemon-blue">
                  <span className="font-semibold text-slate-900">{m.name}</span> <span className="text-slate-500">#{m.number} · {m.variantLabel}</span>
                </Link>
                <span className="price">{formatPrice(m.price)}</span>
                <button
                  type="button"
                  className="rounded-md border border-slate-200 bg-white px-2 py-0.5 text-xs font-semibold text-pokemon-pokeblue hover:border-pokemon-blue"
                  onClick={() => add.mutate({ cardId: m.cardId, variant: m.variant ?? 'normal' })}
                  aria-label={`Add ${m.name} #${m.number} ${m.variantLabel}`}
                >
                  + Add
                </button>
              </li>
            ))}
          </ul>
          {missingPrintings.length > MISSING_SHOWN && (
            <button type="button" className="btn-ghost w-fit px-2 py-1 text-sm" onClick={() => setShowAll((v) => !v)}>
              {showAll ? 'Show fewer' : `Show all ${missingPrintings.length}`}
            </button>
          )}
        </div>
      )}

      {goal && (
        <button type="button" className="w-fit text-xs font-semibold text-slate-500 hover:text-red-700 hover:underline" onClick={() => removeGoal.mutate(setId)}>
          Stop tracking this set
        </button>
      )}
      {(setGoal.error ?? add.error) && (
        <p role="alert" className="text-sm text-red-700">
          {(setGoal.error ?? add.error)!.message}
        </p>
      )}
    </section>
  );
}
