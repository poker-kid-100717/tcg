import { useState } from 'react';

import { api } from '../api/client';
import { useSession } from '../api/hooks';
import { Loading } from '../components/States';

type CheckoutPlan = 'monthly' | 'annual' | 'storefinder' | 'complete';

export default function ProPage() {
  const session = useSession();
  const [working, setWorking] = useState<CheckoutPlan | 'portal' | null>(null);
  const [error, setError] = useState('');

  if (session.isPending) return <Loading label="Loading plans…" />;

  const account = session.data;
  const goCheckout = async (plan: CheckoutPlan) => {
    setWorking(plan);
    setError('');
    try {
      const result = await api.checkout(plan);
      window.location.assign(result.url);
    } catch (e) {
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

  return (
    <div className="container-custom grid gap-10 py-10 sm:py-14">
      <header className="mx-auto grid max-w-3xl gap-3 text-center">
        <p className="eyebrow">TCG Signal plans</p>
        <h1 className="text-4xl sm:text-5xl">Price intelligence and local inventory, separately or together.</h1>
        <p className="text-lg text-slate-600">
          Pro helps evaluate the market. Store Finder monitors supported retailers near you and only surfaces
          store-level inventory that meets the evidence standard.
        </p>
        {preview && (
          <div className="mx-auto mt-2 rounded-full bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800 ring-1 ring-emerald-200">
            Founding preview active — paid features are unlocked while billing is being configured.
          </div>
        )}
      </header>

      <div className="mx-auto grid w-full max-w-6xl gap-5 lg:grid-cols-3">
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
            ) : (
              <Preview label="Pro preview enabled on this device" />
            )
          }
        />

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
            ) : (
              <Preview label="Store Finder preview enabled on this device" />
            )
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
            ) : (
              <Preview label="Complete preview enabled on this device" />
            )
          }
        />
      </div>

      {error && <p className="mx-auto max-w-xl rounded-lg bg-red-50 px-4 py-3 text-center text-sm text-red-800">{error}</p>}

      <section className="mx-auto grid max-w-6xl gap-4 sm:grid-cols-3">
        <Feature title="Accuracy before coverage" body="Retailers are added only after their store-level source is validated. An empty result is preferable to a false trip across town." />
        <Feature title="Useful at the moment of purchase" body="Store Finder combines radius, distance, evidence, verification time and retailer links so a collector can act quickly." />
        <Feature title="Independent service boundary" body="Inventory runs separately from pricing so release-day polling, retailer failures and scaling do not destabilize the core market API." />
      </section>

      <p className="mx-auto max-w-3xl text-center text-xs leading-5 text-slate-500">
        Store availability can change between verification and arrival. TCG Signal reports the source and time of each observation and never invents quantity.
      </p>
    </div>
  );
}

function Preview({ label }: { label: string }) {
  return <div className="rounded-lg bg-emerald-50 px-4 py-3 text-center text-sm font-semibold text-emerald-800">{label}</div>;
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
