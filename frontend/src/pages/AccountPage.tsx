import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';

import { api } from '../api/client';
import { useSession, useSignOut, useUpdatePreferences } from '../api/hooks';
import { ErrorState, Loading } from '../components/States';

export default function AccountPage() {
  const session = useSession();
  const preferences = useUpdatePreferences();
  const signOut = useSignOut();
  const navigate = useNavigate();
  const [billingError, setBillingError] = useState('');
  const [openingBilling, setOpeningBilling] = useState(false);

  if (session.isPending) return <Loading label="Loading your account…" />;
  if (session.error) return <ErrorState error={session.error} onRetry={() => session.refetch()} />;
  const account = session.data;

  if (!account.signedIn) {
    return (
      <Page>
        <p className="text-slate-700">
          You're using TCG Signal without an account, so your watchlist and master sets live in this browser only.
        </p>
        {account.signInAvailable ? (
          <Link to="/sign-in?next=/account" className="btn bg-pokemon-pokeblue text-white">
            Sign in with email
          </Link>
        ) : (
          <p className="text-sm text-slate-500">Email sign-in isn't switched on yet.</p>
        )}
      </Page>
    );
  }

  const manageBilling = async () => {
    setOpeningBilling(true);
    setBillingError('');
    try {
      const result = await api.billingPortal();
      window.location.assign(result.url);
    } catch (e) {
      setBillingError(e instanceof Error ? e.message : 'Unable to open billing.');
      setOpeningBilling(false);
    }
  };

  const plan = account.isPro ? 'Pro' : account.hasStoreFinder ? 'Store Finder' : 'Free';

  return (
    <Page>
      <dl className="grid gap-3 text-sm">
        <div className="flex justify-between gap-4">
          <dt className="text-slate-500">Email</dt>
          <dd className="font-semibold text-slate-900">{account.email}</dd>
        </div>
        <div className="flex justify-between gap-4">
          <dt className="text-slate-500">Plan</dt>
          <dd className="font-semibold text-slate-900">
            {plan} · {account.mode}
          </dd>
        </div>
      </dl>

      <label className="flex items-start gap-3 rounded-lg bg-slate-50 p-4 text-sm">
        <input
          type="checkbox"
          className="mt-0.5 h-4 w-4"
          checked={account.emailAlerts}
          disabled={preferences.isPending}
          onChange={(e) => preferences.mutate({ emailAlerts: e.target.checked })}
        />
        <span>
          <span className="block font-semibold text-slate-900">Email me watchlist alerts</span>
          <span className="text-slate-600">One email a day at most, after TCGplayer prices are recorded.</span>
        </span>
      </label>
      {preferences.error && (
        <p role="alert" className="text-sm text-red-700">
          {preferences.error.message}
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        {account.billingConfigured && account.subscriptionStatus && (
          <button type="button" className="btn bg-pokemon-pokeblue text-white" disabled={openingBilling} onClick={manageBilling}>
            {openingBilling ? 'Opening…' : 'Manage subscription'}
          </button>
        )}
        <button
          type="button"
          className="btn-ghost ring-1 ring-slate-200"
          disabled={signOut.isPending}
          onClick={() => signOut.mutate(undefined, { onSuccess: () => navigate('/') })}
        >
          {signOut.isPending ? 'Signing out…' : 'Sign out'}
        </button>
      </div>
      {billingError && (
        <p role="alert" className="text-sm text-red-700">
          {billingError}
        </p>
      )}
    </Page>
  );
}

function Page({ children }: { children: React.ReactNode }) {
  return (
    <div className="container-custom grid justify-items-center py-12 sm:py-16">
      <section className="panel grid w-full max-w-md gap-5 p-6 sm:p-8">
        <div className="grid gap-1">
          <p className="eyebrow">TCG Signal</p>
          <h1 className="text-2xl">Your account</h1>
        </div>
        {children}
      </section>
    </div>
  );
}
