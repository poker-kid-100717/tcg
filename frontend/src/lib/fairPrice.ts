export type Tone = 'good' | 'ok' | 'caution' | 'bad' | 'neutral';

export interface Verdict {
  tone: Tone;
  /** A short label, e.g. "At or below MSRP". */
  label: string;
  /** One sentence of advice. */
  advice: string;
}

/** Percent above (+) or below (−) a reference price, to one decimal. */
export function percentOver(price: number, reference: number) {
  return Math.round((price / reference - 1) * 1000) / 10;
}

/**
 * How a price compares with retail. Bands: at or below MSRP is a buy; up to 15% over is effectively
 * retail; up to 50% over means waiting for a restock is worth it; beyond that you're paying the
 * secondary market for having missed the drop. Without an MSRP, it falls back to the market price.
 */
export function judgePrice(price: number, msrp: number | null, market: number | null): Verdict {
  if (msrp !== null && msrp > 0) {
    const over = percentOver(price, msrp);
    if (over <= 0) return { tone: 'good', label: 'At or below MSRP', advice: 'This is retail price or better.' };
    if (over <= 15) return { tone: 'ok', label: 'Close to retail', advice: 'A small markup. Fair if you missed the restock.' };
    if (over <= 50) return { tone: 'caution', label: 'Above retail', advice: 'Worth waiting for a restock if you can.' };
    return { tone: 'bad', label: 'Far above retail', advice: 'This is secondary-market pricing, well above MSRP.' };
  }
  if (market !== null && market > 0) {
    const over = percentOver(price, market);
    if (over <= -5) return { tone: 'good', label: 'Below market', advice: 'Cheaper than it usually sells for.' };
    if (over <= 5) return { tone: 'ok', label: 'About market', advice: 'In line with recent TCGplayer sales.' };
    return { tone: 'caution', label: 'Above market', advice: 'More than it usually sells for.' };
  }
  return { tone: 'neutral', label: 'No reference price', advice: "We don't have an MSRP or market price for this yet." };
}

/** Today's market price judged against MSRP, for the list itself. */
export function judgeMarket(msrp: number | null, market: number | null): Verdict | null {
  if (market === null || msrp === null || msrp <= 0) return null;
  return judgePrice(market, msrp, market);
}
