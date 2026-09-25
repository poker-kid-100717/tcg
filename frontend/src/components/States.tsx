import { Link } from 'react-router-dom';

import { ApiError } from '../api/client';

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div role="status" className="flex items-center justify-center gap-3 py-16 text-slate-500">
      <span className="h-5 w-5 animate-spin rounded-full border-2 border-slate-300 border-t-pokemon-blue" aria-hidden="true" />
      {label}
    </div>
  );
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (error instanceof ApiError && error.status === 404) return <NotFound />;
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  return (
    <div role="alert" className="panel mx-auto my-12 max-w-lg p-6 text-center">
      <h2 className="mb-2 text-xl">Prices didn&apos;t load</h2>
      <p className="mb-4 text-slate-600">{message}</p>
      {onRetry && (
        <button type="button" onClick={onRetry} className="btn bg-pokemon-pokeblue text-white hover:brightness-110">
          Try again
        </button>
      )}
    </div>
  );
}

export function NotFound() {
  return (
    <div className="mx-auto my-16 max-w-md text-center">
      <p className="eyebrow mb-2">404</p>
      <h1 className="mb-3 text-3xl">That card isn&apos;t in the binder</h1>
      <p className="mb-6 text-slate-600">The page or card you were looking for doesn&apos;t exist.</p>
      <Link to="/sets" className="btn bg-pokemon-pokeblue text-white hover:brightness-110">
        Browse sets
      </Link>
    </div>
  );
}
