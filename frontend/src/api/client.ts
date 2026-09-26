import type {
  AccountView,
  AddItemRequest,
  CardDetail,
  CardOwnership,
  CollectionView,
  SetChecklist,
  UpdateItemRequest,
  WishlistEntry,
  DownTrend,
  MarketMovers,
  MarketStatus,
  ModelSummary,
  PredictionList,
  SearchResults,
  SetDetail,
  SetSummary,
  Sleepers,
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
  return read<T>(response);
}

/**
 * State-changing calls. Always JSON with X-Requested-With: the API refuses anything else, which is what stops
 * another site from posting a form at a signed-in collector's account.
 */
async function send<T>(method: 'POST' | 'PATCH' | 'DELETE', path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${BASE}${path}`, {
    method,
    headers: { accept: 'application/json', 'content-type': 'application/json', 'x-requested-with': 'fetch' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return read<T>(response);
}

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let message = `The request failed (${response.status}).`;
    try {
      const problem = await response.json();
      // Validation problems carry the useful text per field ("Use at least 10 characters.").
      const fieldError = problem.errors ? (Object.values(problem.errors).flat()[0] as string | undefined) : undefined;
      message = fieldError ?? problem.detail ?? problem.title ?? message;
    } catch {
      // Not a JSON problem response; keep the generic message.
    }
    throw new ApiError(response.status, message);
  }
  if (response.status === 204) return undefined as T;
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
  downtrend: (signal?: AbortSignal) => get<DownTrend>('/market/downtrend?days=30&limit=12', signal),
  sleepers: (signal?: AbortSignal) => get<Sleepers>('/market/sleepers?limit=12', signal),
  predictions: (direction: 'up' | 'down', signal?: AbortSignal) =>
    get<PredictionList>(`/predictions?direction=${direction}&limit=12`, signal),
  cardPredictions: (id: string, signal?: AbortSignal) => get<PredictionList>(`/cards/${enc(id)}/predictions`, signal),
  model: (signal?: AbortSignal) => get<ModelSummary>('/predictions/model', signal),

  account: (signal?: AbortSignal) => get<AccountView>('/account', signal),
  startGuest: () => send<AccountView>('POST', '/account/guest'),
  register: (email: string, password: string) => send<AccountView>('POST', '/account/register', { email, password }),
  login: (email: string, password: string) => send<AccountView>('POST', '/account/login', { email, password }),
  logout: () => send<AccountView>('POST', '/account/logout'),
  deleteAccount: () => send<void>('DELETE', '/account'),

  collection: (signal?: AbortSignal) => get<CollectionView>('/collection', signal),
  ownership: (cardId: string, signal?: AbortSignal) => get<CardOwnership>(`/collection/cards/${enc(cardId)}`, signal),
  checklist: (setId: string, signal?: AbortSignal) => get<SetChecklist>(`/collection/sets/${enc(setId)}`, signal),
  addItem: (request: AddItemRequest) => send<CardOwnership>('POST', '/collection/items', request),
  updateItem: (id: string, request: UpdateItemRequest) => send<CardOwnership>('PATCH', `/collection/items/${enc(id)}`, request),
  removeItem: (id: string) => send<void>('DELETE', `/collection/items/${enc(id)}`),
  addSample: () => send<{ added: number }>('POST', '/collection/sample'),
  exportUrl: `${BASE}/collection/export.csv`,

  wishlist: (signal?: AbortSignal) => get<WishlistEntry[]>('/wishlist', signal),
  wish: (cardId: string, variant: string | null, targetPrice: number | null) =>
    send<WishlistEntry>('POST', '/wishlist', { cardId, variant, targetPrice }),
  unwish: (id: string) => send<void>('DELETE', `/wishlist/${enc(id)}`),
};
