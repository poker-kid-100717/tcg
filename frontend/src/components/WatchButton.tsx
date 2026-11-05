import { Link } from 'react-router-dom';

import { ApiError } from '../api/client';
import { useAddWatch, useMe, useWatchlist } from '../api/hooks';
import type { WatchlistInput } from '../api/types';
import { SignInLink } from './Gates';

export function WatchButton({ input }: { input: WatchlistInput }) {
  const me = useMe();
  const signedIn = Boolean(me.data?.signedIn);
  const watchlist = useWatchlist(signedIn);
  const add = useAddWatch();
  const watched = watchlist.data?.some((item) => item.cardId === input.cardId && item.variant === input.variant);

  if (!signedIn) return <SignInLink className="btn bg-pokemon-pokeblue text-white hover:brightness-110">Sign in to watch</SignInLink>;

  const limitReached = add.error instanceof ApiError && add.error.needsPro;
  return (
    <div className="grid gap-1">
      <button
        type="button"
        className={watched ? 'btn bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200' : 'btn bg-pokemon-pokeblue text-white hover:brightness-110'}
        disabled={add.isPending || watched}
        onClick={() => add.mutate(input)}
      >
        {watched ? 'Watching' : add.isPending ? 'Adding…' : 'Watch price'}
      </button>
      {limitReached && (
        <p className="text-xs text-slate-600">
          {add.error?.message} <Link to="/pro" className="font-semibold text-pokemon-pokeblue underline">See Pro</Link>
        </p>
      )}
      {add.error && !limitReached && <p className="text-xs text-red-700">{add.error.message}</p>}
    </div>
  );
}
