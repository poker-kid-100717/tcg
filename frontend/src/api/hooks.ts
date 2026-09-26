import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, ApiError } from './client';

// Card data changes at most daily, so queries stay fresh for a while and
// navigating back and forth doesn't refetch.
const minutes = (n: number) => n * 60_000;

export const useSets = () =>
  useQuery({ queryKey: ['sets'], queryFn: ({ signal }) => api.sets(signal), staleTime: minutes(60) });

export const useSet = (id: string) =>
  useQuery({ queryKey: ['set', id], queryFn: ({ signal }) => api.set(id, signal), staleTime: minutes(30) });

export const useCard = (id: string) =>
  useQuery({
    queryKey: ['card', id],
    queryFn: ({ signal }) => api.card(id, signal),
    staleTime: minutes(30),
    enabled: id.length > 0,
  });

export const useSearch = (q: string, page: number) =>
  useQuery({
    queryKey: ['search', q, page],
    queryFn: ({ signal }) => api.search(q, page, signal),
    enabled: q.trim().length >= 2,
    placeholderData: keepPreviousData,
    staleTime: minutes(10),
  });

export const useMovers = (days: number) =>
  useQuery({ queryKey: ['movers', days], queryFn: ({ signal }) => api.movers(days, signal), staleTime: minutes(30) });

export const useTopCards = () =>
  useQuery({ queryKey: ['top'], queryFn: ({ signal }) => api.top(signal), staleTime: minutes(30) });

export const useMarketStatus = () =>
  useQuery({ queryKey: ['status'], queryFn: ({ signal }) => api.status(signal), staleTime: minutes(30) });

/** 404s are answers, not failures worth retrying. */
export const shouldRetry = (failureCount: number, error: unknown) =>
  !(error instanceof ApiError && error.status < 500) && failureCount < 2;

export const useDownTrend = () =>
  useQuery({ queryKey: ['downtrend'], queryFn: ({ signal }) => api.downtrend(signal), staleTime: minutes(30) });

export const useSleepers = () =>
  useQuery({ queryKey: ['sleepers'], queryFn: ({ signal }) => api.sleepers(signal), staleTime: minutes(30) });

export const usePredictions = (direction: 'up' | 'down') =>
  useQuery({
    queryKey: ['predictions', direction],
    queryFn: ({ signal }) => api.predictions(direction, signal),
    staleTime: minutes(30),
  });

export const useCardPredictions = (cardId: string) =>
  useQuery({
    queryKey: ['card-predictions', cardId],
    queryFn: ({ signal }) => api.cardPredictions(cardId, signal),
    staleTime: minutes(30),
    enabled: cardId.length > 0,
  });

export const useModelSummary = () =>
  useQuery({ queryKey: ['model'], queryFn: ({ signal }) => api.model(signal), staleTime: minutes(30) });


export const useSession = () =>
  useQuery({
    queryKey: ['session'],
    queryFn: () => api.session(),
    staleTime: Infinity,
    retry: 1,
  });

export const useIntelligence = (cardId: string, variant: string, enabled = true) =>
  useQuery({
    queryKey: ['intelligence', cardId, variant],
    queryFn: ({ signal }) => api.intelligence(cardId, variant, signal),
    enabled: enabled && cardId.length > 0 && variant.length > 0,
    staleTime: minutes(30),
    retry: shouldRetry,
  });

export const useWatchlist = () =>
  useQuery({
    queryKey: ['watchlist'],
    queryFn: ({ signal }) => api.watchlist(signal),
    staleTime: minutes(5),
  });

export const useAlerts = () =>
  useQuery({
    queryKey: ['alerts'],
    queryFn: ({ signal }) => api.alerts(signal),
    staleTime: minutes(2),
  });

export const useDashboard = () =>
  useQuery({
    queryKey: ['dashboard'],
    queryFn: ({ signal }) => api.dashboard(signal),
    staleTime: minutes(2),
  });

export const useAddWatch = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.addWatch,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['watchlist'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
    },
  });
};

export const useUpdateWatch = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number; input: import('./types').WatchlistUpdate }) => api.updateWatch(id, input),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['watchlist'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
    },
  });
};

export const useDeleteWatch = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.deleteWatch,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['watchlist'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
    },
  });
};

export const useReadAlert = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.readAlert,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['alerts'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
    },
  });
};

export const useReadAllAlerts = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.readAllAlerts,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['alerts'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
    },
  });
};


export const useMasterSets = () =>
  useQuery({
    queryKey: ['master-sets'],
    queryFn: ({ signal }) => api.masterSets(signal),
    staleTime: minutes(2),
  });

export const useMasterSet = (id: number) =>
  useQuery({
    queryKey: ['master-set', id],
    queryFn: ({ signal }) => api.masterSet(id, signal),
    enabled: id > 0,
    staleTime: minutes(2),
  });

export const useCreateMasterSet = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.createMasterSet,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['master-sets'] }),
  });
};

export const useUpdateMasterSetItem = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: number; input: import('./types').MasterSetItemUpdate }) => api.updateMasterSetItem(id, input),
    onSuccess: async (_data, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['master-set', variables.id] }),
        queryClient.invalidateQueries({ queryKey: ['master-sets'] }),
      ]);
    },
  });
};

export const useDeleteMasterSet = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.deleteMasterSet,
    onSuccess: async () => queryClient.invalidateQueries({ queryKey: ['master-sets'] }),
  });
};

export const useMasterSetAdvisor = () =>
  useMutation({ mutationFn: api.masterSetAdvisor });


export const useNearbyInventory = (
  latitude: number | null,
  longitude: number | null,
  radiusMiles: number,
) =>
  useQuery({
    queryKey: ['nearby-inventory', latitude, longitude, radiusMiles],
    queryFn: ({ signal }) => api.nearbyInventory(latitude!, longitude!, radiusMiles, signal),
    enabled: latitude !== null && longitude !== null,
    staleTime: minutes(2),
    refetchInterval: minutes(2),
    retry: shouldRetry,
  });
