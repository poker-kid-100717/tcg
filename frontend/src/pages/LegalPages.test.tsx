import { screen } from '@testing-library/react';

import { site } from '../lib/site';
import { renderRoute } from '../test-utils';
import PrivacyPage from './PrivacyPage';
import TermsPage from './TermsPage';

describe('legal pages', () => {
  it('names the operator, auto-renewal and governing law in the terms', () => {
    renderRoute('/terms', '/terms', <TermsPage />);

    expect(screen.getByRole('heading', { level: 1, name: 'Terms of Service' })).toBeInTheDocument();
    expect(screen.getAllByText(new RegExp(site.operator)).length).toBeGreaterThan(0);
    expect(screen.getByText('Subscriptions renew automatically')).toBeInTheDocument();
    expect(screen.getByText(/governed by the laws of the State of New Mexico/)).toBeInTheDocument();
  });

  it('lists every processor and a contact address in the privacy policy', () => {
    renderRoute('/privacy', '/privacy', <PrivacyPage />);

    expect(screen.getByRole('heading', { level: 1, name: 'Privacy Policy' })).toBeInTheDocument();
    for (const provider of ['Cloudflare', 'Neon', 'Resend', 'Stripe']) {
      expect(screen.getByText(new RegExp(`^${provider}:`))).toBeInTheDocument();
    }
    expect(screen.getAllByRole('link', { name: site.supportEmail }).length).toBeGreaterThan(0);
  });
});
