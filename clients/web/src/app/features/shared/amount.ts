import { StatTone } from '../../ui';

/** Green for money coming in / owed to me, red for money going out / owed by me. */
export function amountTone(amount: number): StatTone {
  if (amount > 0) return 'positive';
  if (amount < 0) return 'negative';
  return 'muted';
}

export function amountClass(amount: number): string {
  if (amount > 0) return 'text-success';
  if (amount < 0) return 'text-danger';
  return 'text-muted';
}
