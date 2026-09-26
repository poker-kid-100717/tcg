import { Link } from 'react-router-dom';

import { useAccount, useCollectionActions, useWishlist } from '../api/collection';
import { ShopLink } from '../components/ShopLink';
import { ErrorState, Loading } from '../components/States';
import { formatPrice } from '../lib/format';

export default function WishlistPage() {
  const account = useAccount();
  const wishlist = useWishlist();
  const { unwish } = useCollectionActions();

  if (account.isPending || (account.data?.signedIn && wishlist.isPending)) return <Loading label="Loading your wishlist…" />;
  if (wishlist.error) return <ErrorState error={wishlist.error} onRetry={() => wishlist.refetch()} />;
  const wishes = wishlist.data ?? [];

  return (
    <div className="container-custom grid gap-6 py-8 sm:py-10">
      <header className="grid gap-1">
        <p className="eyebrow">Wishlist</p>
        <h1 className="text-3xl sm:text-4xl">Cards you want</h1>
        <p className="max-w-2xl text-slate-600">
          Set a target price and the card is flagged the day its market price or cheapest listing reaches it. The suggested
          target is the low end of its last 90 days of prices.
        </p>
      </header>

      {wishes.length === 0 ? (
        <div className="panel grid justify-items-center gap-3 p-10 text-center">
          <p className="text-slate-600">No cards on your wishlist yet. Open any card and choose “Add to wishlist”.</p>
          <Link to="/sets" className="btn bg-pokemon-pokeblue text-white hover:brightness-110">
            Browse sets
          </Link>
        </div>
      ) : (
        <ul className="grid gap-3">
          {wishes.map((w) => (
            <li key={w.id} className={`panel flex flex-wrap items-center gap-4 p-3 ${w.atOrBelowTarget ? 'border-emerald-300 bg-emerald-50/40' : ''}`}>
              {w.imageUrl ? <img src={w.imageUrl} alt="" loading="lazy" className="h-20 w-14 rounded object-cover" /> : <span className="h-20 w-14 rounded bg-slate-200" aria-hidden="true" />}
              <div className="grid min-w-0 flex-1 gap-0.5">
                <Link to={`/cards/${encodeURIComponent(w.cardId)}`} className="truncate font-semibold text-slate-900 hover:text-pokemon-blue">
                  {w.name}
                </Link>
                <span className="truncate text-xs text-slate-500">
                  {w.setName} · #{w.number} · {w.variantLabel}
                </span>
                {w.atOrBelowTarget && <strong className="text-sm text-emerald-700">At or below your target now</strong>}
              </div>
              <dl className="grid grid-cols-3 gap-4 text-right text-sm">
                <div>
                  <dt className="text-xs text-slate-500">Market</dt>
                  <dd className="price">{formatPrice(w.market)}</dd>
                </div>
                <div>
                  <dt className="text-xs text-slate-500">Lowest listing</dt>
                  <dd className="price">{formatPrice(w.low)}</dd>
                </div>
                <div>
                  <dt className="text-xs text-slate-500">Your target</dt>
                  <dd className="price">{formatPrice(w.targetPrice)}</dd>
                  {w.targetPrice === null && w.suggestedTarget !== null && <dd className="text-xs text-slate-500">try {formatPrice(w.suggestedTarget)}</dd>}
                </div>
              </dl>
              <div className="flex items-center gap-2">
                <ShopLink url={w.tcgplayerUrl} cardName={w.name} size="sm" />
                <button type="button" className="btn-ghost px-2 py-1 text-sm" onClick={() => unwish.mutate(w.id)} aria-label={`Remove ${w.name} from wishlist`}>
                  Remove
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
