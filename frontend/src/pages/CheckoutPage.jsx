import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import cartService from '../services/cartService';
import ordersService from '../services/ordersService';
import { useLocalStorage } from '../utils/hooks';
import { formatPrice } from '../utils/formatters';

export default function CheckoutPage() {
  const navigate = useNavigate();
  const [user] = useLocalStorage('pokemonTCGUser', null);
  const [cart, setCart] = useLocalStorage('pokemonTCGCart', cartService.getCart());
  const [isPlacingOrder, setIsPlacingOrder] = useState(false);
  const [error, setError] = useState('');

  // Checkout requires an account so the order can be persisted server-side
  // and looked up later from the Orders page.
  if (!user) {
    return (
      <div className="bg-pokemon-background min-h-screen py-12">
        <div className="container-custom max-w-md text-center">
          <div className="bg-white rounded-lg shadow-sm p-8">
            <h1 className="text-2xl font-bold mb-4">Sign in to check out</h1>
            <p className="text-gray-600 mb-6">
              Create an account or sign in so we can save your order.
            </p>
            <Link to="/login?redirect=/checkout" className="btn-primary w-full inline-block">
              Sign in
            </Link>
          </div>
        </div>
      </div>
    );
  }

  if (!cart.items.length) {
    return (
      <div className="bg-pokemon-background min-h-screen py-12">
        <div className="container-custom max-w-md text-center">
          <div className="bg-white rounded-lg shadow-sm p-8">
            <h1 className="text-2xl font-bold mb-4">Your cart is empty</h1>
            <Link to="/shop" className="btn-primary inline-block">
              Start Shopping
            </Link>
          </div>
        </div>
      </div>
    );
  }

  const handlePlaceOrder = async () => {
    setError('');
    setIsPlacingOrder(true);
    try {
      const order = await ordersService.createOrder(cart.total, cart.items);
      const emptyCart = cartService.clearCart();
      setCart(emptyCart);
      navigate('/orders', { state: { justPlacedOrderId: order.id } });
    } catch (err) {
      console.error('Error placing order:', err);
      setError(err.response?.data?.message || 'Something went wrong placing your order. Please try again.');
    } finally {
      setIsPlacingOrder(false);
    }
  };

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom max-w-2xl">
        <h1 className="text-3xl font-bold mb-8">Checkout</h1>

        <div className="bg-white rounded-lg shadow-sm overflow-hidden mb-6">
          <div className="p-4 border-b border-gray-100">
            <h2 className="text-lg font-semibold">Order Items</h2>
          </div>
          <div>
            {cart.items.map((item) => (
              <div key={item.id} className="p-4 border-b border-gray-100 last:border-b-0 flex justify-between items-center">
                <div>
                  <p className="font-medium">{item.name}</p>
                  <p className="text-sm text-gray-500">Qty {item.quantity}</p>
                </div>
                <div className="font-semibold">{formatPrice(item.price * item.quantity)}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="bg-white rounded-lg shadow-sm p-4 mb-6">
          <div className="space-y-2 pb-4 mb-4 border-b border-gray-100">
            <div className="flex justify-between">
              <span className="text-gray-600">Subtotal</span>
              <span>{formatPrice(cart.subtotal)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-600">Tax</span>
              <span>{formatPrice(cart.tax)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-600">Shipping</span>
              <span>{cart.shipping === 0 ? <span className="text-green-600">Free</span> : formatPrice(cart.shipping)}</span>
            </div>
          </div>
          <div className="flex justify-between items-center">
            <span className="font-bold">Total</span>
            <span className="text-xl font-bold">{formatPrice(cart.total)}</span>
          </div>
        </div>

        {error && (
          <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded mb-6">
            {error}
          </div>
        )}

        <button
          onClick={handlePlaceOrder}
          disabled={isPlacingOrder}
          className="btn-primary w-full py-3 disabled:opacity-60"
        >
          {isPlacingOrder ? 'Placing order...' : `Place Order - ${formatPrice(cart.total)}`}
        </button>
        <p className="text-center text-sm text-gray-500 mt-4">
          This is a portfolio project - no real payment is processed. Placing an order
          creates a real record in the backend's database, tied to your account.
        </p>
      </div>
    </div>
  );
}
