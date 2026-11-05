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

export type PredictionStatus = 'Running' | 'Published' | 'Preview' | 'Withheld' | 'InsufficientHistory' | 'Skipped' | 'Failed';

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
  historyDays: number;
  approxHistoryDaysNeeded: number;
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
  storeFinderBillingConfigured: boolean;
  isPro: boolean;
  hasStoreFinder: boolean;
  plan: string;
  subscriptionStatus: string | null;
  mode: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  paymentIssue: boolean;
  canManageBilling: boolean;
}

export interface Me {
  signedIn: boolean;
  email: string | null;
  displayName: string | null;
  account: Account | null;
}

export interface AuthOptions {
  signInAvailable: boolean;
  providerSignIn: boolean;
  localLogin: boolean;
  providerName: string;
}

export interface Plan {
  id: 'free' | 'monthly' | 'annual' | 'storefinder' | 'complete';
  name: string;
  price: number;
  currency: string;
  interval: string;
  available: boolean;
}

export interface Subscription {
  plan: string;
  status: string | null;
  isPro: boolean;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  paymentIssue: boolean;
  canManageBilling: boolean;
}

export interface DataFreshness {
  asOf: string | null;
  ageDays: number | null;
  state: 'Fresh' | 'Aging' | 'Stale' | 'NoData';
  label: string;
}

export interface SignalResult {
  kind: string;
  name: string;
  value: number;
  unit: string;
  lookbackDays: number;
  reason: string;
  asOf: string;
  confidence: number | null;
}

export interface SignalItem {
  cardId: string;
  variant: string;
  variantLabel: string;
  cardName: string;
  setName: string;
  imageUrl: string | null;
  market: number | null;
  kind: string;
  name: string;
  value: number;
  unit: string;
  lookbackDays: number;
  reason: string;
  confidence: number;
  source: string;
  asOf: string;
}

export interface SignalCenter {
  freshness: DataFreshness;
  counts: Record<string, number>;
  rules: Record<string, string>;
  signals: SignalItem[];
}

export interface WatchlistMover {
  watchlistItemId: number;
  cardId: string;
  variant: string;
  cardName: string;
  market: number | null;
  change7Percent: number | null;
  change30Percent: number | null;
}

export interface MarketSummary {
  freshness: DataFreshness;
  printingsPriced: number;
  up7: number;
  down7: number;
  signalCounts: Record<string, number>;
}

export interface FeePreset {
  id: string;
  name: string;
  percentFee: number;
  fixedFee: number;
  note: string;
}

export interface DealInput {
  cardId: string;
  variant: string;
  askingPrice: number;
  inboundShipping: number | null;
  tax: number | null;
  outboundShipping: number | null;
  presetId: string;
  customPercentFee: number | null;
  customFixedFee: number | null;
  expectedSalePrice: number | null;
}

export interface DealAnalysis {
  cardId: string;
  cardName: string;
  variant: string;
  variantLabel: string;
  marketReference: number | null;
  referenceSource: string;
  freshness: DataFreshness;
  confidenceScore: number | null;
  confidenceBand: string;
  askingPrice: number;
  askVsMarketPercent: number | null;
  position: string;
  fees: FeePreset;
  math: {
    acquisition: number;
    expectedSale: number;
    sellingFees: number;
    outboundShipping: number;
    proceeds: number;
    net: number;
    netMarginPercent: number | null;
    breakEvenSalePrice: number | null;
  };
  warnings: string[];
  summary: string;
}

export interface CardHistory {
  id: string;
  historyDays: number;
  history: { date: string; variant: string; market: number | null }[];
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
  confidenceScore: number | null;
  confidenceBand: string;
  confidenceExplanation: string;
  confidenceFactors: { name: string; penalty: number; detail: string }[];
  referenceProvider: string;
  freshness: DataFreshness | null;
  volatilityLevel: string;
  change90Percent: number | null;
  belowHigh30Percent: number | null;
  lowVsMarketPercent: number | null;
  high30: number | null;
  low30: number | null;
  verifiedComps: {
    available: boolean;
    source: string | null;
    explanation: string;
    sales30: number | null;
    medianSold30: number | null;
    lastSalePrice: number | null;
    lastSaleAt: string | null;
    medianDaysBetweenSales: number | null;
  };
  liquidityDetail: {
    status: string;
    explanation: string;
    sales7: number | null;
    sales30: number | null;
    sales90: number | null;
    medianDaysBetweenSales: number | null;
    lastSaleAt: string | null;
  } | null;
  signals: SignalResult[];
  whyMoving: string[];
  history: { date: string; market: number; low: number | null }[];
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
  alertSleeper: boolean;
  alertTrendingDown: boolean;
  alertUnusualMove: boolean;
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
  alertSleeper?: boolean;
  alertTrendingDown?: boolean;
  alertUnusualMove?: boolean;
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
  movers: WatchlistMover[];
  watchedSignals: SignalItem[];
  signalsLocked: boolean;
  targetsHit7Days: number;
  market: MarketSummary | null;
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


export interface InventoryListing {
  retailer: string;
  storeId: string;
  storeName: string;
  address: string;
  city: string;
  region: string;
  postalCode: string;
  latitude: number;
  longitude: number;
  distanceMiles: number;
  productSku: string;
  productName: string;
  category: string;
  imageUrl: string | null;
  productUrl: string | null;
  price: number | null;
  lowStock: boolean;
  availability: string;
  confidenceScore: number;
  observedAt: string;
  source: string;
  evidence: string;
}

export interface InventoryProviderCoverage {
  retailer: string;
  status: string;
  detail: string;
  checkedAt: string | null;
}

export interface NearbyInventory {
  checkedAt: string;
  radiusMiles: number;
  listings: InventoryListing[];
  providers: InventoryProviderCoverage[];
  accuracyPolicy: string;
}
