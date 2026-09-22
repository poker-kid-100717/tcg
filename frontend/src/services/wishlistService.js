// Wishlist Service - persists the wishlist server-side via WishlistController.
// Requires an authenticated user; httpClient automatically attaches the JWT.
import httpClient from './httpClient';

export const getWishlist = async () => {
  const { data } = await httpClient.get('/wishlist');
  return data; // [{ wishlistItemId, cardId, addedAt, card }]
};

export const addToWishlist = async (cardId) => {
  const { data } = await httpClient.post(`/wishlist/${cardId}`);
  return data;
};

export const removeFromWishlist = async (cardId) => {
  const { data } = await httpClient.delete(`/wishlist/${cardId}`);
  return data;
};

const wishlistService = {
  getWishlist,
  addToWishlist,
  removeFromWishlist,
};

export default wishlistService;
