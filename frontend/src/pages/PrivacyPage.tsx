import { Link } from 'react-router-dom';

import { LegalPage, SupportLink } from '../components/LegalPage';
import { site } from '../lib/site';

export default function PrivacyPage() {
  return (
    <LegalPage title="Privacy Policy">
      <p>
        This policy explains what {site.operator} ("{site.name}", "we") collects when you use {site.name}, why, and the
        choices you have. We collect as little as the service needs. We don't show ads, and we don't sell or share your
        personal information for advertising.
      </p>

      <h2>What we collect</h2>
      <ul>
        <li>
          <strong>A session cookie.</strong> One cookie (<code>tcg_signal_session</code>) holds a random token that
          keeps your profile in this browser. It is HttpOnly, lasts up to 180 days, and we store only a hash of it. We
          use no advertising or analytics cookies.
        </li>
        <li>
          <strong>Your email address</strong>, if you sign in. Sign-in codes are stored only as hashes and expire after
          10 minutes.
        </li>
        <li>
          <strong>What you save:</strong> watchlist cards and price targets, the alerts they trigger, master sets
          (including quantities and prices you enter), and your alert email setting.
        </li>
        <li>
          <strong>Subscription details:</strong> your plan, its status and renewal date, and the Stripe customer and
          subscription IDs. Your card details go directly to Stripe; we never see or store them.
        </li>
        <li>
          <strong>Technical data:</strong> your IP address and basic request information, processed by our hosting
          provider and our servers for security, rate limiting and troubleshooting.
        </li>
        <li>
          <strong>Location for Store Finder</strong>, when that feature is available: the coordinates you search from
          are used for that search only and are not stored.
        </li>
      </ul>

      <h2>How we use it</h2>
      <ul>
        <li>To run {site.name}: keep you signed in, save your watchlist and master sets, and show your dashboard.</li>
        <li>
          To email you sign-in codes and, if you leave them on, watchlist alerts (at most one email a day). We don't
          send marketing email.
        </li>
        <li>To take payments and manage your subscription.</li>
        <li>To keep the service secure and prevent abuse.</li>
        <li>To answer you when you contact support.</li>
      </ul>
      <p>
        The Master Set Advisor sends your set's completion and market figures (not your email or identity) to an AI
        model hosted by Cloudflare to write its summary.
      </p>

      <h2>Who processes it for us</h2>
      <p>These providers handle data only to run {site.name} for us:</p>
      <ul>
        <li>Cloudflare: hosting, network delivery and the AI model behind the Master Set Advisor</li>
        <li>Neon: our database</li>
        <li>Resend: delivering sign-in and alert emails</li>
        <li>Stripe: payments and subscription billing</li>
      </ul>
      <p>We may also disclose information if the law requires it, or to protect the service and its users.</p>

      <h2>How long we keep it</h2>
      <p>
        We keep your account data while your account exists and delete it when you ask us to. A browser profile that
        was never signed in stays tied to its cookie, and we may delete unused ones. Stripe keeps payment records as the
        law requires.
      </p>

      <h2>Your choices</h2>
      <ul>
        <li>
          Turn alert emails off on your <Link to="/account" className="font-semibold text-pokemon-blue">account page</Link>{' '}
          or with the link in any alert email.
        </li>
        <li>Sign out from your account page; that ends the session in that browser.</li>
        <li>
          Ask for a copy of your data, a correction, or deletion of your account at <SupportLink />. We'll answer within
          30 days. Depending on where you live, you may have further rights under local law; we honor these requests
          wherever you are.
        </li>
      </ul>

      <h2>Security</h2>
      <p>
        {site.name} is served only over HTTPS. Session tokens and sign-in codes are stored as hashes, and sign-in is
        rate-limited. No system is perfectly secure, but we'll tell affected users promptly if a breach affects their
        data.
      </p>

      <h2>Children</h2>
      <p>
        {site.name} is not directed at children under 13, and we don't knowingly collect their personal information. If
        you believe a child has given us an email address, contact <SupportLink /> and we'll delete it.
      </p>

      <h2>Changes</h2>
      <p>
        If we change this policy, we'll update the effective date above and email signed-in users about material
        changes before they take effect.
      </p>
    </LegalPage>
  );
}
