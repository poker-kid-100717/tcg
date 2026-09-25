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
