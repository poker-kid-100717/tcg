// Response shapes of the price guide API (backend/Pricing/PriceGuideContracts.cs).

export interface SetSummary {
  id: string;
  name: string;
  series: string;
  releaseDate: string | null;
  printedTotal: number;
  total: number;
  logoUrl: string | null;
  symbolUrl: string | null;
}

export interface CardSummary {
  id: string;
  name: string;
  number: string;
  rarity: string | null;
  imageUrl: string | null;
  setId: string;
  setName: string;
  marketPrice: number | null;
  priceVariant: string | null;
  tcgplayerUrl: string | null;
}

export interface SetDetail {
  set: SetSummary;
  stats: {
    cardCount: number;
    pricedCount: number;
    totalMarketValue: number;
    mostValuable: CardSummary | null;
  };
  cards: CardSummary[];
}

export interface VariantPrice {
  variant: string;
  label: string;
  low: number | null;
  mid: number | null;
  high: number | null;
  market: number | null;
}

export interface PricePoint {
  date: string;
  variant: string;
  market: number | null;
}

export interface CardDetail {
  id: string;
  name: string;
  supertype: string | null;
  subtypes: string[];
  hp: string | null;
  types: string[];
  number: string;
  artist: string | null;
  rarity: string | null;
  flavorText: string | null;
  imageUrl: string | null;
  largeImageUrl: string | null;
  set: SetSummary;
  prices: VariantPrice[];
  pricesUpdated: string | null;
  tcgplayerUrl: string | null;
  history: PricePoint[];
}

export interface SearchResults {
  cards: CardSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PriceMove {
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  tcgplayerUrl: string | null;
  variant: string;
  variantLabel: string;
  from: number;
  to: number;
  changePercent: number;
}

export interface MarketMovers {
  from: string | null;
  to: string | null;
  gainers: PriceMove[];
  losers: PriceMove[];
}

export interface ValuableCard {
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  tcgplayerUrl: string | null;
  variantLabel: string;
  market: number;
}

export interface MarketStatus {
  lastSnapshotAt: string | null;
  cardsSeen: number | null;
  pricesWritten: number | null;
  daysOfHistory: number;
}

export interface TrendPoint {
  date: string;
  market: number;
}

export interface TrendingCard {
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  tcgplayerUrl: string | null;
  variant: string;
  variantLabel: string;
  from: number;
  to: number;
  changePercent: number;
  fit: number;
  points: TrendPoint[];
}

export interface DownTrend {
  from: string | null;
  to: string | null;
  days: number;
  daysOfHistory: number;
  cards: TrendingCard[];
}

export interface SleeperCard {
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  tcgplayerUrl: string | null;
  variant: string;
  variantLabel: string;
  market: number;
  low: number;
  mid: number | null;
  listingGapPercent: number;
  change30Percent: number | null;
}

export interface Sleepers {
  asOf: string | null;
  cards: SleeperCard[];
}

export type PredictionStatus = 'Running' | 'Published' | 'Withheld' | 'InsufficientHistory' | 'Failed';

export interface PredictionReason {
  feature: string;
  text: string;
  effectPercent: number;
}

export interface CardPrediction {
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  tcgplayerUrl: string | null;
  variant: string;
  variantLabel: string;
  current: number;
  predicted: number;
  low: number;
  high: number;
  changePercent: number;
  reasons: PredictionReason[];
}

export interface PredictionList {
  status: PredictionStatus;
  asOf: string | null;
  horizonDays: number;
  cards: CardPrediction[];
}

export interface FeatureWeight {
  feature: string;
  label: string;
  weight: number;
}

export interface RealizedAccuracy {
  asOf: string;
  count: number;
  typicalErrorPercent: number;
  noChangeErrorPercent: number;
  directionAccuracyPercent: number | null;
}

export interface ModelSummary {
  status: PredictionStatus;
  asOf: string | null;
  trainedAt: string | null;
  horizonDays: number;
  trainingRows: number;
  validationRows: number;
  typicalErrorPercent: number | null;
  noChangeErrorPercent: number | null;
  directionAccuracyPercent: number | null;
  rangeLowPercent: number | null;
  rangeHighPercent: number | null;
  importance: FeatureWeight[];
  trackRecord: RealizedAccuracy[];
  message: string | null;
}
