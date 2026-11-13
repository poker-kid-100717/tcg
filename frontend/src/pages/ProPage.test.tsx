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
});
