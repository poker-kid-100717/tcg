// Utility functions for formatting data

// Format price to display as currency
export const formatPrice = (price) => {
  if (price === undefined || price === null) return 'N/A';
  
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(price);
};

// Format date to display in a user-friendly format
export const formatDate = (dateString) => {
  if (!dateString) return 'N/A';
  
  const options = { year: 'numeric', month: 'short', day: 'numeric' };
  return new Date(dateString).toLocaleDateString('en-US', options);
};

// Format card name to include set if needed
export const formatCardName = (card) => {
  if (!card) return '';
  
  if (card.set) {
    return `${card.name} (${card.set.name})`;
  }
  
  return card.name;
};

// Format rarity with color coding
export const formatRarity = (rarity) => {
  if (!rarity) return { label: 'Unknown', className: 'badge-common' };
  
  const rarityTypes = {
    'Common': { label: 'Common', className: 'badge-common' },
    'Uncommon': { label: 'Uncommon', className: 'bg-green-100 text-green-800' },
    'Rare': { label: 'Rare', className: 'bg-blue-100 text-blue-800' },
    'Rare Holo': { label: 'Holo Rare', className: 'bg-indigo-100 text-indigo-800' },
    'Rare Ultra': { label: 'Ultra Rare', className: 'bg-purple-100 text-purple-800' },
    'Rare Holo EX': { label: 'EX Holo Rare', className: 'bg-purple-100 text-purple-800' },
    'Rare Holo GX': { label: 'GX Holo Rare', className: 'bg-purple-100 text-purple-800' },
    'Rare Holo V': { label: 'V Holo Rare', className: 'bg-purple-100 text-purple-800' },
    'Rare Holo VMAX': { label: 'VMAX Holo Rare', className: 'bg-pink-100 text-pink-800' },
    'Rare Holo VSTAR': { label: 'VSTAR Holo Rare', className: 'bg-pink-100 text-pink-800' },
    'Rare Secret': { label: 'Secret Rare', className: 'badge-rare' },
    'Rare Rainbow': { label: 'Rainbow Rare', className: 'bg-yellow-100 text-yellow-800' },
    'Rare Shiny': { label: 'Shiny Rare', className: 'bg-teal-100 text-teal-800' },
    'Rare Shiny GX': { label: 'Shiny GX Rare', className: 'bg-teal-100 text-teal-800' },
    'Promo': { label: 'Promo', className: 'bg-orange-100 text-orange-800' },
  };
  
  return rarityTypes[rarity] || { label: rarity, className: 'badge-common' };
};

// Helper to truncate text
export const truncateText = (text, maxLength = 100) => {
  if (!text) return '';
  if (text.length <= maxLength) return text;
  
  return text.substring(0, maxLength) + '...';
};

// Helper to generate readable ID from card ID
export const readableCardId = (id) => {
  if (!id) return '';
  
  // Example: swsh12-179 becomes SWSH12 #179
  const parts = id.split('-');
  if (parts.length !== 2) return id.toUpperCase();
  
  return `${parts[0].toUpperCase()} #${parts[1]}`;
};

// Calculate price trend percentage
export const calculatePriceTrend = (priceHistory) => {
  if (!priceHistory || priceHistory.length < 2) return { trend: 0, direction: 'neutral' };
  
  const oldestPrice = priceHistory[0].price;
  const newestPrice = priceHistory[priceHistory.length - 1].price;
  
  const trend = ((newestPrice - oldestPrice) / oldestPrice) * 100;
  const direction = trend > 0 ? 'up' : trend < 0 ? 'down' : 'neutral';
  
  return { 
    trend: parseFloat(Math.abs(trend).toFixed(1)), 
    direction 
  };
};

// Format investment rating as stars
export const formatRatingAsStars = (rating) => {
  const maxStars = 5;
  const fullStars = Math.floor(rating);
  const halfStar = rating % 1 >= 0.5;
  const emptyStars = maxStars - fullStars - (halfStar ? 1 : 0);
  
  return {
    full: fullStars,
    half: halfStar ? 1 : 0,
    empty: emptyStars
  };
};

// Generate random seller data (for marketplace simulation)
export const generateSellerData = (cardId) => {
  // Use cardId to generate consistent seller data for the same card
  const seed = cardId.split('').reduce((acc, char) => acc + char.charCodeAt(0), 0);
  
  const sellerNames = [
    'PokéCollector',
    'CardMaster',
    'EliteTrainer',
    'RareFinds',
    'VintageCards',
    'PremiumDecks',
    'CardKingdom',
    'PokéVault',
    'GottaCatchEm',
    'TrainerHouse'
  ];
  
  const conditions = ['Mint', 'Near Mint', 'Excellent', 'Good', 'Lightly Played'];
  const sellerCount = 3 + (seed % 5); // 3-7 sellers
  
  const sellers = [];
  let basePrice;
  
  // Generate a base price based on card ID
  if (cardId.includes('secret') || cardId.includes('rainbow')) {
    basePrice = 50 + (seed % 150); // $50-$200 for secret/rainbow rares
  } else if (cardId.includes('ultra') || cardId.includes('gx') || cardId.includes('vmax')) {
    basePrice = 15 + (seed % 85); // $15-$100 for ultra rares
  } else if (cardId.includes('holo')) {
    basePrice = 5 + (seed % 20); // $5-$25 for holos
  } else {
    basePrice = 0.5 + (seed % 10); // $0.50-$10.50 for commons/uncommons
  }
  
  // Generate sellers with different prices and conditions
  for (let i = 0; i < sellerCount; i++) {
    const sellerIndex = (seed + i) % sellerNames.length;
    const conditionIndex = (seed + i * 3) % conditions.length;
    
    // Price variation based on condition
    const conditionFactor = 1 - (conditionIndex * 0.1); // Worse condition = lower price
    const priceFactor = 0.9 + (Math.sin(i + seed) * 0.2); // Random variation ±20%
    const price = (basePrice * conditionFactor * priceFactor).toFixed(2);
    
    // Rating 3.5-5.0
    const rating = (3.5 + ((seed + i) % 15) / 10).toFixed(1);
    
    // Sales count 10-1000
    const sales = 10 + ((seed + i * i) % 990);
    
    sellers.push({
      id: i + 1,
      name: sellerNames[sellerIndex],
      price: parseFloat(price),
      condition: conditions[conditionIndex],
      rating: parseFloat(rating),
      sales: sales,
      inStock: 1 + (i % 15) // 1-15 in stock
    });
  }
  
  // Sort by price ascending
  return sellers.sort((a, b) => a.price - b.price);
};