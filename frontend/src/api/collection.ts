import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, ApiError } from './client';
import type { AccountView, AddItemRequest, SetGoalKind, UpdateItemRequest } from './types';

const SIGNED_OUT: AccountView = { signedIn: false, isGuest: false, email: null };

/** Who's here. A failed lookup reads as signed out, so browsing never breaks on it. */
export const useAccount = () =>
  useQuery({
    queryKey: ['account'],
    queryFn: async ({ signal }) => {
      try {
        return await api.account(signal);
      } catch (error) {
        if (error instanceof ApiError) return SIGNED_OUT;
        throw error;
      }
    },
    staleTime: Infinity,
  });

const signedIn = (account: AccountView | undefined) => !!account?.signedIn;

export const useCollection = () => {
  const account = useAccount();
  return useQuery({
    queryKey: ['collection'],
    queryFn: ({ signal }) => api.collection(signal),
    enabled: signedIn(account.data),
  });
};

export const useOwnership = (cardId: string) => {
  const account = useAccount();
  return useQuery({
    queryKey: ['ownership', cardId],
    queryFn: ({ signal }) => api.ownership(cardId, signal),
    enabled: signedIn(account.data) && cardId.length > 0,
  });
};

export const useChecklist = (setId: string) => {
  const account = useAccount();
  return useQuery({
    queryKey: ['checklist', setId],
    queryFn: ({ signal }) => api.checklist(setId, signal),
    enabled: signedIn(account.data) && setId.length > 0,
  });
};

export const useWishlist = () => {
  const account = useAccount();
  return useQuery({
    queryKey: ['wishlist'],
    queryFn: ({ signal }) => api.wishlist(signal),
    enabled: signedIn(account.data),
  });
};

/**
 * Collecting starts without a sign-up: the first add quietly creates a guest account (kept by a cookie), which
 * can be saved with an email later.
 */
async function ensureSession(client: ReturnType<typeof useQueryClient>) {
  const account = client.getQueryData<AccountView>(['account']);
  if (account?.signedIn) return;
  client.setQueryData(['account'], await api.startGuest());
}

/** Everything that depends on what the collector owns. */
function refreshCollection(client: ReturnType<typeof useQueryClient>) {
  for (const key of ['collection', 'ownership', 'checklist', 'wishlist']) client.invalidateQueries({ queryKey: [key] });
}

export function useCollectionActions() {
  const client = useQueryClient();
  const after = { onSettled: () => refreshCollection(client) };
  return {
    add: useMutation({
      mutationFn: async (request: AddItemRequest) => {
        await ensureSession(client);
        return api.addItem(request);
      },
      ...after,
    }),
    update: useMutation({
      mutationFn: ({ id, request }: { id: string; request: UpdateItemRequest }) => api.updateItem(id, request),
      ...after,
    }),
    remove: useMutation({ mutationFn: (id: string) => api.removeItem(id), ...after }),
    sample: useMutation({
      mutationFn: async () => {
        await ensureSession(client);
        return api.addSample();
      },
      ...after,
    }),
    wish: useMutation({
      mutationFn: async ({ cardId, variant, targetPrice }: { cardId: string; variant: string | null; targetPrice: number | null }) => {
        await ensureSession(client);
        return api.wish(cardId, variant, targetPrice);
      },
      ...after,
    }),
    unwish: useMutation({ mutationFn: (id: string) => api.unwish(id), ...after }),
    setGoal: useMutation({
      mutationFn: async ({ setId, kind }: { setId: string; kind: SetGoalKind }) => {
        await ensureSession(client);
        return api.setGoal(setId, kind);
      },
      ...after,
    }),
    removeGoal: useMutation({ mutationFn: (setId: string) => api.removeGoal(setId), ...after }),
  };
}

export function useAccountActions() {
  const client = useQueryClient();
  // A different person (or none) now: drop everything cached about the last one.
  const switched = (account: AccountView | void) => {
    client.setQueryData(['account'], account ?? SIGNED_OUT);
    for (const key of ['collection', 'ownership', 'checklist', 'wishlist']) client.removeQueries({ queryKey: [key] });
  };
  return {
    register: useMutation({
      mutationFn: ({ email, password }: { email: string; password: string }) => api.register(email, password),
      onSuccess: (account) => client.setQueryData(['account'], account),
    }),
    login: useMutation({
      mutationFn: ({ email, password }: { email: string; password: string }) => api.login(email, password),
      onSuccess: switched,
    }),
    logout: useMutation({ mutationFn: api.logout, onSuccess: switched }),
    remove: useMutation({ mutationFn: api.deleteAccount, onSuccess: () => switched(SIGNED_OUT) }),
  };
}
