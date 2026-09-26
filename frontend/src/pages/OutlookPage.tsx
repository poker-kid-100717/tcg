import { useModelSummary, usePredictions } from '../api/hooks';
import { PredictionTable, statusMessage } from '../components/Predictions';
import { ErrorState, Loading } from '../components/States';
import { formatDate } from '../lib/format';

export default function OutlookPage() {
  const model = useModelSummary();
  const up = usePredictions('up');
  const down = usePredictions('down');
  const horizon = model.data?.horizonDays || 30;
  const preview = model.data?.status === 'Preview' || up.data?.status === 'Preview' || down.data?.status === 'Preview';

  return (
    <div className="container-custom grid gap-10 py-8 sm:py-10">
      <div className="grid gap-2">
        <p className="eyebrow">Price outlook</p>
        <h1 className="text-3xl sm:text-4xl">Where prices are heading</h1>
        <p className="max-w-3xl text-slate-600">
          {preview ? (
            <>
              The validated {horizon}-day model is still collecting daily history. Until it can train and beat the
              no-change baseline, these lists show transparent early market signals from observed price movement or
              TCGplayer listing pressure. They switch to model forecasts automatically once validation is possible.
            </>
          ) : (
            <>
              A machine-learning model predicts each card&apos;s TCGplayer market price {horizon} days from now. It learns
              from the recorded price history and from what the card is: the Pokémon on it, its rarity and printing, its
              set&apos;s age and size, where it ranks in the set, the artist, and how its listings compare with what it
              sells for. Every prediction lists the factors that moved it most.
            </>
          )}
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        {(['up', 'down'] as const).map((direction) => {
          const list = direction === 'up' ? up : down;
          const title = direction === 'up' ? 'Predicted to rise' : 'Predicted to fall';
          return (
            <section key={direction} aria-label={title} className="panel overflow-hidden">
              <div className="flex items-center justify-between gap-3 border-b border-slate-200 px-4 py-3">
                <h2 className="text-lg">{title}</h2>
                {list.data?.status === 'Preview' && (
                  <span className="rounded-full bg-amber-50 px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-amber-700 ring-1 ring-amber-200">
                    Early signal
                  </span>
                )}
              </div>
              {list.isPending ? (
                <Loading label="Loading predictions…" />
              ) : list.error ? (
                <ErrorState error={list.error} onRetry={() => list.refetch()} />
              ) : (
                <PredictionTable cards={list.data.cards} empty={statusMessage(list.data.status)} status={list.data.status} />
              )}
            </section>
          );
        })}
      </div>

      <section aria-labelledby="model-heading" className="grid gap-4">
        <h2 id="model-heading" className="text-2xl">
          How good is it?
        </h2>
        {model.isPending ? (
          <Loading label="Loading the model's results…" />
        ) : model.error ? (
          <ErrorState error={model.error} onRetry={() => model.refetch()} />
        ) : (
          <ModelCard summary={model.data} />
        )}
        <p className="text-xs text-slate-500">
          Predictions are statistical estimates from past prices, not financial advice. Prices can move for reasons no model
          sees coming, like reprints, tournament results or a new set.
        </p>
      </section>
    </div>
  );
}

function Stat({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="panel grid gap-1 p-4">
      <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="text-2xl font-bold tabular-nums text-slate-900">{value}</dd>
      {note && <dd className="text-xs text-slate-500">{note}</dd>}
    </div>
  );
}

function ModelCard({ summary }: { summary: NonNullable<ReturnType<typeof useModelSummary>['data']> }) {
  if (summary.typicalErrorPercent === null) {
    const target = Math.max(summary.approxHistoryDaysNeeded, 1);
    const progress = Math.min(100, Math.round((summary.historyDays / target) * 100));
    const mode =
      summary.status === 'Preview'
        ? 'Early signal'
        : summary.status === 'Withheld'
          ? 'Validation withheld'
          : 'Collecting data';

    return (
      <div className="grid gap-4">
        <p className="panel p-5 text-sm text-slate-600">{summary.message ?? statusMessage(summary.status)}</p>
        <dl className="grid gap-4 sm:grid-cols-3">
          <Stat label="History collected" value={`${summary.historyDays} days`} note="daily TCGplayer snapshots" />
          <Stat label="Training target" value={`~${summary.approxHistoryDaysNeeded} days`} note={`for a ${summary.horizonDays}-day held-out test`} />
          <Stat label="Current mode" value={mode} note="validated accuracy is never fabricated" />
        </dl>
        <div className="panel grid gap-2 p-5">
          <div className="flex items-center justify-between gap-3 text-sm">
            <span className="font-semibold text-slate-700">Model readiness</span>
            <span className="tabular-nums text-slate-500">{progress}%</span>
          </div>
          <div className="h-2 overflow-hidden rounded-full bg-slate-100" aria-label={`Model readiness ${progress}%`}>
            <div className="h-full rounded-full bg-pokemon-pokeblue" style={{ width: `${progress}%` }} />
          </div>
          <p className="text-xs text-slate-500">
            The accuracy cards below replace this readiness view after the model has enough history to train and validate.
          </p>
        </div>
      </div>
    );
  }
  const pct = (n: number | null) => (n === null ? '—' : `${n.toFixed(1)}%`);
  const top = summary.importance.slice(0, 8);
  const max = Math.max(...top.map((f) => f.weight), 1);

  return (
    <div className="grid gap-6">
      {summary.message && <p className="panel p-4 text-sm text-slate-600">{summary.message}</p>}
      <p className="max-w-3xl text-sm text-slate-600">
        Trained on {summary.trainingRows.toLocaleString()} past prices, then tested on the most recent{' '}
        {summary.validationRows.toLocaleString()}, from weeks the model never saw during training. Last trained{' '}
        {formatDate(summary.asOf)}.
      </p>
      <dl className="grid gap-4 sm:grid-cols-3">
        <Stat label="Typical miss" value={pct(summary.typicalErrorPercent)} note={`over ${summary.horizonDays} days, on held-out weeks`} />
        <Stat label="“No change” misses by" value={pct(summary.noChangeErrorPercent)} note="the baseline the model has to beat" />
        <Stat label="Direction right" value={pct(summary.directionAccuracyPercent)} note="of moves bigger than 5%" />
      </dl>

      {top.length > 0 && (
        <div className="panel grid gap-3 p-5">
          <h3 className="text-base">What the model relies on most</h3>
          <ul className="grid gap-2">
            {top.map((f) => (
              <li key={f.feature} className="grid grid-cols-[minmax(0,12rem)_1fr_3rem] items-center gap-3 text-sm">
                <span className="truncate text-slate-700">{f.label}</span>
                <span className="h-2 rounded-full bg-slate-100" aria-hidden="true">
                  <span className="block h-2 rounded-full bg-pokemon-pokeblue" style={{ width: `${(f.weight / max) * 100}%` }} />
                </span>
                <span className="text-right tabular-nums text-slate-500">{f.weight.toFixed(1)}%</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="panel overflow-x-auto p-5">
        <h3 className="mb-1 text-base">Track record</h3>
        <p className="mb-3 text-sm text-slate-600">
          Once a week, the predictions are kept and checked against real prices when their horizon is up.
        </p>
        {summary.trackRecord.length === 0 ? (
          <p className="text-sm text-slate-500">No predictions have reached their date yet.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th className="py-1 pr-4 font-semibold">Predicted on</th>
                <th className="py-1 pr-4 font-semibold">Horizon</th>
                <th className="py-1 pr-4 font-semibold">Cards</th>
                <th className="py-1 pr-4 font-semibold">Typical miss</th>
                <th className="py-1 pr-4 font-semibold">“No change” miss</th>
                <th className="py-1 font-semibold">Direction right</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {summary.trackRecord.map((r) => (
                <tr key={r.asOf} className="border-t border-slate-100">
                  <td className="py-1.5 pr-4">{formatDate(r.asOf, 'short')}</td>
                  <td className="py-1.5 pr-4">{r.horizonDays} days</td>
                  <td className="py-1.5 pr-4">{r.count.toLocaleString()}</td>
                  <td className={`py-1.5 pr-4 ${r.typicalErrorPercent < r.noChangeErrorPercent ? 'gain' : 'loss'}`}>
                    {pct(r.typicalErrorPercent)}
                  </td>
                  <td className="py-1.5 pr-4">{pct(r.noChangeErrorPercent)}</td>
                  <td className="py-1.5">{pct(r.directionAccuracyPercent)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
