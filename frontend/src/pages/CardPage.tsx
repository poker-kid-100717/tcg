import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import { useCard, useCardPredictions } from '../api/hooks';
import { MarketIntelligencePanel } from '../components/MarketIntelligence';
import { CardOutlook } from '../components/Predictions';
import { PriceHistoryChart } from '../components/PriceHistoryChart';
import { ShopLink } from '../components/ShopLink';
import { WatchButton } from '../components/WatchButton';
import { ErrorState, Loading } from '../components/States';
import { formatDate, formatPrice } from '../lib/format';

export default function CardPage() {
  const { cardId = '' } = useParams();
  const { data: card, isPending, error, refetch } = useCard(cardId);
  const outlook = useCardPredictions(cardId);
  const [variant, setVariant] = useState('');

  useEffect(() => {
    if (!card) return;
    const first = card.prices.find((p) => p.market !== null) ?? card.prices[0];
    setVariant((current) => current && card.prices.some((p) => p.variant === current) ? current : (first?.variant ?? ''));
  }, [card?.id]);

  if (isPending) return <Loading label="Loading card…" />;
  if (error) return <ErrorState error={error} onRetry={() => refetch()} />;

  const headline =
    card.prices.find((p) => p.variant === variant) ??
    card.prices.find((p) => p.market !== null) ??
    card.prices[0];
  const details: [string, string | null][] = [
    ['Set', card.set.name],
    ['Number', `${card.number} / ${card.set.printedTotal}`],
    ['Rarity', card.rarity],
    ['Released', formatDate(card.set.releaseDate)],
    ['Type', [card.supertype, ...card.subtypes].filter(Boolean).join(' · ') || null],
    ['HP', card.hp],
    ['Artist', card.artist],
  ];

  return (
    <div className="container-custom py-8 sm:py-10">
      <nav aria-label="Breadcrumb" className="mb-6 text-sm text-slate-500">
        <Link to="/sets" className="hover:text-slate-900">
          Sets
        </Link>{' '}
        /{' '}
        <Link to={`/sets/${card.set.id}`} className="hover:text-slate-900">
          {card.set.name}
        </Link>{' '}
        / <span className="text-slate-900">{card.name}</span>
      </nav>

      <div className="grid gap-8 lg:grid-cols-[minmax(0,340px)_1fr]">
        <div className="mx-auto w-full max-w-sm">
          {card.largeImageUrl || card.imageUrl ? (
            <img
              src={card.largeImageUrl ?? card.imageUrl ?? ''}
              alt={card.name}
              className="w-full rounded-2xl shadow-[0_18px_40px_-18px_rgba(10,40,95,0.5)]"
            />
          ) : (
            <div className="aspect-[5/7] w-full rounded-2xl bg-slate-200" aria-hidden="true" />
          )}
        </div>

        <div className="grid content-start gap-6">
          <div className="grid gap-2">
            <p className="eyebrow">{card.set.series}</p>
            <h1 className="text-3xl sm:text-4xl">{card.name}</h1>
            <p className="text-slate-600">
              {card.set.name} · #{card.number}
              {card.rarity ? ` · ${card.rarity}` : ''}
            </p>
          </div>

          <section aria-labelledby="price-heading" className="panel grid gap-4 p-5">
            <div className="flex flex-wrap items-end justify-between gap-4">
              <div>
                <h2 id="price-heading" className="text-sm font-semibold text-slate-500">
                  TCGplayer market price{headline ? ` · ${headline.label}` : ''}
                </h2>
                <p className="price text-4xl text-slate-900">{formatPrice(headline?.market)}</p>
                <p className="text-xs text-slate-500">Updated {formatDate(card.pricesUpdated)}</p>
              </div>
              <div className="flex flex-wrap gap-2">
                <WatchButton
                  input={{
                    cardId: card.id,
                    variant: headline?.variant ?? variant,
                    cardName: card.name,
                    setName: card.set.name,
                    imageUrl: card.imageUrl,
                    targetBelow: null,
                    targetAbove: null,
                    movePercent: 10,
                  }}
                />
                <Link to={`/deal?card=${encodeURIComponent(card.id)}`} className="btn bg-white text-pokemon-pokeblue ring-1 ring-pokemon-pokeblue">
                  Analyze deal
                </Link>
                <ShopLink url={card.tcgplayerUrl} cardName={card.name} />
              </div>
            </div>

            {card.prices.length > 1 && (
              <label className="grid max-w-sm gap-1 text-sm font-semibold text-slate-700">
                Printing to analyze
                <select className="input" value={headline?.variant ?? variant} onChange={(event) => setVariant(event.target.value)}>
                  {card.prices.map((price) => (
                    <option key={price.variant} value={price.variant}>
                      {price.label} · {formatPrice(price.market)}
                    </option>
                  ))}
                </select>
              </label>
            )}

            {card.prices.length > 0 ? (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[420px] text-sm">
                  <caption className="sr-only">TCGplayer prices by printing</caption>
                  <thead>
                    <tr className="border-b border-slate-200 text-left text-slate-500">
                      <th scope="col" className="py-2 font-medium">Printing</th>
                      <th scope="col" className="py-2 text-right font-medium">Market</th>
                      <th scope="col" className="py-2 text-right font-medium">Low</th>
                      <th scope="col" className="py-2 text-right font-medium">Mid</th>
                      <th scope="col" className="py-2 text-right font-medium">High</th>
                    </tr>
                  </thead>
                  <tbody>
                    {card.prices.map((p) => (
                      <tr key={p.variant} className="border-b border-slate-100 last:border-0">
                        <th scope="row" className="py-2 text-left font-medium text-slate-800">{p.label}</th>
                        <td className="price py-2 text-right text-slate-900">{formatPrice(p.market)}</td>
                        <td className="py-2 text-right tabular-nums text-slate-600">{formatPrice(p.low)}</td>
                        <td className="py-2 text-right tabular-nums text-slate-600">{formatPrice(p.mid)}</td>
                        <td className="py-2 text-right tabular-nums text-slate-600">{formatPrice(p.high)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <p className="text-sm text-slate-600">TCGplayer doesn&apos;t list a price for this card yet.</p>
            )}
          </section>

          {headline?.variant && <MarketIntelligencePanel cardId={card.id} variant={headline.variant} />}

          <section aria-labelledby="history-heading" className="panel grid gap-3 p-5">
            <h2 id="history-heading" className="text-lg">Price history</h2>
            <PriceHistoryChart history={card.history} />
          </section>

          {outlook.data && (
            <section aria-labelledby="outlook-heading" className="panel grid gap-3 p-5">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <h2 id="outlook-heading" className="text-lg">
                  {outlook.data.horizonDays || 30}-day outlook
                </h2>
                <Link to="/outlook" className="text-sm font-semibold text-pokemon-pokeblue hover:underline">
                  How predictions work →
                </Link>
              </div>
              <CardOutlook list={outlook.data} />
            </section>
          )}

          <section aria-labelledby="details-heading" className="panel p-5">
            <h2 id="details-heading" className="mb-3 text-lg">Card details</h2>
            <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
              {details
                .filter(([, value]) => value)
                .map(([term, value]) => (
                  <div key={term} className="flex justify-between gap-4 border-b border-slate-100 py-1.5">
                    <dt className="text-slate-500">{term}</dt>
                    <dd className="text-right font-medium text-slate-900">{value}</dd>
                  </div>
                ))}
            </dl>
            {card.flavorText && <p className="mt-4 text-sm text-slate-600 italic">{card.flavorText}</p>}
          </section>
        </div>
      </div>
    </div>
  );
}
