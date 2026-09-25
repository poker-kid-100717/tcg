const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
const usdCompact = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });

export const formatPrice = (value: number | null | undefined): string =>
  value === null || value === undefined ? '—' : usd.format(value);

/** Whole dollars for big totals such as a set's market value. */
export const formatTotal = (value: number): string => (value >= 1000 ? usdCompact.format(value) : usd.format(value));

export const formatChange = (percent: number): string => `${percent > 0 ? '+' : ''}${percent.toFixed(1)}%`;

/** API dates are ISO calendar dates ("2023-09-22"); show them without time-zone drift. */
export const formatDate = (isoDate: string | null | undefined, style: 'long' | 'short' = 'long'): string => {
  if (!isoDate) return '—';
  const [year, month, day] = isoDate.slice(0, 10).split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.toLocaleDateString('en-US', {
    timeZone: 'UTC',
    year: 'numeric',
    month: style === 'long' ? 'long' : 'short',
    day: 'numeric',
  });
};

/** Collector numbers sort numerically first ("2" before "10"), then prefixed ones ("TG05"). */
export const compareCollectorNumbers = (a: string, b: string): number => {
  const key = (n: string) => {
    const digits = parseInt(n.replace(/^\D*/, ''), 10);
    return [/^\d/.test(n) ? 0 : 1, Number.isNaN(digits) ? 0 : digits] as const;
  };
  const [ap, an] = key(a);
  const [bp, bn] = key(b);
  return ap - bp || an - bn || a.localeCompare(b);
};
