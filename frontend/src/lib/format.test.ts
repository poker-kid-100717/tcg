import { compareCollectorNumbers, formatChange, formatDate, formatPrice, formatTotal } from './format';

describe('format', () => {
  it('formats prices, with a dash when there is none', () => {
    expect(formatPrice(12.5)).toBe('$12.50');
    expect(formatPrice(null)).toBe('—');
    expect(formatPrice(undefined)).toBe('—');
  });

  it('rounds large totals to whole dollars', () => {
    expect(formatTotal(12345.67)).toBe('$12,346');
    expect(formatTotal(99.5)).toBe('$99.50');
  });

  it('signs percentage changes', () => {
    expect(formatChange(150)).toBe('+150.0%');
    expect(formatChange(-25)).toBe('-25.0%');
  });

  it('shows ISO dates without shifting a day in western time zones', () => {
    expect(formatDate('2023-09-22')).toBe('September 22, 2023');
    expect(formatDate('2023-09-22', 'short')).toBe('Sep 22, 2023');
    expect(formatDate(null)).toBe('—');
  });

  it('sorts collector numbers numerically, prefixed numbers last', () => {
    expect(['10', 'TG05', '2', '1', 'TG01'].sort(compareCollectorNumbers)).toEqual(['1', '2', '10', 'TG01', 'TG05']);
  });
});
