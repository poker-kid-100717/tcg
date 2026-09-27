import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';

import { api } from '../api/client';
import { useAuthOptions, useDeleteAccount, useLocalLogin, useLogout, useMe } from '../api/hooks';
import { ProBadge } from '../components/Gates';
import { ErrorState, Loading } from '../components/States';
import { formatDate } from '../lib/format';

/** Only a path on this site ("/dashboard"), never another origin. */
export const safeReturnUrl = (value: string | null) =>
  value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\') ? value : '/dashboard';

const planName: Record<string, string> = {
  free: 'Free',
  monthly: 'Pro · monthly',
  annual: 'Pro · annual',
  storefinder: 'Store Finder',
  complete: 'Complete',
};

export default function AccountPage() {
  const me = useMe();
  const [params] = useSearchParams();
  const returnUrl = safeReturnUrl(params.get('returnUrl'));

  if (me.isPending) return <Loading label="Loading your account…" />;
  if (me.error) return <ErrorState error={me.error} onRetry={() => me.refetch()} />;

  return (
    <div className="container-custom grid max-w-3xl gap-6 py-8 sm:py-10">
      <header className="grid gap-2">
        <p className="eyebrow">Account</p>
        <h1 className="text-3xl sm:text-4xl">{me.data.signedIn ? 'Your account' : 'Sign in to TCG Signal'}</h1>
      </header>
      {params.get('signin') === 'failed' && (
        <p role="alert" className="rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800">
          Sign-in didn’t complete. Please try again.
        </p>
      )}
      {me.data.signedIn ? <SignedIn /> : <SignIn returnUrl={returnUrl} />}
    </div>
  );
}

function SignIn({ returnUrl }: { returnUrl: string }) {
  const options = useAuthOptions();
  const login = useLocalLogin();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    login.mutate(email.trim(), { onSuccess: () => navigate(returnUrl) });
  };

  if (options.isPending) return <Loading label="Loading sign-in options…" />;
  const available = options.data?.signInAvailable;

  return (
    <section className="panel grid gap-5 p-6">
      <p className="text-slate-600">
        An account keeps your watchlist, alerts and master sets. Free accounts can watch up to 3 cards; Pro adds alerts,
        Market Intelligence, the signal center and the Deal Analyzer.
      </p>
      {options.data?.providerSignIn && (
        // A full-page navigation: the API runs the provider's sign-in and sets an HttpOnly session cookie.
        <a href={`/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`} className="btn w-fit bg-pokemon-pokeblue text-white">
          Continue with {options.data?.providerName}
        </a>
      )}
      {options.data?.localLogin && (
        <form onSubmit={submit} className="grid gap-3 rounded-lg border border-dashed border-amber-300 bg-amber-50 p-4">
          <p className="text-sm font-semibold text-amber-900">Development sign-in (never enabled in production)</p>
          <label className="grid gap-1 text-sm font-semibold">
            Email
            <input className="input" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
          </label>
          <button type="submit" className="btn w-fit bg-pokemon-pokeblue text-white" disabled={login.isPending}>
            {login.isPending ? 'Signing in…' : 'Sign in'}
          </button>
          {login.error && <p className="text-sm text-red-700">{login.error.message}</p>}
        </form>
      )}
      {!available && <p className="text-sm text-slate-500">Sign-in isn’t configured on this deployment yet.</p>}
      <p className="text-xs text-slate-500">
        TCG Signal never sees or stores a password. We keep your email address and your app data; you can delete both here at any time.
      </p>
    </section>
  );
}

function SignedIn() {
  const me = useMe();
  const logout = useLogout();
  const remove = useDeleteAccount();
  const navigate = useNavigate();
  const [portalError, setPortalError] = useState<string | null>(null);
  const [confirm, setConfirm] = useState('');
  const account = me.data!.account!;

  const openPortal = async () => {
    setPortalError(null);
    try {
      const link = await api.billingPortal();
      window.location.assign(link.url);
    } catch (error) {
      setPortalError(error instanceof Error ? error.message : 'Billing is unavailable right now.');
    }
  };

  return (
    <>
      <section className="panel grid gap-4 p-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-sm text-slate-500">Signed in as</p>
            <p className="font-semibold text-slate-900">{me.data!.email ?? me.data!.displayName ?? 'your account'}</p>
          </div>
          <button type="button" className="btn-ghost" disabled={logout.isPending} onClick={() => logout.mutate(undefined, { onSuccess: () => navigate('/') })}>
            Sign out
          </button>
        </div>
      </section>

      <section className="panel grid gap-4 p-6" aria-labelledby="plan-heading">
        <div className="flex items-center gap-2">
          <h2 id="plan-heading" className="text-lg">Plan</h2>
          {account.isPro && <ProBadge />}
        </div>
        <dl className="grid gap-2 text-sm sm:grid-cols-2">
          <Row label="Plan" value={planName[account.plan] ?? account.plan} />
          <Row label="Status" value={account.subscriptionStatus ?? 'No subscription'} />
          {account.currentPeriodEnd && (
            <Row label={account.cancelAtPeriodEnd ? 'Access ends' : 'Renews'} value={formatDate(account.currentPeriodEnd)} />
          )}
        </dl>
        {account.paymentIssue && (
          <p role="alert" className="rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-900">
            Your last payment didn’t go through. Update your payment method to keep Pro.
          </p>
        )}
        <div className="flex flex-wrap gap-2">
          {account.canManageBilling && (
            <button type="button" className="btn bg-pokemon-pokeblue text-white" onClick={openPortal}>
              Manage subscription
            </button>
          )}
          {!account.isPro && <Link to="/pro" className="btn bg-pokemon-yellow text-pokemon-pokeblue">Upgrade to Pro</Link>}
        </div>
        {portalError && <p className="text-sm text-red-700">{portalError}</p>}
        <p className="text-xs text-slate-500">Billing is handled by Stripe. Changes can take a moment to show here after checkout.</p>
      </section>

      <section className="panel grid gap-3 border-red-200 p-6" aria-labelledby="delete-heading">
        <h2 id="delete-heading" className="text-lg text-red-800">Delete account</h2>
        <p className="text-sm text-slate-600">
          Permanently deletes your account, watchlist, alerts and master sets, and cancels any subscription immediately.
          This can’t be undone.
        </p>
        <label className="grid gap-1 text-sm font-semibold">
          Type DELETE to confirm
          <input className="input max-w-xs" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        </label>
        <button
          type="button"
          className="btn w-fit bg-red-700 text-white disabled:opacity-50"
          disabled={confirm !== 'DELETE' || remove.isPending}
          onClick={() => remove.mutate(undefined, { onSuccess: () => navigate('/') })}
        >
          {remove.isPending ? 'Deleting…' : 'Delete my account'}
        </button>
        {remove.error && <p className="text-sm text-red-700">{remove.error.message}</p>}
      </section>
    </>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2">
      <dt className="text-slate-500">{label}</dt>
      <dd className="font-semibold text-slate-900">{value}</dd>
    </div>
  );
}
