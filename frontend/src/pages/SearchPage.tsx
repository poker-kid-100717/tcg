import { useSearchParams } from 'react-router-dom';

import { useSearch } from '../api/hooks';
import { CardTile } from '../components/CardTile';
import { ErrorState, Loading } from '../components/States';

export default function SearchPage() {
  const [params, setParams] = useSearchParams();
  const q = params.get('q')?.trim() ?? '';
  const page = Math.max(1, Number(params.get('page')) || 1);
  const { data, isPending, isFetching, error, refetch } = useSearch(q, page);

  const goTo = (next: number) => {
    setParams({ q, ...(next > 1 ? { page: String(next) } : {}) });
    window.scrollTo({ top: 0 });
  };

  if (q.length < 2) {
    return (
      <div className="container-custom py-10">
        <h1 className="mb-2 text-3xl">Search cards</h1>
        <p className="text-slate-600">Type at least two letters of a card name in the search box above.</p>
      </div>
    );
  }

  const pages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="container-custom py-8 sm:py-10">
      <div className="mb-6 grid gap-1">
        <p className="eyebrow">Search</p>
        <h1 className="text-3xl">Cards named “{q}”</h1>
        {data && (
          <p className="text-slate-600" aria-live="polite">
            {data.totalCount.toLocaleString()} {data.totalCount === 1 ? 'card' : 'cards'}, newest sets first
          </p>
        )}
      </div>

      {isPending ? (
        <Loading label="Searching…" />
      ) : error ? (
        <ErrorState error={error} onRetry={() => refetch()} />
      ) : data.cards.length === 0 ? (
        <p className="panel p-10 text-center text-slate-500">No cards found. Check the spelling, or try just part of the name.</p>
      ) : (
        <>
          <ul className={`grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5 xl:grid-cols-6 ${isFetching ? 'opacity-60' : ''}`}>
            {data.cards.map((card) => (
              <li key={card.id}>
                <CardTile
                  id={card.id}
                  name={card.name}
                  number={card.number}
                  imageUrl={card.imageUrl}
                  price={card.marketPrice}
                  subtitle={`${card.setName} · #${card.number}`}
                />
              </li>
            ))}
          </ul>
          {pages > 1 && (
            <nav aria-label="Pages" className="mt-8 flex items-center justify-center gap-3">
              <button type="button" className="btn-ghost" disabled={page <= 1} onClick={() => goTo(page - 1)}>
                ← Previous
              </button>
              <span className="text-sm text-slate-600 tabular-nums">
                Page {page} of {pages}
              </span>
              <button type="button" className="btn-ghost" disabled={page >= pages} onClick={() => goTo(page + 1)}>
                Next →
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}
