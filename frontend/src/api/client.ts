import type {
  CardDetail,
  MarketMovers,
  MarketStatus,
  SearchResults,
  SetDetail,
  SetSummary,
  ValuableCard,
} from './types';

/**
 * The price guide API lives on the same origin: the Cloudflare Worker routes
 * /api to the .NET container in production, and Vite proxies it in dev.
 */
const BASE = import.meta.env.VITE_API_URL ?? '/api';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${BASE}${path}`, { headers: { accept: 'application/json' }, signal });
  if (!response.ok) {
    let message = `The request failed (${response.status}).`;
    try {
      const problem = await response.json();
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Not a JSON problem response; keep the generic message.
    }
    throw new ApiError(response.status, message);
  }
  return response.json() as Promise<T>;
}

const enc = encodeURIComponent;

export const api = {
  sets: (signal?: AbortSignal) => get<SetSummary[]>('/sets', signal),
  set: (id: string, signal?: AbortSignal) => get<SetDetail>(`/sets/${enc(id)}`, signal),
  card: (id: string, signal?: AbortSignal) => get<CardDetail>(`/cards/${enc(id)}`, signal),
  search: (q: string, page: number, signal?: AbortSignal) =>
    get<SearchResults>(`/cards?q=${enc(q)}&page=${page}&pageSize=24`, signal),
  movers: (days: number, signal?: AbortSignal) => get<MarketMovers>(`/market/movers?days=${days}&limit=8`, signal),
  top: (signal?: AbortSignal) => get<ValuableCard[]>('/market/top?limit=12', signal),
  status: (signal?: AbortSignal) => get<MarketStatus>('/market/status', signal),
};
