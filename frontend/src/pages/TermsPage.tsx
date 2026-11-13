import { Link } from 'react-router-dom';

import { LegalPage, SupportLink } from '../components/LegalPage';
import { site } from '../lib/site';

export default function TermsPage() {
  return (
    <LegalPage title="Terms of Service">
      <p>
        These terms are an agreement between you and {site.operator} ("{site.name}", "we", "us") for your use of the{' '}
        {site.name} website and services. By using {site.name}, signing in or subscribing, you agree to them. If you
        don't agree, please don't use the service.
      </p>

      <h2>1. What {site.name} is</h2>
      <p>
        {site.name} is an information service for Pokémon Trading Card Game collectors. It shows card prices and price
        history, market signals and price outlooks, watchlist alerts, master set tracking and, where available, store
        inventory information. We don't sell cards. Purchases happen on the third-party sites we link to, under their
        own terms.
      </p>

      <h2>2. Information, not advice</h2>
      <ul>
        <li>
          Prices come from third-party sources (currently TCGplayer market data) and can be delayed, incomplete or
          wrong. We record them once a day.
        </li>
        <li>
          Market signals, confidence scores, outlooks and the Master Set Advisor are estimates. They are not
          predictions you should rely on and are not financial, investment or tax advice.
        </li>
        <li>Store availability can change between when it was observed and when you arrive.</li>
        <li>You are responsible for your own buying, selling and grading decisions.</li>
      </ul>

      <h2>3. Your account</h2>
      <p>
        You can use parts of {site.name} without an account; your data is then kept in that browser only. Signing in
        uses a one-time code sent to your email. Keep access to that inbox secure: anyone who can read it can sign in as
        you. One account is for one person. Tell us at <SupportLink /> if you think someone else has accessed your
        account.
      </p>

      <h2>4. Subscriptions and payment</h2>
      <ul>
        <li>
          Paid plans are listed on the <Link to="/pro" className="font-semibold text-pokemon-blue">plans page</Link>{' '}
          and are billed in advance, monthly or yearly, in US dollars. Payments are processed by Stripe.
        </li>
        <li>
          <strong>Subscriptions renew automatically</strong> at the end of each billing period until you cancel. You can
          cancel any time from your account ("Manage subscription"). Cancellation takes effect at the end of the
          current period, and you keep paid features until then.
        </li>
        <li>
          Payments are non-refundable, including for partly used periods, except where the law requires a refund or
          where we decide to give one. If something went wrong, contact <SupportLink />.
        </li>
        <li>
          We may change prices. A change applies from your next billing period, and we will email you before it does,
          so you can cancel first.
        </li>
        <li>
          During a free preview, paid features may be available at no charge. They may require a subscription once
          the preview ends.
        </li>
      </ul>

      <h2>5. Acceptable use</h2>
      <p>Don't:</p>
      <ul>
        <li>scrape, bulk download or resell {site.name} data, or access the service with bots beyond normal use;</li>
        <li>try to break, overload or get around the security or usage limits of the service;</li>
        <li>use someone else's account or email address;</li>
        <li>use the service for anything unlawful.</li>
      </ul>
      <p>We may suspend or close accounts that break these rules.</p>

      <h2>6. Ownership and trademarks</h2>
      <p>
        The {site.name} software, design and original content belong to us. Pokémon, the Pokémon Trading Card Game,
        card names and card images are trademarks and copyrights of Nintendo, Creatures, GAME FREAK and The Pokémon
        Company. TCGplayer is a trademark of its owner. {site.name} is not affiliated with, endorsed by or sponsored by
        any of them.
      </p>

      <h2>7. Availability and changes</h2>
      <p>
        We work to keep {site.name} running and accurate, but we don't promise it will always be available, error-free
        or that any feature or data source will continue. We may change or discontinue features. If we discontinue a
        paid plan, we will stop billing for it and refund any period we can no longer provide.
      </p>

      <h2>8. Disclaimers</h2>
      <p>
        To the extent the law allows, {site.name} is provided "as is" and "as available", without warranties of any
        kind, including warranties of accuracy, merchantability, fitness for a particular purpose and
        non-infringement.
      </p>

      <h2>9. Limitation of liability</h2>
      <p>
        To the extent the law allows, we are not liable for indirect, incidental, special or consequential damages, or
        for lost profits, trading losses or lost data, arising from your use of {site.name}. Our total liability for
        any claim is limited to the greater of the amount you paid us in the 12 months before the claim, or $50.
      </p>

      <h2>10. Ending your use</h2>
      <p>
        You can stop using {site.name} at any time and ask us to delete your account at <SupportLink />. Cancel any
        subscription first so you aren't billed again.
      </p>

      <h2>11. Changes to these terms</h2>
      <p>
        We may update these terms. We'll change the effective date above, and for material changes we'll email
        signed-in users before they take effect. Using {site.name} after a change means you accept it.
      </p>

      <h2>12. Governing law</h2>
      <p>
        These terms are governed by the laws of the State of {site.jurisdiction}, USA, without regard to its conflict
        of law rules. Disputes will be resolved in the state or federal courts located in {site.jurisdiction}, unless
        the law where you live gives you the right to bring them elsewhere.
      </p>

      <h2>13. Privacy</h2>
      <p>
        Our <Link to="/privacy" className="font-semibold text-pokemon-blue">Privacy Policy</Link> explains what we
        collect and how we use it.
      </p>
    </LegalPage>
  );
}
