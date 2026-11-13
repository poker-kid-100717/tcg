import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { SealedList } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import FairPricePage from './FairPricePage';

const list: SealedList = {
  asOf: '2026-11-14',
  types: ['Booster Bundle', 'Collection', 'Elite Trainer Box'],
  products: [
    {
      productId: 1,
      name: '30th Celebration Elite Trainer Box',
      setName: '30th Celebration',
      setId: 'me30',
      productType: 'Elite Trainer Box',
      imageUrl: null,
      tcgplayerUrl: 'https://www.tcgplayer.com/product/1',
      releasedOn: '2026-09-16',
      msrp: 49.99,
      msrpSource: 'typical',
      market: 155.59,
      low: 140,
      asOf: '2026-11-14',
      change30Percent: -4.2,
      percentOverMsrp: 211.2,
    },
    {
      productId: 2,
      name: 'Charizard ex Premium Collection',
      setName: 'Obsidian Flames',
      setId: 'sv3',
      productType: 'Collection',
      imageUrl: null,
      tcgplayerUrl: null,
      releasedOn: '2023-08-11',
      msrp: null,
      msrpSource: null,
      market: 40,
      low: 35,
      asOf: '2026-11-14',
      change30Percent: null,
      percentOverMsrp: null,
    },
  ],
};

describe('FairPricePage', () => {
  it('shows MSRP next to the market price and judges a quoted price', async () => {
    mockApi({ '/api/sealed': list });
    const user = userEvent.setup();
    renderRoute('/fair-price', '/fair-price', <FairPricePage />);

    const etb = (await screen.findByRole('heading', { name: '30th Celebration Elite Trainer Box' })).closest('li')!;
    expect(within(etb).getByText('Typical MSRP')).toBeInTheDocument();
    expect(within(etb).getByText('$49.99')).toBeInTheDocument();
    expect(within(etb).getByText('$155.59')).toBeInTheDocument();
    expect(within(etb).getByText(/the market is 211.2% over MSRP/)).toBeInTheDocument();

    await user.type(within(etb).getByLabelText("Price you're seeing"), '54');
    expect(within(etb).getByRole('status')).toHaveTextContent('Close to retail');
  });

  it('compares with the market when the MSRP is unknown', async () => {
    mockApi({ '/api/sealed': list });
    const user = userEvent.setup();
    renderRoute('/fair-price', '/fair-price', <FairPricePage />);

    const collection = (await screen.findByRole('heading', { name: 'Charizard ex Premium Collection' })).closest('li')!;
    expect(within(collection).getByText('Not known')).toBeInTheDocument();
    await user.type(within(collection).getByLabelText("Price you're seeing"), '60');
    expect(within(collection).getByRole('status')).toHaveTextContent('Above market');
  });

  it('explains an empty list instead of showing nothing', async () => {
    mockApi({ '/api/sealed': { asOf: null, types: [], products: [] } });
    renderRoute('/fair-price', '/fair-price', <FairPricePage />);

    expect(await screen.findByText(/No sealed products match yet/)).toBeInTheDocument();
  });
});
