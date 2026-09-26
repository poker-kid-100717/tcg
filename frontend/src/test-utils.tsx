import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';

/**
 * Answers fetch('/api/…') from a map of path → JSON body (or a status code). Keys may start with a method
 * ("POST /api/collection/items") to answer only that method; a bare path answers GET.
 */
export function mockApi(routes: Record<string, unknown>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost');
    const key = url.pathname + url.search;
    const method = (init?.method ?? 'GET').toUpperCase();
    const body =
      routes[`${method} ${key}`] ?? routes[`${method} ${url.pathname}`] ?? (method === 'GET' ? (routes[key] ?? routes[url.pathname]) : undefined);
    if (body === 204) return new Response(null, { status: 204 });
    if (body === undefined) return new Response(JSON.stringify({ title: 'Not found' }), { status: 404 });
    if (typeof body === 'number') return new Response(JSON.stringify({ title: 'Failed', detail: 'Upstream is down' }), { status: body });
    return new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

export function renderRoute(path: string, pattern: string, element: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path={pattern} element={element} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
