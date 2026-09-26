import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';

import { Layout } from './components/Layout';
import { Loading, NotFound } from './components/States';

// Each page is its own chunk, so the first load only fetches the page shown.
const CollectionPage = lazy(() => import('./pages/CollectionPage'));
const WishlistPage = lazy(() => import('./pages/WishlistPage'));
const AccountPage = lazy(() => import('./pages/AccountPage'));
const SetsPage = lazy(() => import('./pages/SetsPage'));
const SetPage = lazy(() => import('./pages/SetPage'));
const CardPage = lazy(() => import('./pages/CardPage'));
const SearchPage = lazy(() => import('./pages/SearchPage'));
const MarketPage = lazy(() => import('./pages/MarketPage'));
const AboutPage = lazy(() => import('./pages/AboutPage'));
const OutlookPage = lazy(() => import('./pages/OutlookPage'));

export default function App() {
  return (
    <BrowserRouter>
      <Suspense fallback={<Loading />}>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<CollectionPage />} />
            <Route path="wishlist" element={<WishlistPage />} />
            <Route path="account" element={<AccountPage />} />
            <Route path="sets" element={<SetsPage />} />
            <Route path="sets/:setId" element={<SetPage />} />
            <Route path="cards/:cardId" element={<CardPage />} />
            <Route path="search" element={<SearchPage />} />
            <Route path="market" element={<MarketPage />} />
            <Route path="outlook" element={<OutlookPage />} />
            <Route path="about" element={<AboutPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}
