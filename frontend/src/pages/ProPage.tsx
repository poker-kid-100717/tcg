import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';

import { api, ApiError } from '../api/client';
import { useSession } from '../api/hooks';
import { Loading } from '../components/States';

type CheckoutPlan = 'monthly' | 'annual' | 'storefinder' | 'complete';

export default function ProPage() {
  const session = useSession();
  const navigate = useNavigate();
  const [working, setWorking] = useState<CheckoutPlan | 'portal' | null>(null);
  const [error, setError] = useState('');

  if (session.isPending) return <Loading label="Loading plans…" />;

  const account = session.data;
  // Paying needs an account that works on every device, so checkout starts with sign-in.
  const goCheckout = async (plan: CheckoutPlan) => {
    if (!account?.signedIn) {
      navigate('/sign-in?next=/pro');
      return;
    }
    setWorking(plan);
    setError('');
    try {
      const result = await api.checkout(plan);
      window.location.assign(result.url);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        navigate('/sign-in?next=/pro');
        return;
      }
      setError(e instanceof Error ? e.message : 'Unable to start checkout.');
      setWorking(null);
    }
  };

  const manage = async () => {
    setWorking('portal');
    setError('');
    try {
      const result = await api.billingPortal();
      window.location.assign(result.url);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to open billing.');
      setWorking(null);
    }
  };

  const preview = !account?.billingConfigured;
  const storeFinder = account?.storeFinderAvailable ?? false;

  // Paid plans are switched off: everything is free while the beta runs.
  if (preview) return <FreeBeta />;

  return (
    <div className="container-custom grid gap-10 py-10 sm:py-14">
      <header className="mx-auto grid max-w-3xl gap-3 text-center">
        <p className="eyebrow">TCG Signal plans</p>
        {storeFinder ? (
          <>
            <h1 className="text-4xl sm:text-5xl">Price intelligence and local inventory, separately or together.</h1>
            <p className="text-lg text-slate-600">
              Pro helps evaluate the market. Store Finder monitors supported retailers near you and only surfaces
              store-level inventory that meets the evidence standard.
            </p>
          </>
        ) : (
          <>
            <h1 className="text-4xl sm:text-5xl">Price intelligence for collectors who buy, sell and grade.</h1>
            <p className="text-lg text-slate-600">
              Pro adds market confidence, deal math, unlimited watchlist alerts and master set tracking on top of the free price guide.
            </p>
          </>
        )}
        {account && !account.signedIn && account.signInAvailable && (
          <p className="text-sm text-slate-600">You'll sign in with your email before checkout, so your plan works on every device.</p>
        )}
      </header>

      <div className={`mx-auto grid w-full gap-5 ${storeFinder ? 'max-w-6xl lg:grid-cols-3' : 'max-w-md'}`}>
        <Plan
          name="Pro"
          price="$9.99/mo"
          description="Market intelligence for buy, sell, grade and trade decisions."
          features={[
            'Market Confidence with transparent reasons',
            'Deal Analyzer and break-even math',
            'Unlimited watchlist and threshold alerts',
            'Unlimited Master Set trackers + AI Set Advisor',
            'Personal dashboard',
            'Longer signal context and model outlook',
          ]}
          actions={
            account?.isPro && account.billingConfigured ? (
              <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={manage}>
                {working === 'portal' ? 'Opening…' : 'Manage subscription'}
              </button>
            ) : account?.billingConfigured ? (
              <div className="grid gap-2">
                <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={() => goCheckout('monthly')}>
                  {working === 'monthly' ? 'Opening…' : 'Choose monthly · $9.99'}
                </button>
                <button type="button" className="btn w-full bg-white text-pokemon-pokeblue ring-1 ring-pokemon-pokeblue" disabled={working !== null} onClick={() => goCheckout('annual')}>
                  {working === 'annual' ? 'Opening…' : 'Choose annual · $79'}
                </button>
              </div>
            ) : null
          }
        />

        {storeFinder && (
          <>
            <Plan
              featured
              name="Store Finder"
              price="$4.99/mo"
              description="Local Pokémon inventory monitoring built around accuracy rather than alert volume."
              features={[
                'Available in Stores account tab',
                '5, 10, 25, 50 or 100 mile radius',
                'Store-level availability only',
                'Verification timestamp and evidence on every result',
                'Confidence score and low-stock disclosure',
                'Retailer coverage status instead of silent failures',
              ]}
              actions={
                account?.hasStoreFinder && account.storeFinderBillingConfigured ? (
                  <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={manage}>
                    {working === 'portal' ? 'Opening…' : 'Manage subscription'}
                  </button>
                ) : account?.storeFinderBillingConfigured ? (
                  <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={() => goCheckout('storefinder')}>
                    {working === 'storefinder' ? 'Opening…' : 'Add Store Finder · $4.99'}
                  </button>
                ) : null
              }
            />

            <Plan
              name="Complete"
              price="$12.99/mo"
              description="Both TCG Signal Pro and Store Finder under one subscription."
              features={[
                'Everything in Pro',
                'Everything in Store Finder',
                'One billing plan',
                'Best value for active collectors',
              ]}
              actions={
                account?.plan === 'complete' && account.billingConfigured ? (
                  <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={manage}>
                    {working === 'portal' ? 'Opening…' : 'Manage subscription'}
                  </button>
                ) : account?.billingConfigured ? (
                  <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={() => goCheckout('complete')}>
                    {working === 'complete' ? 'Opening…' : 'Choose Complete · $12.99'}
                  </button>
                ) : null
              }
            />
          </>
        )}
      </div>

      {error && <p className="mx-auto max-w-xl rounded-lg bg-red-50 px-4 py-3 text-center text-sm text-red-800">{error}</p>}

      <p className="mx-auto max-w-xl text-center text-xs leading-5 text-slate-500">
        Subscriptions renew automatically until you cancel, which you can do any time from your account. See the{' '}
        <Link to="/terms" className="font-semibold text-pokemon-blue">
          Terms of Service
        </Link>
        .
      </p>

      {storeFinder && (
        <>
          <section className="mx-auto grid max-w-6xl gap-4 sm:grid-cols-3">
            <Feature title="Accuracy before coverage" body="Retailers are added only after their store-level source is validated. An empty result is preferable to a false trip across town." />
            <Feature title="Useful at the moment of purchase" body="Store Finder combines radius, distance, evidence, verification time and retailer links so a collector can act quickly." />
            <Feature title="Independent service boundary" body="Inventory runs separately from pricing so release-day polling, retailer failures and scaling do not destabilize the core market API." />
          </section>

          <p className="mx-auto max-w-3xl text-center text-xs leading-5 text-slate-500">
            Store availability can change between verification and arrival. TCG Signal reports the source and time of each observation and never invents quantity.
          </p>
        </>
      )}
    </div>
  );
}

function FreeBeta() {
  return (
    <div className="container-custom grid justify-items-center py-12 sm:py-16">
      <section className="panel grid w-full max-w-2xl gap-5 p-6 text-center sm:p-10">
        <p className="eyebrow">Free beta</p>
        <h1 className="text-3xl sm:text-4xl">Everything in TCG Signal is free right now.</h1>
        <p className="text-slate-600">
          There's nothing to buy during the beta. Every feature is unlocked, and there are no limits on your watchlist or
          master sets.
        </p>
        <ul className="mx-auto grid gap-2 text-left text-sm text-slate-700">
          {[
            'Fair-price check: sealed product MSRP vs today\'s market',
            'Market confidence, deal math and price outlooks',
            'Unlimited watchlist with email alerts',
            'Unlimited master set trackers with the AI Set Advisor',
          ].map((feature) => (
            <li key={feature} className="flex gap-2">
              <span className="font-bold text-emerald-700">✓</span>
              <span>{feature}</span>
            </li>
          ))}
        </ul>
        <p className="text-sm text-slate-500">If paid plans arrive later, we'll say so here before anything changes.</p>
      </section>
    </div>
  );
}

function Plan({ name, price, description, features, actions, featured = false }: {
  name: string;
  price: string;
  description: string;
  features: string[];
  actions?: React.ReactNode;
  featured?: boolean;
}) {
  return (
    <section className={`panel grid content-start gap-5 p-6 ${featured ? 'border-pokemon-blue ring-2 ring-pokemon-blue/10' : ''}`}>
      <div>
        <p className="eyebrow">{name}</p>
        <p className="mt-1 text-3xl font-bold text-slate-900">{price}</p>
        <p className="mt-2 text-sm text-slate-600">{description}</p>
      </div>
      <ul className="grid gap-2 text-sm text-slate-700">
        {features.map((feature) => <li key={feature} className="flex gap-2"><span className="font-bold text-emerald-700">✓</span><span>{feature}</span></li>)}
      </ul>
      {actions}
    </section>
  );
}

function Feature({ title, body }: { title: string; body: string }) {
  return (
    <div className="panel p-5">
      <h2 className="text-lg">{title}</h2>
      <p className="mt-2 text-sm leading-6 text-slate-600">{body}</p>
    </div>
  );
}
