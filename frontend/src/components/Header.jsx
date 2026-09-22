import React, { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useLocalStorage } from '../utils/hooks';
import cartService from '../services/cartService';
import authService from '../services/authService';

export default function Header() {
  const location = useLocation();
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [isUserMenuOpen, setIsUserMenuOpen] = useState(false);
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [cart] = useLocalStorage('pokemonTCGCart', cartService.getCart());
  const [user] = useLocalStorage('pokemonTCGUser', null);

  const cartItemCount = cart?.items?.reduce((total, item) => total + item.quantity, 0) || 0;

  // Navigation links
  const navLinks = [
    { name: 'Home', path: '/' },
    { name: 'Shop', path: '/shop' },
    { name: 'Sets', path: '/sets' },
    { name: 'Card Database', path: '/cards' },
    { name: 'Architecture', path: '/architecture' },
  ];

  const handleSearch = (e) => {
    e.preventDefault();
    if (searchQuery.trim()) {
      // Use window.location.href to force a full page reload
      window.location.href = `/search?q=${encodeURIComponent(searchQuery)}`;
      setIsSearchOpen(false);
    }
  };

  const handleLogout = () => {
    authService.logout();
    window.location.href = '/';
  };

  return (
    <header className="bg-white shadow-md z-50 relative">
      {/* Top bar with logo and search */}
      <div className="container-custom py-3">
        <div className="flex justify-between items-center">
          {/* Logo */}
          <Link to="/" className="flex items-center space-x-2">
            <img 
              src="https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/items/poke-ball.png" 
              alt="Pokemon TCG Marketplace" 
              className="w-8 h-8"
            />
            <span className="text-xl font-bold font-heading text-pokemon-dark">Pokemon TCG Marketplace</span>
          </Link>

          {/* Desktop search, cart, and user */}
          <div className="hidden md:flex items-center space-x-4">
            {/* Search form */}
            <form onSubmit={handleSearch} className="relative">
              <input
                type="text"
                placeholder="Search for cards..."
                className="input w-64"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
              <button 
                type="submit"
                className="absolute right-2 top-1/2 transform -translate-y-1/2 text-gray-500 hover:text-gray-700"
              >
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5">
                  <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
                </svg>
              </button>
            </form>

            {/* Shopping cart */}
            <Link to="/cart" className="relative p-2 rounded-full hover:bg-gray-100">
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-6 h-6">
                <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 3h1.386c.51 0 .955.343 1.087.835l.383 1.437M7.5 14.25a3 3 0 0 0-3 3h15.75m-12.75-3h11.218c1.121-2.3 2.1-4.684 2.924-7.138a60.114 60.114 0 0 0-16.536-1.84M7.5 14.25 5.106 5.272M6 20.25a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Zm12.75 0a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Z" />
              </svg>
              {cartItemCount > 0 && (
                <span className="absolute -top-1 -right-1 bg-pokemon-red text-white text-xs w-5 h-5 flex items-center justify-center rounded-full">
                  {cartItemCount}
                </span>
              )}
            </Link>

            {/* User menu */}
            {user ? (
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setIsUserMenuOpen(!isUserMenuOpen)}
                  className="flex items-center space-x-2 p-2 rounded-full hover:bg-gray-100"
                >
                  <img
                    src={user.avatar}
                    alt={user.name}
                    className="w-8 h-8 rounded-full"
                  />
                  <span className="font-medium text-sm hidden xl:block">{user.name}</span>
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-4 h-4">
                    <path strokeLinecap="round" strokeLinejoin="round" d="m19.5 8.25-7.5 7.5-7.5-7.5" />
                  </svg>
                </button>

                {/* Dropdown menu */}
                {isUserMenuOpen && (
                  <div className="absolute right-0 mt-2 w-48 rounded-md shadow-lg bg-white ring-1 ring-black ring-opacity-5 z-50">
                    <div className="py-1" role="menu" aria-orientation="vertical">
                      <Link
                        to="/account"
                        className="block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                        role="menuitem"
                      >
                        My Account
                      </Link>
                      <Link
                        to="/orders"
                        className="block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                        role="menuitem"
                      >
                        Orders
                      </Link>
                      <Link
                        to="/wishlist"
                        className="block px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                        role="menuitem"
                      >
                        Wishlist
                      </Link>
                      <button
                        onClick={handleLogout}
                        className="block w-full text-left px-4 py-2 text-sm text-gray-700 hover:bg-gray-100"
                        role="menuitem"
                      >
                        Sign out
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ) : (
              <div className="flex items-center space-x-2">
                <Link to="/login" className="btn-outline text-sm py-1.5">
                  Sign in
                </Link>
                <Link to="/register" className="btn-primary text-sm py-1.5">
                  Register
                </Link>
              </div>
            )}
          </div>

          {/* Mobile menu button */}
          <div className="flex md:hidden items-center space-x-3">
            {/* Search button */}
            <button
              onClick={() => setIsSearchOpen(!isSearchOpen)}
              className="p-2 rounded-full hover:bg-gray-100"
            >
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-6 h-6">
                <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
              </svg>
            </button>
            
            {/* Cart button */}
            <Link to="/cart" className="relative p-2 rounded-full hover:bg-gray-100">
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-6 h-6">
                <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 3h1.386c.51 0 .955.343 1.087.835l.383 1.437M7.5 14.25a3 3 0 0 0-3 3h15.75m-12.75-3h11.218c1.121-2.3 2.1-4.684 2.924-7.138a60.114 60.114 0 0 0-16.536-1.84M7.5 14.25 5.106 5.272M6 20.25a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Zm12.75 0a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Z" />
              </svg>
              {cartItemCount > 0 && (
                <span className="absolute -top-1 -right-1 bg-pokemon-red text-white text-xs w-5 h-5 flex items-center justify-center rounded-full">
                  {cartItemCount}
                </span>
              )}
            </Link>
            
            {/* Hamburger menu button */}
            <button
              type="button"
              onClick={() => setIsMenuOpen(!isMenuOpen)}
              className="p-2 rounded-md inline-flex items-center justify-center text-gray-400 hover:text-gray-500 hover:bg-gray-100"
            >
              <span className="sr-only">Open main menu</span>
              <svg
                className={`${isMenuOpen ? 'hidden' : 'block'} h-6 w-6`}
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                aria-hidden="true"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d="M4 6h16M4 12h16M4 18h16"
                />
              </svg>
              <svg
                className={`${isMenuOpen ? 'block' : 'hidden'} h-6 w-6`}
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                aria-hidden="true"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d="M6 18L18 6M6 6l12 12"
                />
              </svg>
            </button>
          </div>
        </div>
      </div>

      {/* Mobile search bar */}
      {isSearchOpen && (
        <div className="md:hidden py-2 px-4 bg-gray-50 border-t border-b border-gray-200">
          <form onSubmit={handleSearch} className="relative">
            <input
              type="text"
              placeholder="Search for cards..."
              className="input pr-10"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            <button 
              type="submit"
              className="absolute right-2 top-1/2 transform -translate-y-1/2 text-gray-500 hover:text-gray-700"
            >
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5">
                <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
              </svg>
            </button>
          </form>
        </div>
      )}

      {/* Navigation bar */}
      <nav className="bg-pokemon-pokeblue text-white">
        <div className="container-custom">
          <div className="hidden md:flex items-center justify-between">
            <div className="flex space-x-8">
              {navLinks.map((link) => (
                <Link
                  key={link.path}
                  to={link.path}
                  className={`py-4 font-medium hover:text-pokemon-yellow transition-colors ${
                    location.pathname === link.path ? 'text-pokemon-yellow' : ''
                  }`}
                >
                  {link.name}
                </Link>
              ))}
            </div>
            <div className="flex items-center space-x-4">
              <Link to="/special-deals" className="py-4 font-medium text-pokemon-yellow hover:text-pokemon-yellowDark">
                Special Deals
              </Link>
              <Link to="/bulk-cards" className="py-4 font-medium hover:text-pokemon-yellow">
                Bulk Cards
              </Link>
            </div>
          </div>
        </div>
      </nav>

      {/* Mobile menu panel */}
      {isMenuOpen && (
        <div className="md:hidden bg-white border-t border-gray-200">
          <div className="px-2 pt-2 pb-3 space-y-1">
            {navLinks.map((link) => (
              <Link
                key={link.path}
                to={link.path}
                className={`block px-3 py-2 rounded-md text-base font-medium ${
                  location.pathname === link.path
                    ? 'bg-gray-100 text-gray-900'
                    : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900'
                }`}
                onClick={() => setIsMenuOpen(false)}
              >
                {link.name}
              </Link>
            ))}
            <Link
              to="/special-deals"
              className="block px-3 py-2 rounded-md text-base font-medium text-pokemon-red hover:bg-gray-50"
              onClick={() => setIsMenuOpen(false)}
            >
              Special Deals
            </Link>
            <Link
              to="/bulk-cards"
              className="block px-3 py-2 rounded-md text-base font-medium text-gray-700 hover:bg-gray-50 hover:text-gray-900"
              onClick={() => setIsMenuOpen(false)}
            >
              Bulk Cards
            </Link>
            
            {/* User options */}
            <div className="pt-4 pb-3 border-t border-gray-200">
              {user ? (
                <div>
                  <div className="flex items-center px-3">
                    <div className="flex-shrink-0">
                      <img
                        className="h-10 w-10 rounded-full"
                        src={user.avatar}
                        alt={user.name}
                      />
                    </div>
                    <div className="ml-3">
                      <div className="text-base font-medium text-gray-800">{user.name}</div>
                      <div className="text-sm font-medium text-gray-500">{user.email}</div>
                    </div>
                  </div>
                  <div className="mt-3 space-y-1">
                    <Link
                      to="/account"
                      className="block px-4 py-2 text-base font-medium text-gray-700 hover:bg-gray-100"
                      onClick={() => setIsMenuOpen(false)}
                    >
                      My Account
                    </Link>
                    <Link
                      to="/orders"
                      className="block px-4 py-2 text-base font-medium text-gray-700 hover:bg-gray-100"
                      onClick={() => setIsMenuOpen(false)}
                    >
                      Orders
                    </Link>
                    <Link
                      to="/wishlist"
                      className="block px-4 py-2 text-base font-medium text-gray-700 hover:bg-gray-100"
                      onClick={() => setIsMenuOpen(false)}
                    >
                      Wishlist
                    </Link>
                    <button
                      onClick={() => {
                        handleLogout();
                        setIsMenuOpen(false);
                      }}
                      className="block w-full text-left px-4 py-2 text-base font-medium text-gray-700 hover:bg-gray-100"
                    >
                      Sign out
                    </button>
                  </div>
                </div>
              ) : (
                <div className="flex flex-col space-y-2 px-3">
                  <Link
                    to="/login"
                    className="btn-outline"
                    onClick={() => setIsMenuOpen(false)}
                  >
                    Sign in
                  </Link>
                  <Link
                    to="/register"
                    className="btn-primary"
                    onClick={() => setIsMenuOpen(false)}
                  >
                    Register
                  </Link>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </header>
  );
}