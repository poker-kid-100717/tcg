import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';

import { api } from '../api/client';
import { useSession, useVerifySignInCode } from '../api/hooks';
import { Loading } from '../components/States';
import { safeNext } from '../lib/navigation';

export default function SignInPage() {
  const session = useSession();
  const verify = useVerifySignInCode();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = safeNext(params.get('next'));

  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [step, setStep] = useState<'email' | 'code'>('email');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  if (session.isPending) return <Loading label="Loading…" />;
  const account = session.data;

  if (account?.signedIn) {
    return (
      <Shell>
        <p className="text-slate-700">
          You're signed in as <strong>{account.email}</strong>.
        </p>
        <Link to={next} className="btn bg-pokemon-pokeblue text-white">
          Continue
        </Link>
      </Shell>
    );
  }

  if (account && !account.signInAvailable) {
    return (
      <Shell>
        <p className="text-slate-700">
          Email sign-in isn't switched on yet. Your watchlist and master sets are saved in this browser in the meantime.
        </p>
      </Shell>
    );
  }

  const sendCode = async (event?: FormEvent) => {
    event?.preventDefault();
    setSending(true);
    setError('');
    setNotice('');
    try {
      await api.requestSignInCode(email.trim());
      setStep('code');
      setCode('');
      setNotice(`We sent a 6-digit code to ${email.trim()}.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'We couldn’t send a code.');
    } finally {
      setSending(false);
    }
  };

  const submitCode = (event: FormEvent) => {
    event.preventDefault();
    setError('');
    verify.mutate(
      { email: email.trim(), code: code.trim() },
      {
        onSuccess: () => navigate(next, { replace: true }),
        onError: (e) => setError(e instanceof Error ? e.message : 'That code didn’t work.'),
      },
    );
  };

  return (
    <Shell>
      {step === 'email' ? (
        <form className="grid gap-4" onSubmit={sendCode}>
          <p className="text-slate-600">
            Enter your email and we'll send you a code. There's no password. Anything you've saved in this browser comes
            with you.
          </p>
          <label className="grid gap-1.5 text-sm font-semibold text-slate-700">
            Email
            <input
              className="input"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </label>
          <button type="submit" className="btn bg-pokemon-pokeblue text-white" disabled={sending || !email.trim()}>
            {sending ? 'Sending…' : 'Email me a code'}
          </button>
          <p className="text-xs text-slate-500">
            By signing in you agree to the{' '}
            <Link to="/terms" className="font-semibold text-pokemon-blue">
              Terms of Service
            </Link>{' '}
            and{' '}
            <Link to="/privacy" className="font-semibold text-pokemon-blue">
              Privacy Policy
            </Link>
            .
          </p>
        </form>
      ) : (
        <form className="grid gap-4" onSubmit={submitCode}>
          {notice && <p className="text-slate-600">{notice} It expires in 10 minutes.</p>}
          <label className="grid gap-1.5 text-sm font-semibold text-slate-700">
            6-digit code
            <input
              className="input text-center font-mono text-xl tracking-[0.4em]"
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]{6}"
              maxLength={6}
              required
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
            />
          </label>
          <button type="submit" className="btn bg-pokemon-pokeblue text-white" disabled={verify.isPending || code.length !== 6}>
            {verify.isPending ? 'Signing in…' : 'Sign in'}
          </button>
          <div className="flex flex-wrap justify-between gap-2 text-sm">
            <button type="button" className="btn-ghost px-2 py-1" disabled={sending} onClick={() => sendCode()}>
              {sending ? 'Sending…' : 'Send a new code'}
            </button>
            <button
              type="button"
              className="btn-ghost px-2 py-1"
              onClick={() => {
                setStep('email');
                setError('');
                setNotice('');
              }}
            >
              Use a different email
            </button>
          </div>
        </form>
      )}

      {error && (
        <p role="alert" className="rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </p>
      )}
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="container-custom grid justify-items-center py-12 sm:py-16">
      <section className="panel grid w-full max-w-md gap-5 p-6 sm:p-8">
        <div className="grid gap-1">
          <p className="eyebrow">TCG Signal</p>
          <h1 className="text-2xl">Sign in</h1>
        </div>
        {children}
      </section>
    </div>
  );
}
