import { judgeMarket, judgePrice, percentOver } from './fairPrice';

describe('fair price', () => {
  it('judges a price against MSRP in four bands', () => {
    expect(judgePrice(49.99, 49.99, 120).label).toBe('At or below MSRP');
    expect(judgePrice(39.99, 49.99, 120).tone).toBe('good');
    expect(judgePrice(56, 49.99, 120).label).toBe('Close to retail');
    expect(judgePrice(70, 49.99, 120).label).toBe('Above retail');
    expect(judgePrice(155.59, 49.99, 155.59).label).toBe('Far above retail');
  });

  it('falls back to the market price when the MSRP is unknown', () => {
    expect(judgePrice(80, null, 100).label).toBe('Below market');
    expect(judgePrice(102, null, 100).label).toBe('About market');
    expect(judgePrice(130, null, 100).label).toBe('Above market');
    expect(judgePrice(10, null, null).tone).toBe('neutral');
  });

  it('only judges the market itself when both prices are known', () => {
    expect(judgeMarket(49.99, 155.59)?.tone).toBe('bad');
    expect(judgeMarket(null, 155.59)).toBeNull();
    expect(judgeMarket(49.99, null)).toBeNull();
  });

  it('rounds the percentage to one decimal', () => {
    expect(percentOver(155.59, 49.99)).toBe(211.2);
    expect(percentOver(49.99, 49.99)).toBe(0);
  });
});
