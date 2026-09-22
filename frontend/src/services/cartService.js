// Cart Service - Manages the shopping cart functionality

// Initial empty cart
const initialCart = {
  items: [],
  subtotal: 0,
  tax: 0,
  shipping: 0,
  total: 0
};

// Get cart from localStorage or return initial empty cart
export const getCart = () => {
  const savedCart = localStorage.getItem('pokemonTCGCart');
  return savedCart ? JSON.parse(savedCart) : initialCart;
};

// Save cart to localStorage
export const saveCart = (cart) => {
  localStorage.setItem('pokemonTCGCart', JSON.stringify(cart));
};

// Add item to cart
export const addItemToCart = (card, quantity = 1, price) => {
  const cart = getCart();
  const existingItemIndex = cart.items.findIndex(item => item.id === card.id);

  if (existingItemIndex >= 0) {
    // Update existing item quantity
    cart.items[existingItemIndex].quantity += quantity;
  } else {
    // Add new item
    cart.items.push({
      id: card.id,
      name: card.name,
      image: card.images.small,
      price: price || card.cardmarket?.prices?.averageSellPrice || card.tcgplayer?.prices?.holofoil?.market || 9.99,
      quantity: quantity,
      set: card.set.name,
      rarity: card.rarity
    });
  }

  // Update cart totals
  updateCartTotals(cart);
  saveCart(cart);
  return cart;
};

// Remove item from cart
export const removeItemFromCart = (cardId) => {
  const cart = getCart();
  cart.items = cart.items.filter(item => item.id !== cardId);
  updateCartTotals(cart);
  saveCart(cart);
  return cart;
};

// Update item quantity in cart
export const updateItemQuantity = (cardId, quantity) => {
  const cart = getCart();
  const itemIndex = cart.items.findIndex(item => item.id === cardId);
  
  if (itemIndex >= 0) {
    if (quantity <= 0) {
      // Remove item if quantity is 0 or negative
      cart.items.splice(itemIndex, 1);
    } else {
      // Update quantity
      cart.items[itemIndex].quantity = quantity;
    }
    
    updateCartTotals(cart);
    saveCart(cart);
  }
  
  return cart;
};

// Clear entire cart
export const clearCart = () => {
  saveCart(initialCart);
  return initialCart;
};

// Helper function to update cart totals
const updateCartTotals = (cart) => {
  // Calculate subtotal
  cart.subtotal = cart.items.reduce((total, item) => total + (item.price * item.quantity), 0);
  
  // Calculate tax (assume 8.5%)
  cart.tax = cart.subtotal * 0.085;
  
  // Calculate shipping (free for orders over $35, otherwise $4.99)
  cart.shipping = cart.subtotal > 35 ? 0 : 4.99;
  
  // Calculate total
  cart.total = cart.subtotal + cart.tax + cart.shipping;
  
  // Round all values to 2 decimal places
  cart.subtotal = parseFloat(cart.subtotal.toFixed(2));
  cart.tax = parseFloat(cart.tax.toFixed(2));
  cart.shipping = parseFloat(cart.shipping.toFixed(2));
  cart.total = parseFloat(cart.total.toFixed(2));
};

// Export cart service object
const cartService = {
  getCart,
  addItemToCart,
  removeItemFromCart,
  updateItemQuantity,
  clearCart
};

export default cartService;