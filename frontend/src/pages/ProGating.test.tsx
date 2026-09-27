import { fireEvent, screen, waitFor } from '@testing-library/react';

import type { Account, DealAnalysis, Me, Plan } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import AccountPage, { safeReturnUrl } from './AccountPage';
import DealAnalyzerPage from './DealAnalyzerPage';
import ProPage from './ProPage';
import WatchlistPage from './WatchlistPage';

const account = (isPro: boolean): Account => ({
  id: 'u1', billingConfigured: true, storeFinderBillingConfigured: true, isPro, hasStoreFinder: false,
  plan: isPro ? 'monthly' : 'free', subscriptionStatus: isPro ? 'active' : null, mode: 'Live billing',
  currentPeriodEnd: null, cancelAtPeriodEnd: false, paymentIssue: false, canManageBilling: isPro,
});
const signedIn = (isPro: boolean): Me => ({ signedIn: true, email: 'a@example.test', displayName: null, account: account(isPro) });
const visitor: Me = { signedIn: false, email: null, displayName: null, account: null };

describe('Pro gating and sign-in', () => {
  it('asks visitors to sign in, returning them to the page afterwards', async () => {
    mockApi({ '/api/me': visitor });
    renderRoute('/watchlist', '/watchlist', <WatchlistPage />);

    const link = await screen.findByRole('link', { name: 'Sign in or create an account' });
    expect(link).toHaveAttribute('href', '/account?returnUrl=%2Fwatchlist');
  });

  it('shows free accounts what the Deal Analyzer does instead of the tool', async () => {
    const fetch = mockApi({ '/api/me': signedIn(false) });
    renderRoute('/deal', '/deal', <DealAnalyzerPage />);

    expect(await screen.findByRole('heading', { name: 'Deal Analyzer is a Pro tool' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'See TCG Signal Pro' })).toHaveAttribute('href', '/pro');
    expect(fetch.mock.calls.some(([url]) => String(url).includes('/deals/'))).toBe(false);
  });

  it('analyzes a deal on the server for Pro accounts and marks the request as the app’s own', async () => {
    const analysis: DealAnalysis = {
      cardId: 'sv3pt5-6', cardName: 'Charizard ex', variant: 'holofoil', variantLabel: 'Holofoil', marketReference: 100,
      referenceSource: 'Pokémon TCG API (TCGplayer market prices)', freshness: { asOf: '2026-09-27', ageDays: 0, state: 'Fresh', label: 'Updated today' },
      confidenceScore: 80, confidenceBand: 'High', askingPrice: 80, askVsMarketPercent: -20, position: 'Below market reference',
      fees: { id: 'local', name: 'Local cash sale', percentFee: 0, fixedFee: 0, note: '' },
      math: { acquisition: 85, expectedSale: 100, sellingFees: 0, outboundShipping: 5, proceeds: 95, net: 10, netMarginPercent: 11.76, breakEvenSalePrice: 90 },
      warnings: [], summary: 'Charizard ex (Holofoil) at $80.00 … Estimate only, not financial advice. — TCG Signal',
    };
    const fetch = mockApi({
      '/api/me': signedIn(true),
      '/api/deals/presets': [{ id: 'local', name: 'Local cash sale', percentFee: 0, fixedFee: 0, note: 'No platform fees.' }],
      '/api/cards/sv3pt5-6': {
        id: 'sv3pt5-6', name: 'Charizard ex', number: '6', imageUrl: null, set: { name: '151' },
        prices: [{ variant: 'holofoil', label: 'Holofoil', market: 100, low: 95, mid: 100, high: 120 }],
      },
      'POST /api/deals/analyze': analysis,
    });
    renderRoute('/deal?card=sv3pt5-6', '/deal', <DealAnalyzerPage />);

    fireEvent.change(await screen.findByLabelText(/^Asking price/), { target: { value: '80' } });
    fireEvent.click(screen.getByRole('button', { name: 'Analyze deal' }));

    expect(await screen.findByText('Below market reference · asking is 20.0% below')).toBeInTheDocument();
    expect(screen.getByText('$90.00')).toBeInTheDocument(); // break-even
    const [, init] = fetch.mock.calls.find(([url]) => String(url).endsWith('/deals/analyze'))!;
    expect(new Headers(init!.headers).get('x-requested-with')).toBe('fetch');
    expect(JSON.parse(String(init!.body))).toMatchObject({ cardId: 'sv3pt5-6', variant: 'holofoil', askingPrice: 80, presetId: 'local' });
  });

  it('prices the plans from configuration', async () => {
    const plans: Plan[] = [
      { id: 'free', name: 'Free', price: 0, currency: 'USD', interval: 'forever', available: true },
      { id: 'monthly', name: 'Pro monthly', price: 9.99, currency: 'USD', interval: 'month', available: true },
      { id: 'annual', name: 'Pro annual', price: 79, currency: 'USD', interval: 'year', available: true },
      { id: 'storefinder', name: 'Store Finder', price: 4.99, currency: 'USD', interval: 'month', available: false },
      { id: 'complete', name: 'Complete', price: 12.99, currency: 'USD', interval: 'month', available: false },
    ];
    mockApi({ '/api/me': visitor, '/api/billing/plans': plans });
    renderRoute('/pro', '/pro', <ProPage />);

    expect(await screen.findByText(/or \$79\/year \(save 34%\)/)).toBeInTheDocument();
    // Visitors sign in before checkout; nothing is sold to an anonymous browser.
    expect(screen.getAllByRole('link', { name: 'Sign in to subscribe' }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: /Choose monthly/ })).not.toBeInTheDocument();
  });

  it('offers the development sign-in only when the API says it is enabled', async () => {
    mockApi({
      '/api/me': visitor,
      '/api/auth/options': { signInAvailable: true, providerSignIn: false, localLogin: true, providerName: 'test' },
      'POST /api/auth/local-login': 204,
    });
    renderRoute('/account?returnUrl=%2Fwatchlist', '/account', <AccountPage />);

    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'me@example.test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Signing in…' })).not.toBeInTheDocument());
  });

  it.each([
    ['/watchlist', '/watchlist'],
    ['https://evil.example', '/dashboard'],
    ['//evil.example', '/dashboard'],
    ['/\\evil.example', '/dashboard'],
    [null, '/dashboard'],
  ])('only returns to local paths after sign-in (%s)', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });
});
