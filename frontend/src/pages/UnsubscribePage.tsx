import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { api } from '../api/client';

/**
 * Linked from the footer of alert emails. Unsubscribing takes a click on purpose: email security
 * scanners open links automatically, and that must not turn anyone's alerts off.
 */
export default function UnsubscribePage() {
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const [state, setState] = useState<'idle' | 'working' | 'done' | 'failed'>('idle');

  const unsubscribe = async () => {
    setState('working');
    try {
      await api.unsubscribe(token);
      setState('done');
    } catch {
      setState('failed');
    }
  };

  return (
    <div className="container-custom grid justify-items-center py-12 sm:py-16">
      <section className="panel grid w-full max-w-md gap-5 p-6 sm:p-8">
        <div className="grid gap-1">
          <p className="eyebrow">TCG Signal</p>
          <h1 className="text-2xl">Alert emails</h1>
        </div>
        {state === 'done' ? (
          <p className="text-slate-700">
            Done. You won't get watchlist alert emails any more. Alerts still appear on your{' '}
            <Link to="/dashboard" className="font-semibold text-pokemon-blue">
              dashboard
            </Link>
            , and you can turn emails back on from your account.
          </p>
        ) : (
          <>
            <p className="text-slate-700">Stop emailing me watchlist alerts?</p>
            <button
              type="button"
              className="btn bg-pokemon-pokeblue text-white"
              disabled={!token || state === 'working'}
              onClick={unsubscribe}
            >
              {state === 'working' ? 'Saving…' : 'Stop alert emails'}
            </button>
            {(state === 'failed' || !token) && (
              <p role="alert" className="text-sm text-red-700">
                This link isn't valid any more. You can turn alert emails off from your account instead.
              </p>
            )}
          </>
        )}
      </section>
    </div>
  );
}
