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

export type PredictionStatus = 'Running' | 'Published' | 'Withheld' | 'InsufficientHistory' | 'Skipped' | 'Failed';

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
  horizonDays: number;
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


export interface Account {
  id: string;
  billingConfigured: boolean;
  isPro: boolean;
  plan: string;
  subscriptionStatus: string | null;
  mode: string;
}

export interface SoldComp {
  id: string;
  source: string;
  title: string | null;
  price: number;
  soldAt: string;
  url: string | null;
}

export interface MarketIntelligence {
  cardId: string;
  variant: string;
  variantLabel: string;
  source: string;
  asOf: string;
  market: number | null;
  low: number | null;
  mid: number | null;
  high: number | null;
  change7Percent: number | null;
  change30Percent: number | null;
  volatilityPercent: number | null;
  spreadPercent: number | null;
  observationDays: number;
  confidenceScore: number;
  confidenceBand: string;
  isStale: boolean;
  liquidity: string;
  liquidityReason: string;
  soldComps30Days: number | null;
  medianSold30Days: number | null;
  lastSoldPrice: number | null;
  lastSoldAt: string | null;
  recentSoldComps: SoldComp[];
  reasons: string[];
}

export interface WatchlistItem {
  id: number;
  cardId: string;
  variant: string;
  variantLabel: string;
  cardName: string;
  setName: string;
  imageUrl: string | null;
  baselinePrice: number | null;
  currentPrice: number | null;
  targetBelow: number | null;
  targetAbove: number | null;
  movePercent: number | null;
  enabled: boolean;
  createdAt: string;
}

export interface WatchlistInput {
  cardId: string;
  variant: string;
  cardName: string;
  setName: string;
  imageUrl: string | null;
  targetBelow: number | null;
  targetAbove: number | null;
  movePercent: number | null;
}

export interface WatchlistUpdate {
  targetBelow: number | null;
  targetAbove: number | null;
  movePercent: number | null;
  enabled: boolean;
}

export interface AlertEvent {
  id: number;
  watchlistItemId: number;
  kind: string;
  message: string;
  currentValue: number | null;
  createdAt: string;
  readAt: string | null;
}

export interface Dashboard {
  account: Account;
  watchlist: WatchlistItem[];
  alerts: AlertEvent[];
  unreadAlerts: number;
}

export interface BillingLink {
  url: string;
}


export interface MasterSetSummary {
  id: number;
  setId: string;
  setName: string;
  setSeries: string;
  logoUrl: string | null;
  uniqueCards: number;
  requiredPrintings: number;
  ownedPrintings: number;
  completionPercent: number;
  ownedMarketValue: number;
  missingMarketCost: number;
}

export interface MasterSetItem {
  cardId: string;
  variant: string;
  variantLabel: string;
  cardName: string;
  cardNumber: string;
  rarity: string | null;
  imageUrl: string | null;
  tcgplayerUrl: string | null;
  currentMarketPrice: number | null;
  change30Percent: number | null;
  ownedQuantity: number;
  condition: string | null;
  acquiredPrice: number | null;
  signal: string;
  signalReason: string;
}

export interface MasterSetDetail {
  summary: MasterSetSummary;
  items: MasterSetItem[];
}

export interface MasterSetItemUpdate {
  cardId: string;
  variant: string;
  ownedQuantity: number;
  condition: string | null;
  acquiredPrice: number | null;
}

export interface MasterSetAiAdvice {
  text: string;
  provider: string;
  model: string;
  generatedByAi: boolean;
  generatedAt: string;
}
