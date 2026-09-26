import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { CollectionEntry, CollectionView } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import CollectionPage from './CollectionPage';

vi.mock('../components/collection/ValueChart', () => ({
  ValueChart: ({ history }: { history: unknown[] }) => <p>value chart with {history.length} points</p>,
}));

const entry = (overrides: Partial<CollectionEntry>): CollectionEntry => ({
  id: 'item-1', cardId: 'sv3pt5-6', name: 'Charizard ex', number: '6', setId: 'sv3pt5', setName: '151', imageUrl: null,
  rarity: 'Double Rare', variant: 'holofoil', variantLabel: 'Holofoil', condition: 'NearMint', quantity: 1, costEach: 20,
  acquiredOn: null, notes: null, marketEach: 24, valueEach: 24, netEach: 20.49, total: 24, gainPercent: 20,
  confidence: { level: 'High', reasons: ['Price moved today'], daysSinceChange: 0 }, tcgplayerUrl: null, addedAt: '2026-09-20T00:00:00Z',
  ...overrides,
});

const view: CollectionView = {
  summary: {
    cards: 3, unique: 2, marketValue: 124, conditionValue: 109, netIfSold: 93.2, costBasis: 90, gain: 19, gainPercent: 21.1,
    highConfidenceShare: 78, change30Percent: -4.2, pricesAsOf: '2026-09-25',
  },
  items: [
    entry({}),
    entry({ id: 'item-2', cardId: 'sv3pt5-199', name: 'Mew ex', number: '151', condition: 'LightlyPlayed', quantity: 2, marketEach: 50, valueEach: 42.5, total: 85, costEach: 35, gainPercent: 21.4,
      confidence: { level: 'Low', reasons: ['Market price unchanged for 40 days, so few recent sales'], daysSinceChange: 40 } }),
  ],
  history: [{ date: '2026-09-24', value: 100 }, { date: '2026-09-25', value: 109 }],
  sets: [{ setId: 'sv3pt5', setName: '151', symbolUrl: null, releaseDate: '2023-09-22', owned: 2, printedTotal: 165, ownedAll: 2, total: 207, completion: 1.2, costToComplete: 410.5, unpricedMissing: 3 }],
  signals: [{ cardId: 'sv3pt5-199', name: 'Mew ex', setName: '151', imageUrl: null, variant: 'holofoil', variantLabel: 'Holofoil', kind: 'ConsiderSelling', reasons: ['Down 18% over 30 days, steadily.'], valueEach: 42.5, quantity: 2 }],
  fees: { commissionRate: 0.1075, paymentRate: 0.025, perSaleFee: 0.3 },
  conditionFactors: { NearMint: 1, LightlyPlayed: 0.85, ModeratelyPlayed: 0.7, HeavilyPlayed: 0.5, Damaged: 0.35 },
};

describe('CollectionPage', () => {
  it('invites a new visitor to start, or to explore a sample collection', async () => {
    const fetchMock = mockApi({
      '/api/account': { signedIn: false, isGuest: false, email: null },
      '/api/sets': [],
      'POST /api/account/guest': { signedIn: true, isGuest: true, email: null },
      'POST /api/collection/sample': { added: 24 },
      '/api/collection': view,
    });
    renderRoute('/', '/', <CollectionPage />);

    expect(await screen.findByRole('heading', { name: 'Know what your collection is really worth.' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Explore a sample collection' }));

    expect(await screen.findByRole('heading', { name: /3 cards/ })).toBeInTheDocument();
    const posts = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST').map(([url]) => String(url));
    expect(posts).toEqual(['/api/account/guest', '/api/collection/sample']);
  });

  it('shows the three values, signals, holdings with confidence, and set progress', async () => {
    mockApi({ '/api/account': { signedIn: true, isGuest: true, email: null }, '/api/collection': view });
    renderRoute('/', '/', <CollectionPage />);

    expect(await screen.findByRole('heading', { name: '3 cards · $109.00' })).toBeInTheDocument();
    expect(screen.getByText('Market value').nextSibling).toHaveTextContent('$124.00');
    expect(screen.getByText('Net if sold').nextSibling).toHaveTextContent('$93.20');
    expect(screen.getByText('−4.2% over 30 days'.replace('−', '-'))).toHaveClass('loss');
    expect(screen.getByText('+$19.00')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Save my collection' })).toHaveAttribute('href', '/account');

    const signals = screen.getByRole('region', { name: 'Heads-up' });
    expect(within(signals).getByText('Consider selling')).toBeInTheDocument();

    const cards = screen.getByRole('region', { name: 'Cards' });
    expect(within(cards).getAllByRole('link').map((a) => a.textContent)).toEqual(['Mew ex', 'Charizard ex']);
    expect(within(cards).getByText('Low confidence')).toBeInTheDocument();
    expect(within(cards).getByLabelText('Condition of Mew ex')).toHaveValue('LightlyPlayed');

    const progress = screen.getByRole('link', { name: /2 \/ 165/ });
    expect(progress).toHaveAttribute('href', '/sets/sv3pt5?show=missing');
    expect(progress).toHaveTextContent('about $410.50 to finish the main set (3 unpriced)');
    expect(screen.getByText('value chart with 2 points')).toBeInTheDocument();
  });

  it('saves a condition change straight away', async () => {
    const fetchMock = mockApi({
      '/api/account': { signedIn: true, isGuest: false, email: 'me@example.com' },
      '/api/collection': view,
      'PATCH /api/collection/items/item-2': { items: [], wish: null },
    });
    renderRoute('/', '/', <CollectionPage />);

    await userEvent.selectOptions(await screen.findByLabelText('Condition of Mew ex'), 'NearMint');

    const patch = fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH');
    expect(String(patch?.[0])).toBe('/api/collection/items/item-2');
    expect(JSON.parse(String(patch?.[1]?.body))).toEqual({ condition: 'NearMint' });
    expect(screen.queryByText('Save my collection')).not.toBeInTheDocument();
  });
});
