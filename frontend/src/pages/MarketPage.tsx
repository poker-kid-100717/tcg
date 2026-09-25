import { useSearchParams } from 'react-router-dom';

import { useDownTrend, useMarketStatus, useMovers, useSleepers, useTopCards } from '../api/hooks';
import { CardTile } from '../components/CardTile';
import { SleepersList, TrendingDownList } from '../components/MarketSignals';
import { MoversTable } from '../components/MoversTable';
import { ErrorState, Loading } from '../components/States';
import { formatDate } from '../lib/format';

const WINDOWS = [
  { days: 1, label: '24 hours' },
  { days: 7, label: '7 days' },
  { days: 30, label: '30 days' },
];

export default function MarketPage() {
  const [params, setParams] = useSearchParams();
  const days = WINDOWS.some((w) => String(w.days) === params.get('days')) ? Number(params.get('days')) : 7;
  const movers = useMovers(days);
  const top = useTopCards();
  const status = useMarketStatus();
  const downtrend = useDownTrend();
  const sleepers = useSleepers();

  return (
    <div className="container-custom grid gap-10 py-8 sm:py-10">
      <div className="grid gap-2">
        <p className="eyebrow">Market overview</p>
        <h1 className="text-3xl sm:text-4xl">Price movers</h1>
        <p className="max-w-2xl text-slate-600">
          The largest changes in TCGplayer market price, comparing the latest daily snapshot with the one from the start of
          the window. Cards under $2 are left out, so a few cents of movement on a common doesn&apos;t top the list.
          {status.data && ` ${status.data.daysOfHistory} days of history recorded so far.`}
        </p>
      </div>

      <div role="tablist" aria-label="Time window" className="flex gap-2">
        {WINDOWS.map((w) => (
          <button
            key={w.days}
            type="button"
            role="tab"
            aria-selected={w.days === days}
            onClick={() => setParams(w.days === 7 ? {} : { days: String(w.days) }, { replace: true })}
            className={`rounded-full px-4 py-1.5 text-sm font-semibold transition ${
              w.days === days ? 'bg-pokemon-pokeblue text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:text-slate-900'
            }`}
          >
            {w.label}
          </button>
        ))}
      </div>

      {movers.isPending ? (
        <Loading label="Loading price moves…" />
      ) : movers.error ? (
        <ErrorState error={movers.error} onRetry={() => movers.refetch()} />
      ) : movers.data.from ? (
        <div className="grid gap-2">
          <p className="text-sm text-slate-500">
            {formatDate(movers.data.from)} → {formatDate(movers.data.to)}
          </p>
          <div className="grid gap-4 lg:grid-cols-2">
            <MoversTable title="Biggest gains" moves={movers.data.gainers} tone="gain" />
            <MoversTable title="Biggest drops" moves={movers.data.losers} tone="loss" />
          </div>
        </div>
      ) : (
        <p className="panel p-6 text-slate-600">
          Not enough history for this window yet. Prices are recorded once a day; check back after a few more snapshots.
        </p>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <section id="trending-down" aria-labelledby="trending-down-title" className="panel overflow-hidden">
          <div className="grid gap-1 border-b border-slate-200 px-4 py-3">
            <h2 id="trending-down-title" className="text-lg">
              Trending down
            </h2>
            <p className="text-xs text-slate-500">
              A steady fall over the last 30 days, not one bad day: a straight line through the daily prices has to slope
              down and fit closely, with at least 5 days of prices and a drop of 10% or more.
            </p>
          </div>
          {downtrend.isPending ? (
            <Loading label="Loading trends…" />
          ) : downtrend.error ? (
            <ErrorState error={downtrend.error} onRetry={() => downtrend.refetch()} />
          ) : (
            <TrendingDownList
              cards={downtrend.data.cards}
              empty={
                downtrend.data.daysOfHistory < 5
                  ? `Needs at least 5 days of prices; ${downtrend.data.daysOfHistory} recorded so far.`
                  : 'Nothing over $2 is in a steady decline right now.'
              }
            />
          )}
        </section>

        <section id="sleepers" aria-labelledby="sleepers-title" className="panel overflow-hidden">
          <div className="grid gap-1 border-b border-slate-200 px-4 py-3">
            <h2 id="sleepers-title" className="text-lg">
              Sleepers
            </h2>
            <p className="text-xs text-slate-500">
              Quiet cards with nothing listed near what they sell for: the cheapest TCGplayer listing is at least 10% above
              the market price, and the market price has moved less than 15% in 30 days. Thin supply often comes before a
              price catches up.
            </p>
          </div>
          {sleepers.isPending ? (
            <Loading label="Loading sleepers…" />
          ) : sleepers.error ? (
            <ErrorState error={sleepers.error} onRetry={() => sleepers.refetch()} />
          ) : (
            <SleepersList cards={sleepers.data.cards} empty="No sleepers in the latest prices." />
          )}
        </section>
      </div>
      <p className="-mt-6 text-xs text-slate-500">
        These are signals from TCGplayer prices, not financial advice. Check the listings before you buy.
      </p>

      {top.data && top.data.length > 0 && (
        <section className="grid gap-4">
          <h2 className="text-2xl">Most valuable cards</h2>
          <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
            {top.data.map((card) => (
              <li key={`${card.cardId}-${card.variantLabel}`}>
                <CardTile
                  id={card.cardId}
                  name={card.name}
                  number={card.number}
                  imageUrl={card.imageUrl}
                  price={card.market}
                  subtitle={`${card.setName} · ${card.variantLabel}`}
                />
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
