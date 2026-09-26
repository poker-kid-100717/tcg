import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';

import { api } from '../api/client';
import { useAccount, useCollection, useCollectionActions } from '../api/collection';
import { useSets } from '../api/hooks';
import type { CardCondition, CollectionEntry, CollectionSignal, CollectionView, GoalProgress, SetProgress } from '../api/types';
import { ConfidenceBadge } from '../components/collection/ConfidenceBadge';
import { ValueChart } from '../components/collection/ValueChart';
import { ErrorState, Loading } from '../components/States';
import { CONDITIONS, formatPercent, GOALS } from '../lib/conditions';
import { formatChange, formatDate, formatPrice, formatTotal } from '../lib/format';

export default function CollectionPage() {
  const account = useAccount();
  const collection = useCollection();

  if (account.isPending || (account.data?.signedIn && collection.isPending)) return <Loading label="Loading your collection…" />;
  if (collection.error) return <ErrorState error={collection.error} onRetry={() => collection.refetch()} />;
  if (!collection.data || (collection.data.items.length === 0 && collection.data.goals.length === 0)) return <EmptyCollection />;
  return <Dashboard view={collection.data} isGuest={!!account.data?.isGuest} />;
}

// ------------------------------------------------------------------ empty state

function EmptyCollection() {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const { sample } = useCollectionActions();
  const sets = useSets();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (query.trim().length >= 2) navigate(`/search?q=${encodeURIComponent(query.trim())}`);
  };

  return (
    <>
      <section className="bg-pokemon-pokeblue pb-14 text-white">
        <div className="container-custom grid gap-6 pt-10 sm:pt-14">
          <p className="text-xs font-semibold tracking-wider text-pokemon-yellow uppercase">Your Pokémon TCG collection</p>
          <h1 className="max-w-3xl text-4xl leading-tight text-white sm:text-5xl">Know what your collection is really worth.</h1>
          <p className="max-w-2xl text-lg text-white/80">
            Add the cards you own and see three honest numbers: market value, value in the condition you actually have, and
            what you&apos;d take home after TCGplayer fees. Track set completion, what the missing cards cost, and when a
            card is worth selling. No sign-up needed to start.
          </p>
          <form role="search" onSubmit={submit} className="flex max-w-xl gap-2">
            <label htmlFor="hero-search" className="sr-only">
              Find a card to add
            </label>
            <input
              id="hero-search"
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Find a card you own, e.g. Umbreon"
              className="h-12 flex-1 rounded-lg border-0 px-4 text-base text-slate-900 focus:ring-2 focus:ring-pokemon-yellow focus:outline-none"
            />
            <button type="submit" className="btn-shop h-12 px-6">
              Search
            </button>
          </form>
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              className="btn border border-white/40 text-white hover:bg-white/10"
              onClick={() => sample.mutate()}
              disabled={sample.isPending}
            >
              {sample.isPending ? 'Building a sample…' : 'Explore a sample collection'}
            </button>
            <span className="text-sm text-white/70">Fills your collection with ~24 real cards you can edit or clear.</span>
          </div>
          {sample.error && (
            <p role="alert" className="text-sm text-pokemon-yellow">
              {sample.error.message}
            </p>
          )}
        </div>
      </section>

      <div className="container-custom grid gap-10 py-10">
        <ol className="grid gap-4 md:grid-cols-3">
          {[
            ['Add what you own', 'Search or open a set, then add each card with its printing, condition and what you paid.'],
            ['See real value', 'Market, condition-adjusted and after-fees values, with a confidence rating on every price.'],
            ['Collect smarter', 'Set completion with the cost of the missing cards, wishlist price targets, and sell-or-hold heads-ups.'],
          ].map(([title, text], i) => (
            <li key={title} className="panel grid gap-1 p-5">
              <span className="eyebrow">Step {i + 1}</span>
              <h2 className="text-lg">{title}</h2>
              <p className="text-sm text-slate-600">{text}</p>
            </li>
          ))}
        </ol>

        {sets.data && (
          <section className="grid gap-4">
            <div className="flex items-baseline justify-between">
              <h2 className="text-2xl">Start from a set</h2>
              <Link to="/sets" className="text-sm font-semibold text-pokemon-blue hover:underline">
                All sets →
              </Link>
            </div>
            <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
              {sets.data.slice(0, 6).map((set) => (
                <li key={set.id}>
                  <Link to={`/sets/${set.id}`} className="panel flex h-full flex-col items-center gap-3 p-4 text-center transition hover:border-pokemon-blue">
                    {set.logoUrl ? <img src={set.logoUrl} alt="" loading="lazy" className="h-14 w-full object-contain" /> : <span className="h-14" aria-hidden="true" />}
                    <span className="line-clamp-1 text-sm font-semibold text-slate-900">{set.name}</span>
                    <span className="text-xs text-slate-500">{formatDate(set.releaseDate, 'short')}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </>
  );
}

// ------------------------------------------------------------------ dashboard

function Dashboard({ view, isGuest }: { view: CollectionView; isGuest: boolean }) {
  const { summary } = view;
  const feeText = `TCGplayer takes ${formatPercent(view.fees.commissionRate, 2)} + ${formatPercent(view.fees.paymentRate, 1)} + ${formatPrice(view.fees.perSaleFee)} per sale`;

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="grid gap-1">
          <p className="eyebrow">My collection</p>
          <h1 className="text-3xl sm:text-4xl">
            {summary.cards.toLocaleString()} cards · {formatTotal(summary.conditionValue)}
          </h1>
          <p className="text-sm text-slate-600">
            {summary.unique} unique printings · prices as of {formatDate(summary.pricesAsOf)}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link to="/sets" className="btn bg-pokemon-pokeblue text-white hover:brightness-110">
            + Add cards
          </Link>
          <a href={api.exportUrl} className="btn border border-slate-300 bg-white text-slate-800 hover:border-pokemon-blue" download>
            Export CSV
          </a>
        </div>
      </header>

      {isGuest && (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <p>This collection lives in this browser only. Add an email to keep it and open it on other devices.</p>
          <Link to="/account" className="font-semibold underline">
            Save my collection
          </Link>
        </div>
      )}

      <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Tile
          label="Market value"
          value={formatTotal(summary.marketValue)}
          hint={summary.change30Percent !== null ? `${formatChange(summary.change30Percent)} over 30 days` : 'Near Mint TCGplayer market'}
          tone={summary.change30Percent === null ? undefined : summary.change30Percent >= 0 ? 'gain' : 'loss'}
        />
        <Tile label="In your condition" value={formatTotal(summary.conditionValue)} hint="Adjusted for each copy's condition" />
        <Tile label="Net if sold" value={formatTotal(summary.netIfSold)} hint="After TCGplayer seller fees" title={feeText} />
        <Tile
          label="Gain on cost"
          value={summary.gain === null ? '—' : `${summary.gain >= 0 ? '+' : '−'}${formatTotal(Math.abs(summary.gain))}`}
          hint={summary.costBasis === null ? 'Add what you paid to track this' : `on ${formatTotal(summary.costBasis)} paid${summary.gainPercent !== null ? ` (${formatChange(summary.gainPercent)})` : ''}`}
          tone={summary.gain === null ? undefined : summary.gain >= 0 ? 'gain' : 'loss'}
        />
      </dl>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <section aria-labelledby="value-heading" className="panel grid gap-3 p-5">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h2 id="value-heading" className="text-lg">
              Value over 90 days
            </h2>
            <p className="text-xs text-slate-500">{Math.round(summary.highConfidenceShare)}% of value is high-confidence pricing</p>
          </div>
          <ValueChart history={view.history} />
        </section>
        <Signals signals={view.signals} />
      </div>

      <Goals goals={view.goals} />

      <Holdings items={view.items} />

      <SetsProgress sets={view.sets.filter((s) => !view.goals.some((g) => g.setId === s.setId))} />
    </div>
  );
}

function Tile({ label, value, hint, tone, title }: { label: string; value: string; hint?: string; tone?: 'gain' | 'loss'; title?: string }) {
  return (
    <div className="panel grid gap-0.5 p-4" title={title}>
      <dt className="text-xs font-medium text-slate-500">{label}</dt>
      <dd className="price text-2xl text-slate-900">{value}</dd>
      {hint && <dd className={`text-xs ${tone ?? 'text-slate-500'}`}>{hint}</dd>}
    </div>
  );
}

const SIGNAL_STYLE: Record<CollectionSignal['kind'], { label: string; className: string }> = {
  ConsiderSelling: { label: 'Consider selling', className: 'bg-red-50 text-red-800' },
  Watch: { label: 'Watch', className: 'bg-amber-50 text-amber-800' },
  Hold: { label: 'Hold', className: 'bg-emerald-50 text-emerald-800' },
};

function Signals({ signals }: { signals: CollectionSignal[] }) {
  return (
    <section aria-labelledby="signals-heading" className="panel grid content-start gap-3 p-5">
      <h2 id="signals-heading" className="text-lg">
        Heads-up
      </h2>
      {signals.length === 0 ? (
        <p className="text-sm text-slate-600">Nothing needs your attention. Cards show up here when a price is falling steadily or the model expects a move.</p>
      ) : (
        <ul className="grid gap-3">
          {signals.slice(0, 6).map((s) => (
            <li key={`${s.cardId}-${s.variant}`} className="flex gap-3">
              {s.imageUrl ? <img src={s.imageUrl} alt="" className="h-14 w-10 shrink-0 rounded object-cover" loading="lazy" /> : null}
              <div className="grid min-w-0 gap-0.5 text-sm">
                <span className="flex flex-wrap items-center gap-2">
                  <Link to={`/cards/${encodeURIComponent(s.cardId)}`} className="truncate font-semibold text-slate-900 hover:text-pokemon-blue">
                    {s.name}
                  </Link>
                  <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${SIGNAL_STYLE[s.kind].className}`}>{SIGNAL_STYLE[s.kind].label}</span>
                </span>
                <span className="text-xs text-slate-500">
                  {s.setName} · {s.variantLabel} · {s.quantity}× {formatPrice(s.valueEach)}
                </span>
                <span className="text-xs text-slate-700">{s.reasons.join(' ')}</span>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// ------------------------------------------------------------------ holdings

const SORTS = {
  value: { label: 'Value', compare: (a: CollectionEntry, b: CollectionEntry) => (b.total ?? -1) - (a.total ?? -1) },
  gain: { label: 'Gain %', compare: (a: CollectionEntry, b: CollectionEntry) => (b.gainPercent ?? -Infinity) - (a.gainPercent ?? -Infinity) },
  name: { label: 'Name', compare: (a: CollectionEntry, b: CollectionEntry) => a.name.localeCompare(b.name) },
  added: { label: 'Recently added', compare: (a: CollectionEntry, b: CollectionEntry) => b.addedAt.localeCompare(a.addedAt) },
} as const;

function Holdings({ items }: { items: CollectionEntry[] }) {
  const [sort, setSort] = useState<keyof typeof SORTS>('value');
  const [filter, setFilter] = useState('');
  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return items
      .filter((i) => !needle || i.name.toLowerCase().includes(needle) || i.setName.toLowerCase().includes(needle))
      .sort(SORTS[sort].compare);
  }, [items, filter, sort]);

  return (
    <section aria-labelledby="holdings-heading" className="grid gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 id="holdings-heading" className="text-2xl">
          Cards
        </h2>
        <div className="flex flex-wrap gap-3">
          <label className="grid gap-1 text-sm font-medium">
            Find
            <input className="input w-52" type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Card or set" />
          </label>
          <label className="grid gap-1 text-sm font-medium">
            Sort by
            <select className="input w-44" value={sort} onChange={(e) => setSort(e.target.value as keyof typeof SORTS)}>
              {Object.entries(SORTS).map(([key, { label }]) => (
                <option key={key} value={key}>
                  {label}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>
      <ul className="grid gap-2">
        {shown.map((item) => (
          <HoldingRow key={item.id} item={item} />
        ))}
      </ul>
    </section>
  );
}

function HoldingRow({ item }: { item: CollectionEntry }) {
  const { update, remove } = useCollectionActions();
  const [cost, setCost] = useState(item.costEach?.toFixed(2) ?? '');
  const saveCost = () => {
    const next = cost.trim() === '' ? null : Number(cost);
    if (next === item.costEach || (next !== null && Number.isNaN(next))) return;
    update.mutate({ id: item.id, request: next === null ? { clearCost: true } : { costEach: next } });
  };

  return (
    <li className="panel grid gap-3 p-3 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:items-center">
      <div className="flex min-w-0 items-center gap-3 sm:contents">
        {item.imageUrl ? (
          <img src={item.imageUrl} alt="" loading="lazy" className="h-20 w-14 shrink-0 rounded object-cover" />
        ) : (
          <span className="h-20 w-14 shrink-0 rounded bg-slate-200" aria-hidden="true" />
        )}
        <div className="grid min-w-0 gap-1">
          <Link to={`/cards/${encodeURIComponent(item.cardId)}`} className="truncate font-semibold text-slate-900 hover:text-pokemon-blue">
            {item.name}
          </Link>
          <span className="truncate text-xs text-slate-500">
            {item.setName} · #{item.number} · {item.variantLabel}
          </span>
          <span>
            <ConfidenceBadge confidence={item.confidence} />
          </span>
        </div>
      </div>

      <div className="flex flex-wrap items-end gap-3 sm:justify-end">
        <label className="grid gap-0.5 text-xs font-medium text-slate-500">
          Qty
          <input
            className="input h-9 w-20"
            type="number"
            min={1}
            max={9999}
            defaultValue={item.quantity}
            aria-label={`Quantity of ${item.name}`}
            onBlur={(e) => {
              const q = Number(e.target.value);
              if (q >= 1 && q !== item.quantity) update.mutate({ id: item.id, request: { quantity: q } });
            }}
          />
        </label>
        <label className="grid gap-0.5 text-xs font-medium text-slate-500">
          Condition
          <select
            className="input h-9 w-40"
            value={item.condition}
            aria-label={`Condition of ${item.name}`}
            onChange={(e) => update.mutate({ id: item.id, request: { condition: e.target.value as CardCondition } })}
          >
            {CONDITIONS.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-0.5 text-xs font-medium text-slate-500">
          Paid each
          <input
            className="input h-9 w-24"
            type="number"
            min={0}
            step="0.01"
            inputMode="decimal"
            value={cost}
            placeholder="—"
            aria-label={`Price paid for each ${item.name}`}
            onChange={(e) => setCost(e.target.value)}
            onBlur={saveCost}
          />
        </label>
        <div className="grid min-w-24 gap-0.5 text-right">
          <span className="text-xs text-slate-500">{formatPrice(item.valueEach)} each</span>
          <span className="price text-lg text-slate-900">{formatPrice(item.total)}</span>
          {item.gainPercent !== null && <span className={`text-xs ${item.gainPercent >= 0 ? 'gain' : 'loss'}`}>{formatChange(item.gainPercent)}</span>}
        </div>
        <button type="button" className="btn-ghost h-9 px-2 text-sm" onClick={() => remove.mutate(item.id)} aria-label={`Remove ${item.name}`}>
          ✕
        </button>
      </div>
      {update.error && (
        <p role="alert" className="text-sm text-red-700 sm:col-span-3">
          {update.error.message}
        </p>
      )}
    </li>
  );
}

// ------------------------------------------------------------------ goals

function Goals({ goals }: { goals: GoalProgress[] }) {
  return (
    <section aria-labelledby="goals-heading" className="grid gap-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="goals-heading" className="text-2xl">
          Set goals
        </h2>
        <Link to="/sets" className="text-sm font-semibold text-pokemon-blue hover:underline">
          Start a set →
        </Link>
      </div>
      {goals.length === 0 ? (
        <p className="panel p-5 text-sm text-slate-600">
          Chasing a set? Open it and pick <strong>Main set</strong>, <strong>Full set</strong> or <strong>Master set</strong> (every
          card in every printing) to track it here with what the rest costs.
        </p>
      ) : (
        <ul className="grid gap-3 md:grid-cols-2">
          {goals.map((goal) => (
            <li key={goal.setId}>
              <Link to={`/sets/${goal.setId}?show=missing`} className="panel grid gap-2 p-4 transition hover:border-pokemon-blue">
                <span className="flex items-center justify-between gap-3">
                  <span className="flex min-w-0 items-center gap-2 font-semibold text-slate-900">
                    {goal.symbolUrl && <img src={goal.symbolUrl} alt="" className="h-5 w-5 object-contain" />}
                    <span className="truncate">{goal.setName}</span>
                    <span className="shrink-0 rounded-full bg-pokemon-pokeblue/10 px-2 py-0.5 text-[11px] font-bold text-pokemon-pokeblue">
                      {GOALS.find((g) => g.value === goal.kind)!.label}
                    </span>
                  </span>
                  <span className="text-sm tabular-nums text-slate-600">
                    {goal.owned} / {goal.total}
                  </span>
                </span>
                <span className="h-2 overflow-hidden rounded-full bg-slate-100">
                  <span className="block h-full rounded-full bg-pokemon-blue" style={{ width: `${Math.min(100, goal.completion)}%` }} />
                </span>
                <span className="text-xs text-slate-500">
                  {Math.round(goal.completion)}% complete · about {formatTotal(goal.costToComplete)} to finish
                  {goal.unpriced > 0 && ` (${goal.unpriced} unpriced)`}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// ------------------------------------------------------------------ sets

function SetsProgress({ sets }: { sets: SetProgress[] }) {
  if (sets.length === 0) return null;
  return (
    <section aria-labelledby="sets-heading" className="grid gap-4">
      <h2 id="sets-heading" className="text-2xl">
        Other sets you have cards from
      </h2>
      <ul className="grid gap-3 md:grid-cols-2">
        {sets.map((set) => (
          <li key={set.setId}>
            <Link to={`/sets/${set.setId}?show=missing`} className="panel grid gap-2 p-4 transition hover:border-pokemon-blue">
              <span className="flex items-center justify-between gap-3">
                <span className="flex min-w-0 items-center gap-2 font-semibold text-slate-900">
                  {set.symbolUrl && <img src={set.symbolUrl} alt="" className="h-5 w-5 object-contain" />}
                  <span className="truncate">{set.setName}</span>
                </span>
                <span className="text-sm tabular-nums text-slate-600">
                  {set.owned} / {set.printedTotal}
                </span>
              </span>
              <span className="h-2 overflow-hidden rounded-full bg-slate-100" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(set.completion)} aria-label={`${set.setName} completion`}>
                <span className="block h-full rounded-full bg-pokemon-blue" style={{ width: `${Math.min(100, set.completion)}%` }} />
              </span>
              <span className="text-xs text-slate-500">
                {Math.round(set.completion)}% complete
                {set.costToComplete !== null && ` · about ${formatTotal(set.costToComplete)} to finish the main set`}
                {set.unpricedMissing > 0 && ` (${set.unpricedMissing} unpriced)`}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
