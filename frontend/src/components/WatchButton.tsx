import { useAddWatch, useWatchlist } from '../api/hooks';
import type { WatchlistInput } from '../api/types';

export function WatchButton({ input }: { input: WatchlistInput }) {
  const watchlist = useWatchlist();
  const add = useAddWatch();
  const watched = watchlist.data?.some((item) => item.cardId === input.cardId && item.variant === input.variant);

  return (
    <button
      type="button"
      className={watched ? 'btn bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200' : 'btn bg-pokemon-pokeblue text-white hover:brightness-110'}
      disabled={add.isPending || watched}
      onClick={() => add.mutate(input)}
    >
      {watched ? 'Watching' : add.isPending ? 'Adding…' : 'Watch price'}
    </button>
  );
}
