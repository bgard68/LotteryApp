/** "$800 million" style display for jackpot amounts; null in -> null out (hidden in UI). */
export function formatJackpot(amount: number | null | undefined): string | null {
  if (amount == null) return null;
  if (amount >= 1_000_000_000) return `$${trim(amount / 1_000_000_000)} billion`;
  if (amount >= 1_000_000) return `$${trim(amount / 1_000_000)} million`;
  return `$${Math.round(amount).toLocaleString('en-US')}`;
}

function trim(value: number): string {
  const rounded = Math.round(value * 10) / 10;
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1);
}
