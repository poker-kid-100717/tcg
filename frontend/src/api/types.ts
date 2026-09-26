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
  /** Where prices come from: the TCGplayer API directly, or the Pokémon TCG API's daily copy of TCGplayer's. */
  priceSource?: string;
  tcgplayerApi?: boolean;
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

// Collections (backend/Collections/CollectionContracts.cs).

export type CardCondition = 'NearMint' | 'LightlyPlayed' | 'ModeratelyPlayed' | 'HeavilyPlayed' | 'Damaged';

export interface AccountView {
  signedIn: boolean;
  isGuest: boolean;
  email: string | null;
}

export interface PriceConfidence {
  level: 'High' | 'Medium' | 'Low';
  reasons: string[];
  daysSinceChange: number | null;
}

export interface CollectionEntry {
  id: string;
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  rarity: string | null;
  variant: string;
  variantLabel: string;
  condition: CardCondition;
  quantity: number;
  costEach: number | null;
  acquiredOn: string | null;
  notes: string | null;
  marketEach: number | null;
  valueEach: number | null;
  netEach: number | null;
  total: number | null;
  gainPercent: number | null;
  confidence: PriceConfidence;
  tcgplayerUrl: string | null;
  addedAt: string;
}

export interface CollectionSummary {
  cards: number;
  unique: number;
  marketValue: number;
  conditionValue: number;
  netIfSold: number;
  costBasis: number | null;
  gain: number | null;
  gainPercent: number | null;
  highConfidenceShare: number;
  change30Percent: number | null;
  pricesAsOf: string | null;
}

export interface ValuePoint {
  date: string;
  value: number;
}

export interface SetProgress {
  setId: string;
  setName: string;
  symbolUrl: string | null;
  releaseDate: string | null;
  owned: number;
  printedTotal: number;
  ownedAll: number;
  total: number;
  completion: number;
  costToComplete: number | null;
  unpricedMissing: number;
}

export interface CollectionSignal {
  cardId: string;
  name: string;
  setName: string;
  imageUrl: string | null;
  variant: string;
  variantLabel: string;
  kind: 'ConsiderSelling' | 'Watch' | 'Hold';
  reasons: string[];
  valueEach: number | null;
  quantity: number;
}

export interface CollectionView {
  summary: CollectionSummary;
  items: CollectionEntry[];
  history: ValuePoint[];
  sets: SetProgress[];
  signals: CollectionSignal[];
  fees: { commissionRate: number; paymentRate: number; perSaleFee: number };
  conditionFactors: Record<CardCondition, number>;
}

export interface MissingCard {
  cardId: string;
  name: string;
  number: string;
  imageUrl: string | null;
  secret: boolean;
  variant: string | null;
  price: number | null;
  low: number | null;
  timing: string | null;
  timingReason: string | null;
  tcgplayerUrl: string | null;
}

export interface SetChecklist {
  setId: string;
  printedTotal: number;
  total: number;
  owned: { cardId: string; quantity: number; variants: string[] }[];
  missing: MissingCard[];
  costToCompleteBase: number;
  costToCompleteAll: number;
}

export interface WishlistEntry {
  id: string;
  cardId: string;
  name: string;
  number: string;
  setId: string;
  setName: string;
  imageUrl: string | null;
  variant: string | null;
  variantLabel: string;
  targetPrice: number | null;
  suggestedTarget: number | null;
  market: number | null;
  low: number | null;
  atOrBelowTarget: boolean;
  tcgplayerUrl: string | null;
  addedAt: string;
}

export interface CardOwnership {
  items: CollectionEntry[];
  wish: WishlistEntry | null;
}

export interface AddItemRequest {
  cardId: string;
  variant: string;
  condition?: CardCondition;
  quantity?: number;
  costEach?: number | null;
  acquiredOn?: string | null;
  notes?: string | null;
}

export interface UpdateItemRequest {
  quantity?: number;
  condition?: CardCondition;
  costEach?: number;
  clearCost?: boolean;
  notes?: string;
}
