import type { CardCondition } from '../api/types';

export const CONDITIONS: { value: CardCondition; label: string; short: string }[] = [
  { value: 'NearMint', label: 'Near Mint', short: 'NM' },
  { value: 'LightlyPlayed', label: 'Lightly Played', short: 'LP' },
  { value: 'ModeratelyPlayed', label: 'Moderately Played', short: 'MP' },
  { value: 'HeavilyPlayed', label: 'Heavily Played', short: 'HP' },
  { value: 'Damaged', label: 'Damaged', short: 'DMG' },
];

export const conditionLabel = (condition: CardCondition) => CONDITIONS.find((c) => c.value === condition)?.label ?? condition;

/** "reverseHolofoil" → "Reverse Holofoil". */
export const variantLabel = (variant: string) =>
  variant.replace(/(?<=[a-z0-9])(?=[A-Z])/g, ' ').replace(/^./, (c) => c.toUpperCase());

export const formatPercent = (fraction: number, digits = 0) => `${(fraction * 100).toFixed(digits)}%`;
