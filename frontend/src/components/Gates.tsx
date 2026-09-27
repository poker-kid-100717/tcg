import type { ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';

/** The small PRO marker next to Pro features. */
export function ProBadge() {
  return <span className="rounded-full bg-pokemon-yellow px-2 py-0.5 text-[11px] font-extrabold text-pokemon-pokeblue">PRO</span>;
}

/** Link to the account page's sign-in, returning here afterwards. */
export function SignInLink({ className = 'btn bg-pokemon-pokeblue text-white', children = 'Sign in' }: { className?: string; children?: ReactNode }) {
  const location = useLocation();
  const returnUrl = `${location.pathname}${location.search}`;
  return (
    <Link to={`/account?returnUrl=${encodeURIComponent(returnUrl)}`} className={className}>
      {children}
    </Link>
  );
}

/** Shown in place of an account-only page for visitors. */
export function SignInPrompt({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="container-custom py-12">
      <div className="panel mx-auto grid max-w-2xl gap-4 p-8 text-center">
        <h1 className="text-3xl">{title}</h1>
        <p className="text-slate-600">{detail}</p>
        <div className="mx-auto">
          <SignInLink>Sign in or create an account</SignInLink>
        </div>
        <p className="text-xs text-slate-500">Free accounts can watch up to 3 cards. No password to remember: sign in with your existing account.</p>
      </div>
    </div>
  );
}

/**
 * A locked Pro feature: says what it does and what it would show, rather than just "upgrade". `preview` is rendered
 * blurred and inert behind the message so the value is visible.
 */
export function ProLock({ title, detail, points, preview, compact = false }: {
  title: string;
  detail: string;
  points?: string[];
  preview?: ReactNode;
  compact?: boolean;
}) {
  return (
    <section className={`panel relative overflow-hidden ${compact ? 'p-5' : 'p-6 sm:p-8'}`}>
      {preview && (
        <div aria-hidden="true" className="pointer-events-none absolute inset-0 select-none opacity-40 blur-[3px]">
          {preview}
        </div>
      )}
      <div className={`relative grid gap-3 ${preview ? 'rounded-xl bg-white/90 p-5 shadow-sm' : ''}`}>
        <div className="flex items-center gap-2">
          <h2 className="text-lg">{title}</h2>
          <ProBadge />
        </div>
        <p className="max-w-2xl text-sm text-slate-600">{detail}</p>
        {points && points.length > 0 && (
          <ul className="grid gap-1 text-sm text-slate-700">
            {points.map((point) => <li key={point}>✓ {point}</li>)}
          </ul>
        )}
        <Link to="/pro" className="btn w-fit bg-pokemon-pokeblue text-white hover:brightness-110">
          See TCG Signal Pro
        </Link>
      </div>
    </section>
  );
}

/** A freshness chip: fresh, aging, stale, or no data, with the data's date. */
export function FreshnessChip({ freshness }: { freshness: { state: string; label: string } | null | undefined }) {
  if (!freshness) return null;
  const tone = freshness.state === 'Fresh'
    ? 'bg-emerald-50 text-emerald-800 ring-emerald-200'
    : freshness.state === 'Aging'
      ? 'bg-amber-50 text-amber-900 ring-amber-200'
      : 'bg-red-50 text-red-800 ring-red-200';
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-semibold ring-1 ${tone}`}>{freshness.label}</span>;
}
