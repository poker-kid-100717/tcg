import { screen, within } from '@testing-library/react';

import type { DownTrend, Sleepers } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import MarketPage from './MarketPage';

const card = {
  number: '6',
  setId: 'sv3pt5',
  setName: '151',
  imageUrl: null,
  tcgplayerUrl: null,
  variant: 'holofoil',
  variantLabel: 'Holofoil',
};

const downtrend: DownTrend = {
  from: '2026-08-25',
  to: '2026-09-24',
  days: 30,
  daysOfHistory: 31,
  cards: [
    {
      ...card,
      cardId: 'sv3pt5-6',
      name: 'Charizard ex',
      from: 40,
      to: 28,
      changePercent: -30,
      fit: 0.97,
      points: [
        { date: '2026-08-25', market: 40 },
        { date: '2026-09-10', market: 34 },
        { date: '2026-09-24', market: 28 },
      ],
    },
  ],
};

const sleepers: Sleepers = {
  asOf: '2026-09-24',
  cards: [
    { ...card, cardId: 'sv3pt5-25', name: 'Pikachu', market: 20, low: 26, mid: 24, listingGapPercent: 30, change30Percent: 5.3 },
  ],
};

const base = {
  '/api/market/movers': { from: null, to: null, gainers: [], losers: [] },
  '/api/market/top': [],
  '/api/market/status': { lastSnapshotAt: null, cardsSeen: null, pricesWritten: null, daysOfHistory: 31 },
};

describe('MarketPage', () => {
  it('lists cards trending down with their price line, and sleepers with the listing gap', async () => {
    mockApi({ ...base, '/api/market/downtrend': downtrend, '/api/market/sleepers': sleepers });
    renderRoute('/market', '/market', <MarketPage />);

    const trending = await screen.findByRole('region', { name: 'Trending down' });
    const charizard = await within(trending).findByRole('link', { name: /Charizard ex/ });
    expect(charizard).toHaveAttribute('href', '/cards/sv3pt5-6');
    expect(charizard).toHaveTextContent('-30.0%');
    expect(within(charizard).getByRole('img', { name: '$40.00 to $28.00 over 3 days' })).toBeInTheDocument();

    const sleeping = screen.getByRole('region', { name: 'Sleepers' });
    const pikachu = await within(sleeping).findByRole('link', { name: /Pikachu/ });
    expect(pikachu).toHaveTextContent('+30.0% listing gap · 30 days +5.3%');
    expect(pikachu).toHaveTextContent('sells $20.00');
    expect(pikachu).toHaveTextContent('listed from $26.00');
    expect(screen.getByText(/not financial advice/)).toBeInTheDocument();
  });

  it('explains an empty trend list while history is short', async () => {
    mockApi({
      ...base,
      '/api/market/downtrend': { ...downtrend, daysOfHistory: 2, cards: [] },
      '/api/market/sleepers': { asOf: null, cards: [] },
    });
    renderRoute('/market', '/market', <MarketPage />);

    expect(await screen.findByText('Needs at least 5 days of prices; 2 recorded so far.')).toBeInTheDocument();
    expect(await screen.findByText('No sleepers in the latest prices.')).toBeInTheDocument();
  });
});
