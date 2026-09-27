import type {
  AuthOptions,
  CardHistory,
  DealAnalysis,
  DealInput,
  FeePreset,
  Me,
  Plan,
  SignalCenter,
  Subscription,
  AlertEvent,
  BillingLink,
  CardDetail,
  Dashboard,
  DownTrend,
  MarketMovers,
  MarketIntelligence,
  MarketStatus,
  MasterSetAiAdvice,
  MasterSetDetail,
  MasterSetItemUpdate,
  MasterSetSummary,
  ModelSummary,
  NearbyInventory,
  PredictionList,
  SearchResults,
  SetDetail,
  SetSummary,
  Sleepers,
  ValuableCard,
  WatchlistInput,
  WatchlistItem,
  WatchlistUpdate,
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
    /** The ProblemDetails type: "sign_in_required" (401) or "pro_required" (403) tell the UI what to offer. */
    public readonly type: string | null = null,
  ) {
    super(message);
  }

  get needsSignIn() {
    return this.status === 401;
  }

  get needsPro() {
    return this.status === 403 && this.type === 'pro_required';
  }
}

async function request<T>(path: string, init: RequestInit = {}, signal?: AbortSignal): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('accept', 'application/json');
  // Marks the request as the app's own (the API refuses state-changing requests without it or a JSON body).
  headers.set('x-requested-with', 'fetch');
  if (init.body && !headers.has('content-type')) headers.set('content-type', 'application/json');
  const response = await fetch(`${BASE}${path}`, { ...init, headers, signal, credentials: 'same-origin' });
  if (!response.ok) {
    let message = `The request failed (${response.status}).`;
    let type: string | null = null;
    try {
      const problem = await response.json();
      message = problem.detail ?? problem.title ?? message;
      type = typeof problem.type === 'string' ? problem.type : null;
    } catch {
      // Not a JSON problem response; keep the generic message.
    }
    if (response.status === 401) type ??= 'sign_in_required';
    throw new ApiError(response.status, message, type);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

const get = <T>(path: string, signal?: AbortSignal) => request<T>(path, {}, signal);
const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });
const put = <T>(path: string, body: unknown) =>
  request<T>(path, { method: 'PUT', body: JSON.stringify(body) });
const del = <T>(path: string) => request<T>(path, { method: 'DELETE' });

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
  nearbyInventory: (lat: number, lng: number, radius: number, signal?: AbortSignal) =>
    request<NearbyInventory>(
      '/inventory/nearby',
      {
        method: 'POST',
        body: JSON.stringify({ latitude: lat, longitude: lng, radiusMiles: radius }),
      },
      signal,
    ),

  me: (signal?: AbortSignal) => get<Me>('/me', signal),
  authOptions: (signal?: AbortSignal) => get<AuthOptions>('/auth/options', signal),
  localLogin: (email: string) => post<void>('/auth/local-login', { email }),
  logout: () => post<void>('/auth/logout'),
  deleteAccount: () => del<void>('/account'),
  plans: (signal?: AbortSignal) => get<Plan[]>('/billing/plans', signal),
  subscription: (signal?: AbortSignal) => get<Subscription>('/billing/subscription', signal),
  signals: (kind: string, minConfidence: number, signal?: AbortSignal) =>
    get<SignalCenter>(`/signals?kind=${enc(kind)}&minConfidence=${minConfidence}&limit=100`, signal),
  dealPresets: (signal?: AbortSignal) => get<FeePreset[]>('/deals/presets', signal),
  analyzeDeal: (input: DealInput) => post<DealAnalysis>('/deals/analyze', input),
  history: (id: string, days: number, signal?: AbortSignal) => get<CardHistory>(`/cards/${enc(id)}/history?days=${days}`, signal),
  intelligence: (id: string, variant: string, signal?: AbortSignal) =>
    get<MarketIntelligence>(`/cards/${enc(id)}/intelligence?variant=${enc(variant)}`, signal),
  watchlist: (signal?: AbortSignal) => get<WatchlistItem[]>('/watchlist', signal),
  addWatch: (input: WatchlistInput) => post<{ id: number }>('/watchlist', input),
  updateWatch: (id: number, input: WatchlistUpdate) => put<void>(`/watchlist/${id}`, input),
  deleteWatch: (id: number) => del<void>(`/watchlist/${id}`),
  alerts: (signal?: AbortSignal) => get<AlertEvent[]>('/alerts', signal),
  readAlert: (id: number) => post<void>(`/alerts/${id}/read`),
  readAllAlerts: () => post<void>('/alerts/read-all'),
  dashboard: (signal?: AbortSignal) => get<Dashboard>('/dashboard', signal),
  masterSets: (signal?: AbortSignal) => get<MasterSetSummary[]>('/master-sets', signal),
  createMasterSet: (setId: string) => post<{ id: number }>('/master-sets', { setId }),
  masterSet: (id: number, signal?: AbortSignal) => get<MasterSetDetail>(`/master-sets/${id}`, signal),
  updateMasterSetItem: (id: number, input: MasterSetItemUpdate) => put<void>(`/master-sets/${id}/items`, input),
  deleteMasterSet: (id: number) => del<void>(`/master-sets/${id}`),
  masterSetAdvisor: (id: number) => post<MasterSetAiAdvice>(`/master-sets/${id}/advisor`),
  checkout: (plan: 'monthly' | 'annual' | 'storefinder' | 'complete') => post<BillingLink>('/billing/checkout', { plan }),
  billingPortal: () => post<BillingLink>('/billing/portal'),
};
