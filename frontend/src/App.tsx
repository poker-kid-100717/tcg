import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';

import { Layout } from './components/Layout';
import { Loading, NotFound } from './components/States';

// Each page is its own chunk, so the first load only fetches the page shown.
const HomePage = lazy(() => import('./pages/HomePage'));
const SetsPage = lazy(() => import('./pages/SetsPage'));
const SetPage = lazy(() => import('./pages/SetPage'));
const CardPage = lazy(() => import('./pages/CardPage'));
const SearchPage = lazy(() => import('./pages/SearchPage'));
const MarketPage = lazy(() => import('./pages/MarketPage'));
const AboutPage = lazy(() => import('./pages/AboutPage'));
const OutlookPage = lazy(() => import('./pages/OutlookPage'));
const DealAnalyzerPage = lazy(() => import('./pages/DealAnalyzerPage'));
const WatchlistPage = lazy(() => import('./pages/WatchlistPage'));
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const ProPage = lazy(() => import('./pages/ProPage'));

export default function App() {
  return (
    <BrowserRouter>
      <Suspense fallback={<Loading />}>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<HomePage />} />
            <Route path="sets" element={<SetsPage />} />
            <Route path="sets/:setId" element={<SetPage />} />
            <Route path="cards/:cardId" element={<CardPage />} />
            <Route path="search" element={<SearchPage />} />
            <Route path="market" element={<MarketPage />} />
            <Route path="outlook" element={<OutlookPage />} />
            <Route path="deal" element={<DealAnalyzerPage />} />
            <Route path="watchlist" element={<WatchlistPage />} />
            <Route path="dashboard" element={<DashboardPage />} />
            <Route path="pro" element={<ProPage />} />
            <Route path="about" element={<AboutPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}
