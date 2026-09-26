import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';

import { useCollectionActions, useOwnership } from '../../api/collection';
import type { CardCondition, CardDetail } from '../../api/types';
import { CONDITIONS, conditionLabel } from '../../lib/conditions';
import { formatPrice } from '../../lib/format';

/** The card page's "I have this" panel: what's owned already, add copies, or wish for it with a target price. */
export function AddToCollection({ card }: { card: CardDetail }) {
  const ownership = useOwnership(card.id);
  const actions = useCollectionActions();
  const printings = card.prices.length > 0 ? card.prices : [{ variant: 'normal', label: 'Normal', market: null }];
  const [variant, setVariant] = useState(printings[0].variant);
  const [condition, setCondition] = useState<CardCondition>('NearMint');
  const [quantity, setQuantity] = useState('1');
  const [cost, setCost] = useState('');
  const [target, setTarget] = useState('');
  const [added, setAdded] = useState<string | null>(null);

  const owned = ownership.data?.items ?? [];
  const wish = ownership.data?.wish ?? null;
  const market = printings.find((p) => p.variant === variant)?.market ?? null;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setAdded(null);
    const count = Math.max(1, Math.floor(Number(quantity)) || 1);
    actions.add.mutate(
      { cardId: card.id, variant, condition, quantity: count, costEach: cost === '' ? null : Number(cost) },
      {
        onSuccess: () => {
          setAdded(`Added ${count} × ${card.name}.`);
          setCost('');
          setQuantity('1');
        },
      },
    );
  };

  return (
    <section aria-labelledby="collect-heading" className="panel grid gap-4 border-pokemon-blue/30 p-5">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="collect-heading" className="text-lg">
          Your collection
        </h2>
        {owned.length > 0 && (
          <Link to="/" className="text-sm font-semibold text-pokemon-pokeblue hover:underline">
            View collection →
          </Link>
        )}
      </div>

      {owned.length > 0 && (
        <ul className="grid gap-2" aria-label="Copies you own">
          {owned.map((item) => (
            <li key={item.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-slate-50 px-3 py-2 text-sm">
              <span>
                <strong className="tabular-nums">{item.quantity}×</strong> {item.variantLabel} · {conditionLabel(item.condition)}
                {item.costEach !== null && <span className="text-slate-500"> · paid {formatPrice(item.costEach)} each</span>}
              </span>
              <span className="flex items-center gap-3">
                <span className="price">{formatPrice(item.total)}</span>
                <button
                  type="button"
                  className="text-xs font-semibold text-red-700 hover:underline"
                  onClick={() => actions.remove.mutate(item.id)}
                  aria-label={`Remove ${item.variantLabel} ${conditionLabel(item.condition)} copies`}
                >
                  Remove
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={submit} className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <label className="grid gap-1 text-sm font-medium">
          Printing
          <select className="input" value={variant} onChange={(e) => setVariant(e.target.value)}>
            {printings.map((p) => (
              <option key={p.variant} value={p.variant}>
                {p.label}
                {p.market !== null ? ` (${formatPrice(p.market)})` : ''}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-sm font-medium">
          Condition
          <select className="input" value={condition} onChange={(e) => setCondition(e.target.value as CardCondition)}>
            {CONDITIONS.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-sm font-medium">
          Quantity
          <input className="input" type="number" min={1} max={9999} required value={quantity} onChange={(e) => setQuantity(e.target.value)} />
        </label>
        <label className="grid gap-1 text-sm font-medium">
          Paid each <span className="font-normal text-slate-500">(optional)</span>
          <input
            className="input"
            type="number"
            min={0}
            step="0.01"
            inputMode="decimal"
            value={cost}
            placeholder={market !== null ? market.toFixed(2) : '0.00'}
            onChange={(e) => setCost(e.target.value)}
          />
        </label>
        <div className="flex flex-wrap items-center gap-3 sm:col-span-2 lg:col-span-4">
          <button type="submit" className="btn bg-pokemon-pokeblue text-white hover:brightness-110" disabled={actions.add.isPending}>
            {actions.add.isPending ? 'Adding…' : 'Add to collection'}
          </button>
          <p role="status" className="text-sm">
            {added && <span className="text-emerald-700">{added}</span>}
            {actions.add.error && <span className="text-red-700">{actions.add.error.message}</span>}
          </p>
        </div>
      </form>

      <div className="grid gap-2 border-t border-slate-100 pt-4">
        {wish ? (
          <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
            <p>
              <span aria-hidden="true">★ </span>On your wishlist
              {wish.targetPrice !== null && <> · alert at {formatPrice(wish.targetPrice)}</>}
              {wish.atOrBelowTarget && <strong className="ml-2 text-emerald-700">At your price now</strong>}
            </p>
            <button type="button" className="btn-ghost px-2 py-1 text-sm" onClick={() => actions.unwish.mutate(wish.id)}>
              Remove from wishlist
            </button>
          </div>
        ) : (
          <form
            className="flex flex-wrap items-end gap-3"
            onSubmit={(event) => {
              event.preventDefault();
              actions.wish.mutate({ cardId: card.id, variant, targetPrice: target === '' ? null : Number(target) });
            }}
          >
            <label className="grid gap-1 text-sm font-medium">
              Want it? Target price <span className="font-normal text-slate-500">(optional)</span>
              <input
                className="input w-40"
                type="number"
                min={0}
                step="0.01"
                inputMode="decimal"
                value={target}
                placeholder={market !== null ? (market * 0.9).toFixed(2) : ''}
                onChange={(e) => setTarget(e.target.value)}
              />
            </label>
            <button type="submit" className="btn border border-slate-300 bg-white text-slate-800 hover:border-pokemon-blue" disabled={actions.wish.isPending}>
              <span aria-hidden="true">☆</span> Add to wishlist
            </button>
          </form>
        )}
      </div>
    </section>
  );
}
