import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

import type { Account } from '../api/types';
import { mockApi } from '../test-utils';
import SignInPage from './SignInPage';

const anonymous: Account = {
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

function renderSignIn(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/sign-in" element={<SignInPage />} />
          <Route path="/pro" element={<p>Plans page</p>} />
          <Route path="/dashboard" element={<p>Dashboard page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('SignInPage', () => {
  it('emails a code, signs in with it and returns to the page that asked', async () => {
    const fetchMock = mockApi({
      '/api/session': anonymous,
      '/api/auth/code': {},
      '/api/auth/verify': { ...anonymous, signedIn: true, email: 'ash@example.com' },
    });
    const user = userEvent.setup();
    renderSignIn('/sign-in?next=/pro');

    await user.type(await screen.findByLabelText('Email'), 'ash@example.com');
    await user.click(screen.getByRole('button', { name: 'Email me a code' }));
    expect(await screen.findByText(/We sent a 6-digit code to ash@example.com/)).toBeInTheDocument();

    await user.type(screen.getByLabelText('6-digit code'), '12a3456');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Plans page')).toBeInTheDocument();
    const verify = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/auth/verify'));
    expect(JSON.parse(String(verify?.[1]?.body))).toEqual({ email: 'ash@example.com', code: '123456' });
  });

  it('never redirects off the site after sign-in', async () => {
    mockApi({
      '/api/session': anonymous,
      '/api/auth/code': {},
      '/api/auth/verify': { ...anonymous, signedIn: true, email: 'ash@example.com' },
    });
    const user = userEvent.setup();
    renderSignIn('/sign-in?next=//evil.example');

    await user.type(await screen.findByLabelText('Email'), 'ash@example.com');
    await user.click(screen.getByRole('button', { name: 'Email me a code' }));
    await user.type(await screen.findByLabelText('6-digit code'), '123456');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Dashboard page')).toBeInTheDocument();
  });

  it('shows why a code was refused', async () => {
    mockApi({ '/api/session': anonymous, '/api/auth/code': {}, '/api/auth/verify': 400 });
    const user = userEvent.setup();
    renderSignIn('/sign-in');

    await user.type(await screen.findByLabelText('Email'), 'ash@example.com');
    await user.click(screen.getByRole('button', { name: 'Email me a code' }));
    await user.type(await screen.findByLabelText('6-digit code'), '000000');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Upstream is down');
  });

  it('says so when email sign-in is not switched on', async () => {
    mockApi({ '/api/session': { ...anonymous, signInAvailable: false } });
    renderSignIn('/sign-in');

    expect(await screen.findByText(/Email sign-in isn't switched on yet/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
  });
});
