import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';

import { useCreateMasterSet, useMasterSets, useSets } from '../api/hooks';
import { ErrorState, Loading } from '../components/States';
import { formatPrice } from '../lib/format';

export default function MasterSetsPage() {
  const masterSets = useMasterSets();
  const sets = useSets();
  const create = useCreateMasterSet();
  const navigate = useNavigate();
  const [setId, setSetId] = useState('');

  if (masterSets.isPending || sets.isPending) return <Loading label="Loading master sets…" />;
  if (masterSets.error) return <ErrorState error={masterSets.error} onRetry={() => masterSets.refetch()} />;
  if (sets.error) return <ErrorState error={sets.error} onRetry={() => sets.refetch()} />;

  const add = async () => {
    if (!setId) return;
    const result = await create.mutateAsync(setId);
    navigate(`/master-sets/${result.id}`);
  };

  return (
    <div className="container-custom grid gap-8 py-8 sm:py-10">
      <header className="grid gap-2">
        <p className="eyebrow">Collection completion</p>
        <h1 className="text-3xl sm:text-4xl">Master Sets</h1>
        <p className="max-w-3xl text-slate-600">
          Build a complete printing checklist, track what you own, see the estimated cost to finish, and turn missing cards into buy-or-watch decisions.
        </p>
      </header>

      <section className="panel grid gap-3 p-5 sm:grid-cols-[1fr_auto] sm:items-end">
        <label className="grid gap-1 text-sm font-semibold">
          Start a master set
          <select className="input" value={setId} onChange={(e) => setSetId(e.target.value)}>
            <option value="">Choose a Pokémon set…</option>
            {sets.data.map((set) => (
              <option key={set.id} value={set.id}>{set.name} · {set.series}</option>
            ))}
          </select>
        </label>
        <button type="button" className="btn bg-pokemon-pokeblue text-white" disabled={!setId || create.isPending} onClick={add}>
          {create.isPending ? 'Building checklist…' : 'Build Master Set'}
        </button>
        {create.error && <p className="text-sm text-red-700 sm:col-span-2">{create.error.message}</p>}
      </section>

      {masterSets.data.length === 0 ? (
        <div className="panel p-10 text-center text-slate-600">
          No master sets yet. Pick a set above and TCG Signal will create the full printing checklist.
        </div>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {masterSets.data.map((set) => (
            <Link key={set.id} to={`/master-sets/${set.id}`} className="panel grid gap-4 p-5 hover:border-pokemon-blue">
              <div className="flex items-center gap-4">
                {set.logoUrl && <img src={set.logoUrl} alt="" className="h-14 w-24 object-contain" />}
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{set.setSeries}</p>
                  <h2 className="text-xl">{set.setName}</h2>
                </div>
              </div>
              <div>
                <div className="mb-1 flex justify-between text-sm font-semibold">
                  <span>{set.ownedPrintings} / {set.requiredPrintings} printings</span>
                  <span>{set.completionPercent.toFixed(1)}%</span>
                </div>
                <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                  <div className="h-full bg-pokemon-pokeblue" style={{ width: `${Math.min(100, set.completionPercent)}%` }} />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-3 text-sm">
                <div><p className="text-slate-500">Owned value</p><p className="font-bold">{formatPrice(set.ownedMarketValue)}</p></div>
                <div><p className="text-slate-500">Est. cost to finish</p><p className="font-bold">{formatPrice(set.missingMarketCost)}</p></div>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
