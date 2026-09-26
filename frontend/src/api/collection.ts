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
 * can be saved with an email later. The new account is only put in the cache once the write that needed it has
 * finished: switching to "signed in" earlier starts loading the collection while that write is still in flight,
 * and the page would show the collection from before it.
 */
async function withSession<T>(client: ReturnType<typeof useQueryClient>, write: () => Promise<T>): Promise<T> {
  const account = client.getQueryData<AccountView>(['account']);
  const started = account?.signedIn ? null : await api.startGuest();
  try {
    return await write();
  } finally {
    if (started) client.setQueryData(['account'], started);
  }
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
      mutationFn: (request: AddItemRequest) => withSession(client, () => api.addItem(request)),
      ...after,
    }),
    update: useMutation({
      mutationFn: ({ id, request }: { id: string; request: UpdateItemRequest }) => api.updateItem(id, request),
      ...after,
    }),
    remove: useMutation({ mutationFn: (id: string) => api.removeItem(id), ...after }),
    sample: useMutation({
      mutationFn: () => withSession(client, () => api.addSample()),
      ...after,
    }),
    wish: useMutation({
      mutationFn: ({ cardId, variant, targetPrice }: { cardId: string; variant: string | null; targetPrice: number | null }) =>
        withSession(client, () => api.wish(cardId, variant, targetPrice)),
      ...after,
    }),
    unwish: useMutation({ mutationFn: (id: string) => api.unwish(id), ...after }),
    setGoal: useMutation({
      mutationFn: ({ setId, kind }: { setId: string; kind: SetGoalKind }) => withSession(client, () => api.setGoal(setId, kind)),
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
