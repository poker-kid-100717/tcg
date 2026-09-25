import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { useSets } from '../api/hooks';
import type { SetSummary } from '../api/types';
import { ErrorState, Loading } from '../components/States';
import { formatDate } from '../lib/format';

export default function SetsPage() {
  const { data, isPending, error, refetch } = useSets();
  const [filter, setFilter] = useState('');

  // Group by series, newest series first (by their newest set).
  const series = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    const groups = new Map<string, SetSummary[]>();
    for (const set of data ?? []) {
      if (needle && !set.name.toLowerCase().includes(needle) && !set.series.toLowerCase().includes(needle)) continue;
      groups.set(set.series, [...(groups.get(set.series) ?? []), set]);
    }
    return [...groups.entries()].map(([name, sets]) => ({
      name,
      sets: sets.sort((a, b) => (b.releaseDate ?? '').localeCompare(a.releaseDate ?? '')),
    }));
  }, [data, filter]);

  if (isPending) return <Loading label="Loading sets…" />;
  if (error) return <ErrorState error={error} onRetry={() => refetch()} />;

  return (
    <div className="container-custom py-8 sm:py-10">
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div className="grid gap-1">
          <p className="eyebrow">Set guide</p>
          <h1 className="text-3xl sm:text-4xl">Every Pokémon TCG set</h1>
          <p className="text-slate-600">{data.length} sets. Open one to see every card and what it&apos;s worth.</p>
        </div>
        <label className="grid gap-1 text-sm font-medium">
          Filter sets
          <input className="input w-64" type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Set or series name" />
        </label>
      </div>

      {series.length === 0 && <p className="panel p-10 text-center text-slate-500">No sets match “{filter}”.</p>}

      <div className="grid gap-10">
        {series.map((group) => (
          <section key={group.name} aria-labelledby={`series-${group.name}`} className="grid gap-4">
            <h2 id={`series-${group.name}`} className="text-xl">
              {group.name} <span className="text-sm font-medium text-slate-400">{group.sets.length} sets</span>
            </h2>
            <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
              {group.sets.map((set) => (
                <li key={set.id}>
                  <Link
                    to={`/sets/${set.id}`}
                    className="panel flex h-full flex-col gap-3 p-4 transition hover:border-pokemon-blue hover:shadow-sm"
                  >
                    <span className="flex h-16 items-center justify-center">
                      {set.logoUrl ? (
                        <img src={set.logoUrl} alt="" loading="lazy" className="max-h-16 max-w-full object-contain" />
                      ) : null}
                    </span>
                    <span className="flex items-center gap-2">
                      {set.symbolUrl && <img src={set.symbolUrl} alt="" loading="lazy" className="h-4 w-4 object-contain" />}
                      <span className="line-clamp-1 font-semibold text-slate-900">{set.name}</span>
                    </span>
                    <span className="flex justify-between text-xs text-slate-500">
                      <span>{formatDate(set.releaseDate, 'short')}</span>
                      <span>{set.total} cards</span>
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}
