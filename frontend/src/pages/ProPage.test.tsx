import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

import type { Account } from '../api/types';
import { mockApi } from '../test-utils';
import ProPage from './ProPage';

const liveBilling: Account = {
  id: 'user-1',
  billingConfigured: true,
  storeFinderBillingConfigured: true,
  isPro: false,
  hasStoreFinder: false,
  plan: 'free',
  subscriptionStatus: null,
  mode: 'Live billing',
  signedIn: false,
  email: null,
  emailAlerts: true,
  signInAvailable: true,
  storeFinderAvailable: false,
};

function renderPro() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/pro']}>
        <Routes>
          <Route path="/pro" element={<ProPage />} />
          <Route path="/sign-in" element={<p>Sign-in page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ProPage', () => {
  it('sends an anonymous visitor to sign in before checkout', async () => {
    const fetchMock = mockApi({ '/api/session': liveBilling });
    const user = userEvent.setup();
    renderPro();

    await user.click(await screen.findByRole('button', { name: /Choose monthly/ }));

    expect(await screen.findByText('Sign-in page')).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/billing/checkout'))).toBe(false);
  });

  it('offers only Pro while Store Finder has no retailer source', async () => {
    mockApi({ '/api/session': liveBilling });
    renderPro();

    expect(await screen.findByRole('button', { name: /Choose monthly/ })).toBeInTheDocument();
    expect(screen.queryByText('Store Finder')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Complete/ })).not.toBeInTheDocument();
  });

  it('offers Store Finder and Complete once a retailer source is connected', async () => {
    mockApi({ '/api/session': { ...liveBilling, storeFinderAvailable: true } });
    renderPro();

    expect(await screen.findByRole('button', { name: /Add Store Finder/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Choose Complete/ })).toBeInTheDocument();
  });

  it('says everything is free while billing is off, with nothing to buy', async () => {
    mockApi({ '/api/session': { ...liveBilling, billingConfigured: false, isPro: true, plan: 'preview', mode: 'Free beta' } });
    renderPro();

    expect(await screen.findByText('Everything in TCG Signal is free right now.')).toBeInTheDocument();
    expect(screen.queryByText(/\$9\.99/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Choose/ })).not.toBeInTheDocument();
  });
});
