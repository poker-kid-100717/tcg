import { screen } from '@testing-library/react';

import type { CardDetail } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import CardPage from './CardPage';

vi.mock('../components/PriceHistoryChart', () => ({
  PriceHistoryChart: ({ history }: { history: unknown[] }) => <p>chart with {history.length} points</p>,
}));

const card: CardDetail = {
  id: 'sv3pt5-6',
  name: 'Charizard ex',
  supertype: 'Pokémon',
  subtypes: ['Stage 2', 'ex'],
  hp: '330',
  types: ['Fire'],
  number: '6',
  artist: 'PLANETA Mochizuki',
  rarity: 'Double Rare',
  flavorText: null,
  imageUrl: 'https://images.test/small.png',
  largeImageUrl: 'https://images.test/large.png',
  set: {
    id: 'sv3pt5',
    name: '151',
    series: 'Scarlet & Violet',
    releaseDate: '2023-09-22',
    printedTotal: 165,
    total: 207,
    logoUrl: null,
    symbolUrl: null,
  },
  prices: [
    { variant: 'holofoil', label: 'Holofoil', low: 20, mid: 25, high: 60, market: 24.37 },
    { variant: 'reverseHolofoil', label: 'Reverse Holofoil', low: null, mid: null, high: null, market: null },
  ],
  pricesUpdated: '2026-09-24',
  tcgplayerUrl: 'https://prices.pokemontcg.io/tcgplayer/sv3pt5-6',
  history: [
    { date: '2026-09-17', variant: 'holofoil', market: 22 },
    { date: '2026-09-24', variant: 'holofoil', market: 24.37 },
  ],
};

describe('CardPage', () => {
  it('shows the market price, every printing and a Shop now link to TCGplayer', async () => {
    mockApi({ '/api/cards/sv3pt5-6': card });
    renderRoute('/cards/sv3pt5-6', '/cards/:cardId', <CardPage />);

    expect(await screen.findByRole('heading', { name: 'Charizard ex', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('TCGplayer market price · Holofoil')).toBeInTheDocument();
    expect(screen.getAllByText('$24.37')).toHaveLength(2); // headline + table row

    const rows = screen.getAllByRole('row');
    expect(rows.map((r) => r.querySelector('th')?.textContent)).toEqual(['Printing', 'Holofoil', 'Reverse Holofoil']);

    const shop = screen.getByRole('link', { name: /Shop Charizard ex on TCGplayer/ });
    expect(shop).toHaveAttribute('href', card.tcgplayerUrl);
    expect(shop).toHaveAttribute('target', '_blank');
    expect(shop).toHaveAttribute('rel', 'noopener noreferrer');

    expect(screen.getByText('chart with 2 points')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '151' })).toHaveAttribute('href', '/sets/sv3pt5');
  });

  it('hides the Shop link when TCGplayer has no listing', async () => {
    mockApi({ '/api/cards/sv3pt5-6': { ...card, prices: [], tcgplayerUrl: null } });
    renderRoute('/cards/sv3pt5-6', '/cards/:cardId', <CardPage />);

    expect(await screen.findByText("TCGplayer doesn't list a price for this card yet.")).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /TCGplayer/ })).not.toBeInTheDocument();
  });

  it('shows a not-found page for an unknown card', async () => {
    mockApi({});
    renderRoute('/cards/nope', '/cards/:cardId', <CardPage />);

    expect(await screen.findByRole('heading', { name: "That card isn't in the binder" })).toBeInTheDocument();
  });

  it('explains an upstream outage and offers a retry', async () => {
    mockApi({ '/api/cards/sv3pt5-6': 502 });
    renderRoute('/cards/sv3pt5-6', '/cards/:cardId', <CardPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Upstream is down');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
