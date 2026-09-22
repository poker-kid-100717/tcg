import React, { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import ordersService from '../services/ordersService';
import { useLocalStorage } from '../utils/hooks';
import { formatPrice } from '../utils/formatters';

export default function OrdersPage() {
  const location = useLocation();
  const [user] = useLocalStorage('pokemonTCGUser', null);
  const [orders, setOrders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!user) {
      setLoading(false);
      return;
    }

    let isMounted = true;
    setLoading(true);
    ordersService
      .getOrders()
      .then((data) => {
        if (isMounted) setOrders(data);
      })
      .catch((err) => {
        console.error('Error fetching orders:', err);
        if (isMounted) setError('Could not load your orders. Please try again later.');
      })
      .finally(() => {
        if (isMounted) setLoading(false);
      });

    return () => {
      isMounted = false;
    };
  }, [user]);

  if (!user) {
    return (
      <div className="container-custom py-12 text-center">
        <h1 className="text-3xl font-bold mb-4">Sign in to see your orders</h1>
        <Link to="/login?redirect=/orders" className="btn-primary inline-block">
          Sign in
        </Link>
      </div>
    );
  }

  const justPlacedOrderId = location.state?.justPlacedOrderId;

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom max-w-3xl">
        <h1 className="text-3xl font-bold mb-8">Your Orders</h1>

        {justPlacedOrderId && (
          <div className="bg-green-50 border border-green-200 text-green-800 px-4 py-3 rounded mb-6">
            Order #{justPlacedOrderId} placed successfully and saved to your account.
          </div>
        )}

        {error && (
          <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded mb-6">{error}</div>
        )}

        {loading ? (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center text-gray-500">Loading orders...</div>
        ) : orders.length === 0 ? (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <p className="text-gray-600 mb-4">You haven't placed any orders yet.</p>
            <Link to="/shop" className="btn-primary inline-block">
              Start Shopping
            </Link>
          </div>
        ) : (
          <div className="space-y-4">
            {orders.map((order) => (
              <div key={order.id} className="bg-white rounded-lg shadow-sm overflow-hidden">
                <div className="p-4 border-b border-gray-100 flex justify-between items-center">
                  <div>
                    <p className="font-semibold">Order #{order.id}</p>
                    <p className="text-sm text-gray-500">
                      {new Date(order.createdAt).toLocaleString()} - {order.status}
                    </p>
                  </div>
                  <div className="font-bold">{formatPrice(order.total)}</div>
                </div>
                <div>
                  {order.items.map((item) => (
                    <div key={item.id} className="p-3 border-b border-gray-50 last:border-b-0 flex justify-between text-sm">
                      <span>{item.name} x{item.quantity}</span>
                      <span>{formatPrice(item.price * item.quantity)}</span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
