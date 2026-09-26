import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';

import { api } from '../api/client';
import { useAccount, useAccountActions } from '../api/collection';
import { Loading } from '../components/States';

export default function AccountPage() {
  const account = useAccount();
  const actions = useAccountActions();
  const navigate = useNavigate();
  const [mode, setMode] = useState<'save' | 'login'>('save');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  if (account.isPending) return <Loading />;
  const me = account.data!;
  const saved = me.signedIn && !me.isGuest;
  const pending = actions.register.isPending || actions.login.isPending;
  const error = mode === 'save' ? actions.register.error : actions.login.error;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const done = { onSuccess: () => navigate('/') };
    if (mode === 'save') actions.register.mutate({ email, password }, done);
    else actions.login.mutate({ email, password }, done);
  };

  return (
    <div className="container-custom grid max-w-2xl gap-6 py-8 sm:py-10">
      <header className="grid gap-1">
        <p className="eyebrow">Account</p>
        <h1 className="text-3xl sm:text-4xl">{saved ? 'Your account' : 'Keep your collection'}</h1>
      </header>

      {saved ? (
        <section className="panel grid gap-4 p-5">
          <p>
            Signed in as <strong>{me.email}</strong>.
          </p>
          <div className="flex flex-wrap gap-2">
            <a href={api.exportUrl} className="btn border border-slate-300 bg-white text-slate-800 hover:border-pokemon-blue" download>
              Export collection (CSV)
            </a>
            <button type="button" className="btn-ghost" onClick={() => actions.logout.mutate()}>
              Sign out
            </button>
          </div>
        </section>
      ) : (
        <section className="panel grid gap-4 p-5">
          <div role="tablist" aria-label="Account" className="flex gap-1 rounded-lg bg-slate-100 p-1 text-sm font-semibold">
            {(
              [
                ['save', me.isGuest ? 'Save this collection' : 'Create an account'],
                ['login', 'Sign in'],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                role="tab"
                aria-selected={mode === key}
                className={`flex-1 rounded-md px-3 py-2 ${mode === key ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-600'}`}
                onClick={() => setMode(key)}
              >
                {label}
              </button>
            ))}
          </div>
          <p className="text-sm text-slate-600">
            {mode === 'save'
              ? me.isGuest
                ? 'Your cards are kept in this browser. Add an email and password to keep them for good and open them anywhere.'
                : 'Create an account to keep a collection across devices.'
              : me.isGuest
                ? 'Signing in adds the cards in this browser to your account.'
                : 'Welcome back.'}
          </p>
          <form onSubmit={submit} className="grid gap-3">
            <label className="grid gap-1 text-sm font-medium">
              Email
              <input className="input" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
            </label>
            <label className="grid gap-1 text-sm font-medium">
              Password <span className="font-normal text-slate-500">{mode === 'save' ? '(at least 10 characters)' : ''}</span>
              <input
                className="input"
                type="password"
                autoComplete={mode === 'save' ? 'new-password' : 'current-password'}
                minLength={mode === 'save' ? 10 : undefined}
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </label>
            {error && (
              <p role="alert" className="text-sm text-red-700">
                {error.message}
              </p>
            )}
            <button type="submit" className="btn bg-pokemon-pokeblue text-white hover:brightness-110" disabled={pending}>
              {pending ? 'One moment…' : mode === 'save' ? 'Save my collection' : 'Sign in'}
            </button>
          </form>
        </section>
      )}

      {me.signedIn && (
        <section className="panel grid gap-3 border-red-200 p-5">
          <h2 className="text-lg">Delete {saved ? 'account' : 'this collection'}</h2>
          <p className="text-sm text-slate-600">Removes every card and wishlist entry for good. Export a CSV first if you want a copy.</p>
          <button
            type="button"
            className="btn w-fit border border-red-300 text-red-700 hover:bg-red-50"
            onClick={() => {
              if (window.confirm('Delete your collection and wishlist? This cannot be undone.')) actions.remove.mutate(undefined, { onSuccess: () => navigate('/') });
            }}
          >
            Delete everything
          </button>
        </section>
      )}
    </div>
  );
}
