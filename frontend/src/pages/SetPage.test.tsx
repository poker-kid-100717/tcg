import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { CardSummary, SetDetail } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import SetPage from './SetPage';

const card = (number: string, name: string, rarity: string, marketPrice: number | null): CardSummary => ({
  id: `sv3pt5-${number}`,
  name,
  number,
  rarity,
  imageUrl: null,
  setId: 'sv3pt5',
  setName: '151',
  marketPrice,
  priceVariant: marketPrice === null ? null : 'holofoil',
  tcgplayerUrl: null,
});

const detail: SetDetail = {
  set: { id: 'sv3pt5', name: '151', series: 'Scarlet & Violet', releaseDate: '2023-09-22', printedTotal: 165, total: 207, logoUrl: null, symbolUrl: null },
  stats: { cardCount: 4, pricedCount: 3, totalMarketValue: 1234.5, mostValuable: card('199', 'Charizard ex', 'Special Illustration Rare', 1100) },
  cards: [
    card('2', 'Ivysaur', 'Uncommon', 0.25),
    card('10', 'Metapod', 'Common', null),
    card('199', 'Charizard ex', 'Special Illustration Rare', 1100),
    card('1', 'Bulbasaur', 'Common', 134.25),
  ],
};

const names = () => within(screen.getByRole('list')).getAllByRole('link').map((l) => l.querySelector('p')?.textContent);

describe('SetPage', () => {
  beforeEach(() => mockApi({ '/api/sets/sv3pt5': detail }));

  it('shows set stats and lists cards by collector number', async () => {
    renderRoute('/sets/sv3pt5', '/sets/:setId', <SetPage />);

    expect(await screen.findByRole('heading', { name: '151', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('$1,235')).toBeInTheDocument(); // totals over $1,000 round to whole dollars
    expect(screen.getByRole('link', { name: 'Charizard ex' })).toHaveAttribute('href', '/cards/sv3pt5-199');
    expect(screen.getByText(/165 cards \+ 42 secret/)).toBeInTheDocument();
    expect(names()).toEqual(['Bulbasaur', 'Ivysaur', 'Metapod', 'Charizard ex']);
  });

  it('sorts by price with unpriced cards last, and filters by rarity', async () => {
    const user = userEvent.setup();
    renderRoute('/sets/sv3pt5', '/sets/:setId', <SetPage />);
    await screen.findByRole('heading', { name: '151', level: 1 });

    await user.selectOptions(screen.getByLabelText('Sort by'), 'price-desc');
    expect(names()).toEqual(['Charizard ex', 'Bulbasaur', 'Ivysaur', 'Metapod']);

    await user.selectOptions(screen.getByLabelText('Rarity'), 'Common');
    expect(names()).toEqual(['Bulbasaur', 'Metapod']);
    expect(screen.getByText('2 of 4 cards')).toBeInTheDocument();
  });

  it('finds a card by name or exact number', async () => {
    const user = userEvent.setup();
    renderRoute('/sets/sv3pt5', '/sets/:setId', <SetPage />);
    await screen.findByRole('heading', { name: '151', level: 1 });

    await user.type(screen.getByLabelText('Find in set'), 'saur');
    expect(names()).toEqual(['Bulbasaur', 'Ivysaur']);

    await user.clear(screen.getByLabelText('Find in set'));
    await user.type(screen.getByLabelText('Find in set'), '199');
    expect(names()).toEqual(['Charizard ex']);
  });

  const progress = (kind: string, owned: number, total: number, cost: number) => ({
    setId: 'sv3pt5', setName: '151', symbolUrl: null, logoUrl: null, kind, owned, total, completion: (owned / total) * 100, costToComplete: cost, unpriced: 0,
  });
  const checklist = (goal: string | null) => ({
    setId: 'sv3pt5', printedTotal: 165, total: 207,
    owned: [{ cardId: 'sv3pt5-1', quantity: 2, variants: ['normal'] }],
    missing: [], costToCompleteBase: 812.4, costToCompleteAll: 2410,
    master: {
      owned: 1, total: 330, costToComplete: 3105.5, unpriced: 0,
      missing: [
        { cardId: 'sv3pt5-1', name: 'Bulbasaur', number: '1', imageUrl: null, secret: false, variant: 'reverseHolofoil', variantLabel: 'Reverse Holofoil', price: 1.25, tcgplayerUrl: null },
      ],
    },
    goal,
    progress: [progress('MainSet', 1, 165, 812.4), progress('FullSet', 1, 207, 2410), progress('MasterSet', 1, 330, 3105.5)],
  });

  it('marks owned cards, shows what the rest costs, and filters to the cards still needed', async () => {
    mockApi({
      '/api/sets/sv3pt5': detail,
      '/api/account': { signedIn: true, isGuest: true, email: null },
      '/api/collection/sets/sv3pt5': checklist(null),
    });
    const user = userEvent.setup();
    renderRoute('/sets/sv3pt5', '/sets/:setId', <SetPage />);

    expect(await screen.findByText('1 of 165 cards (1%)')).toBeInTheDocument();
    expect(screen.getByText('$812.40')).toBeInTheDocument();
    expect(screen.getByText('Have 2')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Show'), 'missing');
    expect(names()).toEqual(['Ivysaur', 'Metapod', 'Charizard ex']);
    await user.selectOptions(screen.getByLabelText('Show'), 'owned');
    expect(names()).toEqual(['Bulbasaur']);
  });

  it('starts a master set goal and lists every printing still needed', async () => {
    const fetchMock = mockApi({
      '/api/sets/sv3pt5': detail,
      '/api/account': { signedIn: true, isGuest: true, email: null },
      '/api/collection/sets/sv3pt5': checklist('MasterSet'),
      'PUT /api/collection/goals/sv3pt5': progress('MasterSet', 1, 330, 3105.5),
    });
    const user = userEvent.setup();
    renderRoute('/sets/sv3pt5', '/sets/:setId', <SetPage />);

    expect(await screen.findByRole('heading', { name: 'Your goal: master set' })).toBeInTheDocument();
    expect(screen.getByText('1 of 330 printings (0%)')).toBeInTheDocument();
    expect(screen.getByText('$3,106')).toBeInTheDocument();
    const needed = screen.getByRole('list', { name: 'Printings you still need' });
    expect(within(needed).getByRole('link')).toHaveTextContent('Bulbasaur #1 · Reverse Holofoil');
    expect(screen.getByRole('button', { name: 'Master set' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Full set' }));
    const put = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(String(put?.[0])).toBe('/api/collection/goals/sv3pt5');
    expect(JSON.parse(String(put?.[1]?.body))).toEqual({ kind: 'FullSet' });
  });
});
