import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import wishlistService from '../services/wishlistService';
import { useLocalStorage } from '../utils/hooks';

export default function WishlistPage() {
  const [user] = useLocalStorage('pokemonTCGUser', null);
  const [wishlist, setWishlist] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadWishlist = () => {
    setLoading(true);
    wishlistService
      .getWishlist()
      .then(setWishlist)
      .catch((err) => {
        console.error('Error fetching wishlist:', err);
        setError('Could not load your wishlist. Please try again later.');
      })
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    if (!user) {
      setLoading(false);
      return;
    }
    loadWishlist();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user]);

  const handleRemove = async (cardId) => {
    try {
      await wishlistService.removeFromWishlist(cardId);
      setWishlist((current) => current.filter((item) => item.cardId !== cardId));
    } catch (err) {
      console.error('Error removing from wishlist:', err);
    }
  };

  if (!user) {
    return (
      <div className="container-custom py-12 text-center">
        <h1 className="text-3xl font-bold mb-4">Sign in to see your wishlist</h1>
        <Link to="/login?redirect=/wishlist" className="btn-primary inline-block">
          Sign in
        </Link>
      </div>
    );
  }

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom">
        <h1 className="text-3xl font-bold mb-8">Your Wishlist</h1>

        {error && (
          <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded mb-6">{error}</div>
        )}

        {loading ? (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center text-gray-500">Loading wishlist...</div>
        ) : wishlist.length === 0 ? (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <p className="text-gray-600 mb-4">Your wishlist is empty.</p>
            <Link to="/shop" className="btn-primary inline-block">
              Browse Cards
            </Link>
          </div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
            {wishlist.map((item) => (
              <div key={item.wishlistItemId} className="bg-white rounded-lg shadow-sm overflow-hidden">
                {item.card ? (
                  <Link to={`/cards/${item.cardId}`}>
                    <img src={item.card.images?.small} alt={item.card.name} className="w-full" />
                  </Link>
                ) : (
                  <div className="h-40 bg-gray-100 flex items-center justify-center text-gray-400 text-sm">
                    Card unavailable
                  </div>
                )}
                <div className="p-3">
                  <p className="font-medium text-sm truncate">{item.card?.name || item.cardId}</p>
                  <button
                    onClick={() => handleRemove(item.cardId)}
                    className="text-red-500 hover:text-red-700 text-xs font-medium mt-2"
                  >
                    Remove
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
