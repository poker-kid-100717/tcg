import React from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import Header from './components/Header';
import Footer from './components/Footer';
import HomePage from './pages/HomePage';
import ShopPage from './pages/ShopPage';
import CardDetailPage from './pages/CardDetailPage';
import SetsPage from './pages/SetsPage';
import SetDetailPage from './pages/SetDetailPage';
import SearchPage from './pages/SearchPage';
import CartPage from './pages/CartPage';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import CheckoutPage from './pages/CheckoutPage';
import OrdersPage from './pages/OrdersPage';
import WishlistPage from './pages/WishlistPage';

function App() {
  return (
    <Router>
      <div className="flex flex-col min-h-screen">
        <Header />
        <main className="flex-grow">
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/shop" element={<ShopPage />} />
            <Route path="/cards/:cardId" element={<CardDetailPage />} />
            <Route path="/sets" element={<SetsPage />} />
            <Route path="/sets/:setId" element={<SetDetailPage />} />
            <Route path="/search" element={<SearchPage />} />
            <Route path="/cart" element={<CartPage />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />
            
            {/* Real, backend-backed routes */}
            <Route path="/checkout" element={<CheckoutPage />} />
            <Route path="/orders" element={<OrdersPage />} />
            <Route path="/wishlist" element={<WishlistPage />} />

            {/* Placeholder routes */}
            <Route path="/account" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Account Page</h1><p className="mt-4">This is a placeholder for the account page.</p></div>} />
            <Route path="/special-deals" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Special Deals</h1><p className="mt-4">This is a placeholder for the special deals page.</p></div>} />
            <Route path="/bulk-cards" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Bulk Cards</h1><p className="mt-4">This is a placeholder for the bulk cards page.</p></div>} />
            <Route path="/price-guide" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Price Guide</h1><p className="mt-4">This is a placeholder for the price guide page.</p></div>} />
            <Route path="/card-grading" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Card Grading</h1><p className="mt-4">This is a placeholder for the card grading page.</p></div>} />
            <Route path="/blog" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Blog</h1><p className="mt-4">This is a placeholder for the blog page.</p></div>} />
            <Route path="/faq" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">FAQ</h1><p className="mt-4">This is a placeholder for the FAQ page.</p></div>} />
            <Route path="/api" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Developer API</h1><p className="mt-4">This is a placeholder for the developer API documentation page.</p></div>} />
            <Route path="/about" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">About Us</h1><p className="mt-4">This is a placeholder for the about us page.</p></div>} />
            <Route path="/contact" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Contact</h1><p className="mt-4">This is a placeholder for the contact page.</p></div>} />
            <Route path="/careers" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Careers</h1><p className="mt-4">This is a placeholder for the careers page.</p></div>} />
            <Route path="/privacy" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Privacy Policy</h1><p className="mt-4">This is a placeholder for the privacy policy page.</p></div>} />
            <Route path="/terms" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Terms of Service</h1><p className="mt-4">This is a placeholder for the terms of service page.</p></div>} />
            <Route path="/pre-orders" element={<div className="container-custom py-12"><h1 className="text-3xl font-bold">Pre-orders</h1><p className="mt-4">This is a placeholder for the pre-orders page.</p></div>} />
            
            {/* 404 catch-all route */}
            <Route path="*" element={
              <div className="container-custom py-12 text-center">
                <h1 className="text-3xl font-bold mb-4">Page Not Found</h1>
                <p className="mb-6">The page you're looking for doesn't exist or has been moved.</p>
                <a href="/" className="btn-primary">Go Home</a>
              </div>
            } />
          </Routes>
        </main>
        <Footer />
      </div>
    </Router>
  );
}

export default App;