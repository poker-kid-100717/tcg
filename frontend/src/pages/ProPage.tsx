import { useState } from 'react';

import { api } from '../api/client';
import { useSession } from '../api/hooks';
import { Loading } from '../components/States';

export default function ProPage() {
  const session = useSession();
  const [working, setWorking] = useState<'monthly' | 'annual' | 'portal' | null>(null);
  const [error, setError] = useState('');

  if (session.isPending) return <Loading label="Loading Pro…" />;

  const account = session.data;
  const goCheckout = async (plan: 'monthly' | 'annual') => {
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

  return (
    <div className="container-custom grid gap-10 py-10 sm:py-14">
      <header className="mx-auto grid max-w-3xl gap-3 text-center">
        <p className="eyebrow">TCG Signal Pro</p>
        <h1 className="text-4xl sm:text-5xl">Understand the market behind the price.</h1>
        <p className="text-lg text-slate-600">
          Confidence, history, watch alerts and deal math for collectors who need more than a single “market value” number.
        </p>
        {!account?.billingConfigured && (
          <div className="mx-auto mt-2 rounded-full bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800 ring-1 ring-emerald-200">
            Founding preview active — every Pro feature is unlocked while billing is being configured.
          </div>
        )}
      </header>

      <div className="mx-auto grid w-full max-w-4xl gap-5 md:grid-cols-2">
        <Plan
          name="Free"
          price="$0"
          description="For looking up cards and following the broad market."
          features={['Card and set lookup', 'Current TCGplayer price data', 'Basic price history', 'Market movers and signals', 'Up to 3 watched printings when billing is live']}
        />
        <Plan
          featured
          name="Pro"
          price="$9.99/mo"
          description="For collectors making buy, sell, grade and trade decisions."
          features={['Market Confidence with transparent reasons', 'Deal Analyzer and break-even math', 'Unlimited watchlist and threshold alerts', 'Personal dashboard', 'Longer signal context and model outlook']}
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
              <div className="rounded-lg bg-emerald-50 px-4 py-3 text-center text-sm font-semibold text-emerald-800">
                Pro preview enabled on this device
              </div>
            )
          }
        />
      </div>

      {error && <p className="mx-auto max-w-xl rounded-lg bg-red-50 px-4 py-3 text-center text-sm text-red-800">{error}</p>}

      <section className="mx-auto grid max-w-4xl gap-4 sm:grid-cols-3">
        <Feature title="Confidence, not false precision" body="The app scores freshness, history, volatility and spread—and explicitly says when transaction-level liquidity is unknown." />
        <Feature title="Useful at the moment of purchase" body="Deal Analyzer turns an asking price, fees, tax and shipping into descriptive break-even and net-proceeds math." />
        <Feature title="Your market, not everyone’s" body="Watch exact printings, set thresholds and surface changes on a personal dashboard after each daily snapshot." />
      </section>

      <p className="mx-auto max-w-3xl text-center text-xs leading-5 text-slate-500">
        Pricing signals are informational and are not financial advice. TCG Signal does not guarantee a sale price, future appreciation, or model accuracy.
      </p>
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
