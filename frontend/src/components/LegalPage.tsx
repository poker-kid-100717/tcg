import { site } from '../lib/site';

/** Shared layout for the Terms and Privacy pages: readable line length, dated, with a contact line. */
export function LegalPage({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="container-custom py-10 sm:py-14">
      <article className="panel mx-auto grid max-w-3xl gap-6 p-6 text-[15px] leading-7 text-slate-700 sm:p-10 [&_h2]:mt-4 [&_h2]:text-xl [&_li]:ml-5 [&_li]:list-disc [&_ul]:grid [&_ul]:gap-1.5">
        <header className="grid gap-2">
          <p className="eyebrow">{site.name}</p>
          <h1 className="text-3xl sm:text-4xl">{title}</h1>
          <p className="text-sm text-slate-500">Effective {site.legalEffectiveDate}</p>
        </header>
        {children}
        <p className="border-t border-slate-200 pt-6 text-sm text-slate-500">
          Questions? Email{' '}
          <a href={`mailto:${site.supportEmail}`} className="font-semibold text-pokemon-blue">
            {site.supportEmail}
          </a>
          .
        </p>
      </article>
    </div>
  );
}

export function SupportLink() {
  return (
    <a href={`mailto:${site.supportEmail}`} className="font-semibold text-pokemon-blue">
      {site.supportEmail}
    </a>
  );
}
