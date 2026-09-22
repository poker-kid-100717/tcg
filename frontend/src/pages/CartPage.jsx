import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import cartService from '../services/cartService';
import { useLocalStorage } from '../utils/hooks';
import { formatPrice } from '../utils/formatters';

export default function CartPage() {
  const [cart, setCart] = useLocalStorage('pokemonTCGCart', cartService.getCart());
  const [couponCode, setCouponCode] = useState('');
  const [couponError, setCouponError] = useState('');
  const [couponSuccess, setCouponSuccess] = useState('');
  
  const handleUpdateQuantity = (cardId, quantity) => {
    const updatedCart = cartService.updateItemQuantity(cardId, quantity);
    setCart(updatedCart);
  };
  
  const handleRemoveItem = (cardId) => {
    const updatedCart = cartService.removeItemFromCart(cardId);
    setCart(updatedCart);
  };
  
  const handleClearCart = () => {
    const emptyCart = cartService.clearCart();
    setCart(emptyCart);
  };
  
  const handleApplyCoupon = () => {
    setCouponError('');
    setCouponSuccess('');
    
    if (!couponCode.trim()) {
      setCouponError('Please enter a coupon code');
      return;
    }
    
    // Mock coupon codes
    const validCoupons = {
      'POKEMON10': 10,
      'PIKACHU20': 20,
      'CHARIZARD30': 30
    };
    
    if (validCoupons[couponCode.toUpperCase()]) {
      // In a real app, you would call an API to validate the coupon
      // Here we'll just simulate a successful application
      setCouponSuccess(`Coupon applied! ${validCoupons[couponCode.toUpperCase()]}% discount`);
      setCouponCode('');
    } else {
      setCouponError('Invalid coupon code');
    }
  };

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom">
        <h1 className="text-3xl md:text-4xl font-bold mb-8">Your Shopping Cart</h1>
        
        {cart.items.length === 0 ? (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <div className="text-gray-400 mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-16 h-16 mx-auto">
                <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 3h1.386c.51 0 .955.343 1.087.835l.383 1.437M7.5 14.25a3 3 0 00-3 3h15.75m-12.75-3h11.218c1.121-2.3 2.1-4.684 2.924-7.138a60.114 60.114 0 00-16.536-1.84M7.5 14.25L5.106 5.272M6 20.25a.75.75 0 11-1.5 0 .75.75 0 011.5 0zm12.75 0a.75.75 0 11-1.5 0 .75.75 0 011.5 0z" />
              </svg>
            </div>
            <h2 className="text-2xl font-semibold mb-4">Your cart is empty</h2>
            <p className="text-gray-600 mb-6">
              Looks like you haven't added any Pokémon cards to your cart yet.
            </p>
            <Link to="/shop" className="btn-primary">
              Start Shopping
            </Link>
          </div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
            {/* Cart items */}
            <div className="lg:col-span-2">
              <div className="bg-white rounded-lg shadow-sm overflow-hidden mb-6">
                <div className="p-4 border-b border-gray-100 flex justify-between items-center">
                  <h2 className="text-lg font-semibold">
                    Cart Items ({cart.items.reduce((total, item) => total + item.quantity, 0)})
                  </h2>
                  <button
                    onClick={handleClearCart}
                    className="text-gray-500 hover:text-gray-700 text-sm font-medium"
                  >
                    Clear Cart
                  </button>
                </div>
                
                <div>
                  {cart.items.map(item => (
                    <div 
                      key={item.id}
                      className="p-4 border-b border-gray-100 last:border-b-0 flex flex-col sm:flex-row gap-4"
                    >
                      <div className="flex-shrink-0">
                        <img 
                          src={item.image} 
                          alt={item.name}
                          className="w-24 h-auto rounded"
                        />
                      </div>
                      
                      <div className="flex-grow">
                        <div className="flex flex-col sm:flex-row sm:justify-between mb-2">
                          <div>
                            <h3 className="text-lg font-medium">
                              <Link to={`/cards/${item.id}`} className="hover:text-primary-600">
                                {item.name}
                              </Link>
                            </h3>
                            <p className="text-sm text-gray-500">
                              {item.set} • {item.rarity}
                            </p>
                          </div>
                          <div className="font-bold text-lg mt-2 sm:mt-0">
                            {formatPrice(item.price * item.quantity)}
                          </div>
                        </div>
                        
                        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                          <div className="flex items-center">
                            <span className="text-sm text-gray-500 mr-2">Qty:</span>
                            <div className="flex">
                              <button
                                onClick={() => handleUpdateQuantity(item.id, Math.max(1, item.quantity - 1))}
                                className="px-2 py-1 border border-r-0 border-gray-300 rounded-l-md bg-gray-50 hover:bg-gray-100"
                              >
                                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4">
                                  <path d="M6.75 9.25a.75.75 0 000 1.5h6.5a.75.75 0 000-1.5h-6.5z" />
                                </svg>
                              </button>
                              <input
                                type="number"
                                min="1"
                                value={item.quantity}
                                onChange={(e) => handleUpdateQuantity(item.id, Math.max(1, parseInt(e.target.value) || 1))}
                                className="w-12 py-1 px-2 border border-gray-300 text-center"
                              />
                              <button
                                onClick={() => handleUpdateQuantity(item.id, item.quantity + 1)}
                                className="px-2 py-1 border border-l-0 border-gray-300 rounded-r-md bg-gray-50 hover:bg-gray-100"
                              >
                                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4">
                                  <path d="M10.75 6.75a.75.75 0 00-1.5 0v2.5h-2.5a.75.75 0 000 1.5h2.5v2.5a.75.75 0 001.5 0v-2.5h2.5a.75.75 0 000-1.5h-2.5v-2.5z" />
                                </svg>
                              </button>
                            </div>
                          </div>
                          
                          <div className="text-sm text-gray-500">
                            {formatPrice(item.price)} each
                          </div>
                          
                          <button
                            onClick={() => handleRemoveItem(item.id)}
                            className="text-red-500 hover:text-red-700 text-sm font-medium flex items-center"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-4 h-4 mr-1">
                              <path strokeLinecap="round" strokeLinejoin="round" d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 00-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 00-7.5 0" />
                            </svg>
                            Remove
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
              
              <div className="bg-white rounded-lg shadow-sm p-4">
                <h2 className="text-lg font-semibold mb-4">Apply Coupon</h2>
                
                <div className="flex flex-col sm:flex-row gap-3">
                  <input
                    type="text"
                    placeholder="Enter coupon code"
                    value={couponCode}
                    onChange={(e) => setCouponCode(e.target.value)}
                    className="input flex-grow"
                  />
                  <button
                    onClick={handleApplyCoupon}
                    className="btn-outline whitespace-nowrap"
                  >
                    Apply Coupon
                  </button>
                </div>
                
                {couponError && (
                  <p className="text-red-500 text-sm mt-2">{couponError}</p>
                )}
                
                {couponSuccess && (
                  <p className="text-green-600 text-sm mt-2">{couponSuccess}</p>
                )}
                
                <div className="mt-4 text-sm text-gray-600">
                  Try coupon codes: POKEMON10, PIKACHU20, CHARIZARD30
                </div>
              </div>
            </div>
            
            {/* Order summary */}
            <div className="lg:col-span-1">
              <div className="bg-white rounded-lg shadow-sm p-4 sticky top-6">
                <h2 className="text-lg font-semibold mb-4">Order Summary</h2>
                
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
                    <span>
                      {cart.shipping === 0 
                        ? <span className="text-green-600">Free</span> 
                        : formatPrice(cart.shipping)
                      }
                    </span>
                  </div>
                  {couponSuccess && (
                    <div className="flex justify-between text-green-600">
                      <span>Discount</span>
                      <span>-{formatPrice(cart.subtotal * 0.1)}</span>
                    </div>
                  )}
                </div>
                
                <div className="flex justify-between items-center mb-6">
                  <span className="font-bold">Total</span>
                  <span className="text-xl font-bold">
                    {couponSuccess
                      ? formatPrice(cart.total * 0.9) // Apply 10% discount
                      : formatPrice(cart.total)
                    }
                  </span>
                </div>
                
                <Link
                  to="/checkout" 
                  className="btn-primary py-3 w-full flex justify-center items-center"
                >
                  Proceed to Checkout
                </Link>
                
                <p className="text-center text-sm text-gray-500 mt-4">
                  Free shipping on orders over $35
                </p>
                
                <div className="mt-6">
                  <h3 className="font-medium mb-2">We Accept</h3>
                  <div className="flex items-center space-x-2">
                    <img 
                      src="https://www.paypalobjects.com/webstatic/en_US/i/buttons/PP_logo_h_100x26.png"
                      alt="PayPal"
                      className="h-6"
                    />
                    <img
                      src="https://www.mastercard.us/content/dam/mccom/global/logos/logo-mastercard-mobile.svg"
                      alt="Mastercard"
                      className="h-6"
                    />
                    <img
                      src="https://www.visa.com/images/merchantoffers/card-image.png"
                      alt="Visa"
                      className="h-6"
                    />
                    <img
                      src="https://www.discover.com/content/dam/discover/en_us/global/logos/discover-logo.svg"
                      alt="Discover"
                      className="h-6"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}