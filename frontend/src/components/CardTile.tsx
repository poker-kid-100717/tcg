import { Link } from 'react-router-dom';

import { formatPrice } from '../lib/format';

export interface CardTileProps {
  id: string;
  name: string;
  number: string;
  imageUrl: string | null;
  price: number | null;
  subtitle?: string;
  badge?: React.ReactNode;
}

/** A card in a grid: image, name, collector number and its TCGplayer market price. */
export function CardTile({ id, name, number, imageUrl, price, subtitle, badge }: CardTileProps) {
  return (
    <Link
      to={`/cards/${encodeURIComponent(id)}`}
      className="group flex flex-col overflow-hidden rounded-xl border border-slate-200 bg-white transition hover:-translate-y-0.5 hover:border-pokemon-blue hover:shadow-md"
    >
      <div className="relative bg-slate-100 p-3">
        {imageUrl ? (
          <img src={imageUrl} alt={name} loading="lazy" className="mx-auto aspect-[5/7] w-full max-w-[220px] object-contain" />
        ) : (
          <div className="mx-auto aspect-[5/7] w-full max-w-[220px] rounded-lg bg-slate-200" aria-hidden="true" />
        )}
        {badge && <div className="absolute top-2 right-2">{badge}</div>}
      </div>
      <div className="flex flex-1 flex-col gap-1 p-3">
        <p className="line-clamp-1 font-semibold text-slate-900 group-hover:text-pokemon-blue">{name}</p>
        <p className="line-clamp-1 text-xs text-slate-500">{subtitle ?? `#${number}`}</p>
        <p className="price mt-auto pt-1 text-lg text-slate-900">{formatPrice(price)}</p>
      </div>
    </Link>
  );
}
