import { formatJackpot } from './money';

describe('formatJackpot', () => {
  it('formats millions and billions compactly', () => {
    expect(formatJackpot(800_000_000)).toBe('$800 million');
    expect(formatJackpot(344_200_000)).toBe('$344.2 million');
    expect(formatJackpot(1_500_000_000)).toBe('$1.5 billion');
  });

  it('formats sub-million amounts as plain dollars', () => {
    expect(formatJackpot(950_000)).toBe('$950,000');
  });

  it('returns null for missing amounts so the UI hides them', () => {
    expect(formatJackpot(null)).toBeNull();
    expect(formatJackpot(undefined)).toBeNull();
  });
});
