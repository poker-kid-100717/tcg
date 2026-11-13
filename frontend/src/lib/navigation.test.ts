import { safeNext } from './navigation';

describe('safeNext', () => {
  it('keeps same-site paths', () => {
    expect(safeNext('/pro')).toBe('/pro');
    expect(safeNext('/cards/sv3pt5-6?variant=holofoil')).toBe('/cards/sv3pt5-6?variant=holofoil');
  });

  it('falls back for anything that could leave the site', () => {
    expect(safeNext(null)).toBe('/dashboard');
    expect(safeNext('https://evil.example')).toBe('/dashboard');
    expect(safeNext('//evil.example')).toBe('/dashboard');
    expect(safeNext('/\\evil.example')).toBe('/dashboard');
  });
});
