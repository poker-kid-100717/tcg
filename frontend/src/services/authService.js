// Auth Service - Manages user authentication (frontend only)

// Mock user data
const MOCK_USERS = [
  {
    id: 1,
    username: 'pokemaster',
    email: 'ash@pokemon.com',
    password: 'pikachu123',
    name: 'Ash Ketchum',
    avatar: 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/25.png',
    wishlist: ['sv4-172', 'sv3-123', 'swsh12-179'],
    recentPurchases: [
      { id: 'swsh12-179', date: '2023-11-28', price: 89.99 },
      { id: 'sv3-151', date: '2023-10-15', price: 26.99 }
    ]
  },
  {
    id: 2,
    username: 'deckbuilder',
    email: 'gary@pokemon.com',
    password: 'blastoise456',
    name: 'Gary Oak',
    avatar: 'https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/9.png',
    wishlist: ['swsh11-94', 'swsh9-123'],
    recentPurchases: [
      { id: 'sv3-72', date: '2023-12-01', price: 15.99 },
      { id: 'sv4-178', date: '2023-11-20', price: 65.99 }
    ]
  }
];

// Check if user is already logged in
export const checkAuth = () => {
  const user = localStorage.getItem('pokemonTCGUser');
  return user ? JSON.parse(user) : null;
};

// Login user
export const login = (email, password) => {
  // Find user with matching email and password
  const user = MOCK_USERS.find(u => 
    u.email === email && u.password === password
  );
  
  if (user) {
    // Don't store the password in localStorage
    const { password, ...userWithoutPassword } = user;
    localStorage.setItem('pokemonTCGUser', JSON.stringify(userWithoutPassword));
    return { success: true, user: userWithoutPassword };
  }
  
  return { success: false, message: 'Invalid email or password' };
};

// Register new user
export const register = (userData) => {
  // Check if email is already in use
  if (MOCK_USERS.some(u => u.email === userData.email)) {
    return { success: false, message: 'Email already in use' };
  }
  
  // Create new user
  const newUser = {
    id: MOCK_USERS.length + 1,
    username: userData.username,
    email: userData.email,
    password: userData.password,
    name: userData.name || userData.username,
    avatar: `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/${Math.floor(Math.random() * 151) + 1}.png`,
    wishlist: [],
    recentPurchases: []
  };
  
  // In a real app, you would send this to a backend API
  // For demo purposes, we'll just simulate a successful registration
  
  // Don't store the password in localStorage
  const { password, ...userWithoutPassword } = newUser;
  localStorage.setItem('pokemonTCGUser', JSON.stringify(userWithoutPassword));
  
  return { success: true, user: userWithoutPassword };
};

// Logout user
export const logout = () => {
  localStorage.removeItem('pokemonTCGUser');
  return { success: true };
};

// Add card to user wishlist
export const addToWishlist = (cardId) => {
  const user = checkAuth();
  if (!user) return { success: false, message: 'User not logged in' };
  
  if (!user.wishlist.includes(cardId)) {
    user.wishlist.push(cardId);
    localStorage.setItem('pokemonTCGUser', JSON.stringify(user));
  }
  
  return { success: true, wishlist: user.wishlist };
};

// Remove card from user wishlist
export const removeFromWishlist = (cardId) => {
  const user = checkAuth();
  if (!user) return { success: false, message: 'User not logged in' };
  
  user.wishlist = user.wishlist.filter(id => id !== cardId);
  localStorage.setItem('pokemonTCGUser', JSON.stringify(user));
  
  return { success: true, wishlist: user.wishlist };
};

// Add a purchase to user history
export const addPurchase = (cardId, price) => {
  const user = checkAuth();
  if (!user) return { success: false, message: 'User not logged in' };
  
  const purchase = {
    id: cardId,
    date: new Date().toISOString().substring(0, 10),
    price: price
  };
  
  user.recentPurchases.unshift(purchase); // Add to beginning of array
  
  // Keep only the 10 most recent purchases
  if (user.recentPurchases.length > 10) {
    user.recentPurchases = user.recentPurchases.slice(0, 10);
  }
  
  localStorage.setItem('pokemonTCGUser', JSON.stringify(user));
  
  return { success: true, purchases: user.recentPurchases };
};

// Export auth service object
const authService = {
  checkAuth,
  login,
  register,
  logout,
  addToWishlist,
  removeFromWishlist,
  addPurchase
};

export default authService;