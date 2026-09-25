import { describe, expect, it } from 'vitest';
import {
  formatPrice,
  formatRarity,
  readableCardId,
  truncateText,
  calculatePriceTrend,
} from './formatters';

describe('formatPrice', () => {
  it('formats a number as USD currency', () => {
    expect(formatPrice(9.99)).toBe('$9.99');
  });

  it('rounds to two decimal places', () => {
    expect(formatPrice(10)).toBe('$10.00');
  });

  it('returns N/A for null or undefined', () => {
    expect(formatPrice(null)).toBe('N/A');
    expect(formatPrice(undefined)).toBe('N/A');
  });
});

describe('formatRarity', () => {
  it('returns a known label and class for a recognized rarity', () => {
    expect(formatRarity('Rare Holo')).toEqual({
      label: 'Holo Rare',
      className: 'bg-indigo-100 text-indigo-800',
    });
  });

  it('falls back to the raw rarity for an unrecognized value', () => {
    expect(formatRarity('Something New')).toEqual({
      label: 'Something New',
      className: 'badge-common',
    });
  });

  it('handles a missing rarity', () => {
    expect(formatRarity(undefined)).toEqual({ label: 'Unknown', className: 'badge-common' });
  });
});

describe('readableCardId', () => {
  it('splits set code and number into a readable form', () => {
    expect(readableCardId('swsh12-179')).toBe('SWSH12 #179');
  });

  it('uppercases ids that do not match the expected shape', () => {
    expect(readableCardId('abc')).toBe('ABC');
  });

  it('handles an empty id', () => {
    expect(readableCardId('')).toBe('');
  });
});

describe('truncateText', () => {
  it('leaves short text untouched', () => {
    expect(truncateText('short', 10)).toBe('short');
  });

  it('truncates long text and appends an ellipsis', () => {
    expect(truncateText('this is a long sentence', 10)).toBe('this is a ...');
  });
});

describe('calculatePriceTrend', () => {
  it('reports an upward trend when price increased', () => {
    const history = [{ price: 10 }, { price: 15 }];
    expect(calculatePriceTrend(history)).toEqual({ trend: 50, direction: 'up' });
  });

  it('reports a downward trend when price decreased', () => {
    const history = [{ price: 20 }, { price: 10 }];
    expect(calculatePriceTrend(history)).toEqual({ trend: 50, direction: 'down' });
  });

  it('returns neutral with fewer than two data points', () => {
    expect(calculatePriceTrend([{ price: 10 }])).toEqual({ trend: 0, direction: 'neutral' });
  });
});
