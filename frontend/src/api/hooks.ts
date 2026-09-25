import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { api, ApiError } from './client';

// Card data changes at most daily, so queries stay fresh for a while and
// navigating back and forth doesn't refetch.
const minutes = (n: number) => n * 60_000;

export const useSets = () =>
  useQuery({ queryKey: ['sets'], queryFn: ({ signal }) => api.sets(signal), staleTime: minutes(60) });

export const useSet = (id: string) =>
  useQuery({ queryKey: ['set', id], queryFn: ({ signal }) => api.set(id, signal), staleTime: minutes(30) });

export const useCard = (id: string) =>
  useQuery({ queryKey: ['card', id], queryFn: ({ signal }) => api.card(id, signal), staleTime: minutes(30) });

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
