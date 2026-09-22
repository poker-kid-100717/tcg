// Orders Service - persists orders server-side via OrdersController.
// Requires an authenticated user (the backend endpoint is [Authorize]);
// httpClient automatically attaches the JWT when one is present.
import httpClient from './httpClient';

export const getOrders = async () => {
  const { data } = await httpClient.get('/orders');
  return data;
};

export const createOrder = async (total, items) => {
  const { data } = await httpClient.post('/orders', {
    total,
    items: items.map((item) => ({
      cardId: item.id,
      cardName: item.name,
      cardImage: item.image,
      price: item.price,
      quantity: item.quantity,
    })),
  });
  return data;
};

const ordersService = {
  getOrders,
  createOrder,
};

export default ordersService;
