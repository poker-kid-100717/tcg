import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { api } from '../api/client';
import { useMe, usePlans } from '../api/hooks';
import type { Plan } from '../api/types';
import { SignInLink } from '../components/Gates';
import { Loading } from '../components/States';

type CheckoutPlan = 'monthly' | 'annual' | 'storefinder' | 'complete';

const price = (plan: Plan | undefined) =>
  plan ? new Intl.NumberFormat('en-US', { style: 'currency', currency: plan.currency, maximumFractionDigits: plan.price % 1 ? 2 : 0 }).format(plan.price) : '—';

const COMPARISON: [string, string, string][] = [
  ['Card prices, sets, search, 30-day history', '✓', '✓'],
  ['Market movers, sleepers and downtrends', '✓', '✓'],
  ['Watchlist', 'Up to 3 cards', 'Unlimited'],
  ['Price, move and signal alerts', '—', '✓'],
  ['True Market: confidence score with reasons', '—', '✓'],
  ['Verified sold comps kept separate from reference prices', '—', '✓'],
  ['Signal center (9 published rules)', '—', '✓'],
  ['“Why is this moving?” explanations', '—', '✓'],
  ['Deal Analyzer with fee presets and break-even', '—', '✓'],
  ['Up to a year of price history', '—', '✓'],
];

export default function ProPage() {
  const me = useMe();
  const plans = usePlans();
  const [params] = useSearchParams();
  const [working, setWorking] = useState<CheckoutPlan | 'portal' | null>(null);
  const [error, setError] = useState('');
  const checkoutState = params.get('checkout');

  // Coming back from Checkout: Pro unlocks when Stripe's webhook confirms payment, usually within seconds.
  useEffect(() => {
    if (checkoutState !== 'success' || me.data?.account?.isPro) return;
    const timer = window.setInterval(() => me.refetch(), 3000);
    return () => window.clearInterval(timer);
  }, [checkoutState, me.data?.account?.isPro]);

  if (me.isPending || plans.isPending) return <Loading label="Loading plans…" />;

  const account = me.data?.account ?? null;
  const signedIn = Boolean(me.data?.signedIn);
  const plan = (id: Plan['id']) => plans.data?.find((p) => p.id === id);

  const goCheckout = async (id: CheckoutPlan) => {
    setWorking(id);
    setError('');
    try {
      window.location.assign((await api.checkout(id)).url);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to start checkout.');
      setWorking(null);
    }
  };

  const manage = async () => {
    setWorking('portal');
    setError('');
    try {
      window.location.assign((await api.billingPortal()).url);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to open billing.');
      setWorking(null);
    }
  };

  const action = (id: CheckoutPlan, label: string, owned: boolean, secondary = false) => {
    const p = plan(id);
    const style = `btn w-full ${secondary ? 'bg-white text-pokemon-pokeblue ring-1 ring-pokemon-pokeblue' : 'bg-pokemon-pokeblue text-white'}`;
    if (owned && account?.canManageBilling) {
      return (
        <button type="button" className="btn w-full bg-pokemon-pokeblue text-white" disabled={working !== null} onClick={manage}>
          {working === 'portal' ? 'Opening…' : 'Manage subscription'}
        </button>
      );
    }
    if (owned) return <p className="rounded-lg bg-emerald-50 px-4 py-3 text-center text-sm font-semibold text-emerald-800">Your current plan</p>;
    if (!p?.available) return <p className="rounded-lg bg-slate-50 px-4 py-3 text-center text-sm text-slate-600">Not available yet</p>;
    if (!signedIn) return <SignInLink className={style}>Sign in to subscribe</SignInLink>;
    return (
      <button type="button" className={style} disabled={working !== null} onClick={() => goCheckout(id)}>
        {working === id ? 'Opening checkout…' : label}
      </button>
    );
  };

  const monthly = plan('monthly');
  const annual = plan('annual');
  const annualSaving = monthly && annual && monthly.price > 0 ? Math.round((1 - annual.price / (monthly.price * 12)) * 100) : null;
  const hasPro = Boolean(account?.isPro);

  return (
    <div className="container-custom grid gap-10 py-10 sm:py-14">
      <header className="mx-auto grid max-w-3xl gap-3 text-center">
        <p className="eyebrow">TCG Signal Pro</p>
        <h1 className="text-4xl sm:text-5xl">Know what a card is really worth before you buy, sell or trade.</h1>
        <p className="text-lg text-slate-600">
          Pro shows how much to trust a price, what moved and why, and what a deal nets after real costs — with the evidence
          behind every number.
        </p>
      </header>

      {checkoutState === 'success' && (
        <p role="status" className="mx-auto max-w-xl rounded-lg bg-emerald-50 px-4 py-3 text-center text-sm text-emerald-900">
          {hasPro ? 'You’re on Pro. Thanks for subscribing!' : 'Payment received. Pro unlocks as soon as Stripe confirms it — usually within a few seconds.'}
        </p>
      )}
      {checkoutState === 'cancelled' && (
        <p className="mx-auto max-w-xl rounded-lg bg-slate-50 px-4 py-3 text-center text-sm text-slate-700">Checkout was cancelled; nothing was charged.</p>
      )}

      <div className="mx-auto grid w-full max-w-4xl gap-5 md:grid-cols-2">
        <PlanCard name="Free" price="$0" cadence="" description="Prices, sets, market movers and a 3-card watchlist." features={['30-day price history', 'Market movers, sleepers and downtrends', 'Watch up to 3 cards']}>
          {!signedIn && <SignInLink className="btn w-full bg-white text-pokemon-pokeblue ring-1 ring-pokemon-pokeblue">Create a free account</SignInLink>}
        </PlanCard>
        <PlanCard
          featured
          name="Pro"
          price={price(monthly)}
          cadence="/month"
          description={`or ${price(annual)}/year${annualSaving && annualSaving > 0 ? ` (save ${annualSaving}%)` : ''}. Cancel any time.`}
          features={['Everything in Free', 'Unlimited watchlist with price, move and signal alerts', 'True Market confidence, verified comps and signals', 'Deal Analyzer and a year of history']}
        >
          <div className="grid gap-2">
            {action('monthly', `Choose monthly · ${price(monthly)}`, hasPro)}
            {!hasPro && action('annual', `Choose annual · ${price(annual)}`, false, true)}
          </div>
        </PlanCard>
      </div>

      {error && <p role="alert" className="mx-auto max-w-xl rounded-lg bg-red-50 px-4 py-3 text-center text-sm text-red-800">{error}</p>}

      <section className="mx-auto w-full max-w-4xl overflow-x-auto">
        <table className="w-full min-w-[320px] text-left text-sm">
          <caption className="sr-only">Free and Pro compared</caption>
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th scope="col" className="py-2 pr-2 font-semibold">Feature</th>
              <th scope="col" className="w-24 py-2 text-center font-semibold">Free</th>
              <th scope="col" className="w-24 py-2 text-center font-semibold">Pro</th>
            </tr>
          </thead>
          <tbody>
            {COMPARISON.map(([feature, free, pro]) => (
              <tr key={feature} className="border-b border-slate-100">
                <th scope="row" className="py-2 pr-2 font-normal text-slate-700">{feature}</th>
                <td className="py-2 text-center text-slate-500">{free}</td>
                <td className="py-2 text-center font-semibold text-emerald-700">{pro}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="mx-auto grid w-full max-w-4xl gap-5 md:grid-cols-2">
        <PlanCard name="Store Finder" price={price(plan('storefinder'))} cadence="/month" description="Local Pokémon inventory at supported retailers, with the evidence behind each result." features={['Store-level availability only', 'Verification time and source on every result']}>
          {action('storefinder', `Add Store Finder · ${price(plan('storefinder'))}`, Boolean(account?.hasStoreFinder), true)}
        </PlanCard>
        <PlanCard name="Complete" price={price(plan('complete'))} cadence="/month" description="Pro and Store Finder together." features={['Everything in Pro', 'Everything in Store Finder']}>
          {action('complete', `Choose Complete · ${price(plan('complete'))}`, account?.plan === 'complete', true)}
        </PlanCard>
      </section>

      <p className="mx-auto max-w-3xl text-center text-xs leading-5 text-slate-500">
        Payments are processed by Stripe. TCG Signal describes market data; it is not financial advice and doesn’t guarantee
        prices, sales or returns. Prices shown in {monthly?.currency ?? 'USD'}; taxes may apply.
      </p>
    </div>
  );
}

function PlanCard({ name, price, cadence, description, features, featured = false, children }: {
  name: string;
  price: string;
  cadence: string;
  description: string;
  features: string[];
  featured?: boolean;
  children?: React.ReactNode;
}) {
  return (
    <section className={`panel grid content-start gap-5 p-6 ${featured ? 'border-pokemon-blue ring-2 ring-pokemon-blue/10' : ''}`}>
      <div>
        <p className="eyebrow">{name}</p>
        <p className="mt-1 text-3xl font-bold text-slate-900">{price}<span className="text-base font-semibold text-slate-500">{cadence}</span></p>
        <p className="mt-2 text-sm text-slate-600">{description}</p>
      </div>
      <ul className="grid gap-2 text-sm text-slate-700">
        {features.map((feature) => <li key={feature} className="flex gap-2"><span className="font-bold text-emerald-700">✓</span><span>{feature}</span></li>)}
      </ul>
      {children}
    </section>
  );
}
