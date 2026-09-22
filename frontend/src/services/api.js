import axios from 'axios';

// Create axios instance for Pokemon TCG API
const pokemonTCGAPI = axios.create({
  baseURL: 'https://api.pokemontcg.io/v2',
  headers: {
    'Content-Type': 'application/json',
    // You would typically add an API key here
    // 'X-Api-Key': 'your-api-key'
  },
});

// Get all cards with pagination and filters
export const getCards = async (params = {}) => {
  try {
    // If there's a query parameter, make sure 'q' is properly formatted for the API
    if (params.q) {
      // If the query doesn't have specific filters, search in name
      if (!params.q.includes(':')) {
        params.q = `name:"${params.q}*"`;
      }
    }
    
    const response = await pokemonTCGAPI.get('/cards', { params });
    return response.data;
  } catch (error) {
    console.error('Error fetching Pokemon cards:', error);
    throw error;
  }
};

// Get a single card by ID
export const getCardById = async (id) => {
  try {
    const response = await pokemonTCGAPI.get(`/cards/${id}`);
    return response.data;
  } catch (error) {
    console.error(`Error fetching Pokemon card with ID ${id}:`, error);
    throw error;
  }
};

// Get all sets
export const getSets = async () => {
  try {
    const response = await pokemonTCGAPI.get('/sets');
    return response.data;
  } catch (error) {
    console.error('Error fetching Pokemon sets:', error);
    throw error;
  }
};

// Get a single set by ID
export const getSetById = async (id) => {
  try {
    const response = await pokemonTCGAPI.get(`/sets/${id}`);
    return response.data;
  } catch (error) {
    console.error(`Error fetching Pokemon set with ID ${id}:`, error);
    throw error;
  }
};

// Get all types
export const getTypes = async () => {
  try {
    const response = await pokemonTCGAPI.get('/types');
    return response.data;
  } catch (error) {
    console.error('Error fetching Pokemon types:', error);
    throw error;
  }
};

// Get all subtypes
export const getSubtypes = async () => {
  try {
    const response = await pokemonTCGAPI.get('/subtypes');
    return response.data;
  } catch (error) {
    console.error('Error fetching Pokemon subtypes:', error);
    throw error;
  }
};

// Get all rarities
export const getRarities = async () => {
  try {
    const response = await pokemonTCGAPI.get('/rarities');
    return response.data;
  } catch (error) {
    console.error('Error fetching Pokemon rarities:', error);
    throw error;
  }
};

// Get card market prices by ID
export const getCardMarketPrices = async (id) => {
  try {
    const response = await pokemonTCGAPI.get(`/cards/${id}`);
    return response.data.card?.tcgplayer?.prices || null;
  } catch (error) {
    console.error(`Error fetching market prices for card ${id}:`, error);
    throw error;
  }
};

// Generate mock price history data (this would be replaced by real API endpoint in production)
export const getCardPriceHistory = async (id) => {
  // Simulate API call delay
  await new Promise(resolve => setTimeout(resolve, 500));
  
  // Generate random price fluctuations for the past 12 months
  const today = new Date();
  const data = [];
  
  // Start with a base price between $1 and $100 based on the card ID
  // This ensures the same card always gets the same starting price
  const seed = id.split('').reduce((acc, char) => acc + char.charCodeAt(0), 0);
  let basePrice = (seed % 100) + 1;
  
  // If the seed is even, make it a "rare" card with higher price
  if (seed % 2 === 0) {
    basePrice += 50;
  }
  
  // If the seed is divisible by 3, make it a "ultra rare" card
  if (seed % 3 === 0) {
    basePrice += 100;
  }
  
  // Generate monthly data points
  for (let i = 11; i >= 0; i--) {
    const date = new Date(today);
    date.setMonth(today.getMonth() - i);
    
    // Create price fluctuation (up to ±15% from previous price)
    const fluctuation = basePrice * (0.85 + (Math.random() * 0.3));
    
    // Ensure some growth trend for card value over time (0.5-2% monthly growth)
    const growth = 1 + ((Math.random() * 1.5 + 0.5) / 100);
    
    basePrice = fluctuation * growth;
    
    data.push({
      date: date.toISOString().substring(0, 10),
      price: parseFloat(basePrice.toFixed(2))
    });
  }
  
  return {
    id,
    priceHistory: data,
    predictedPrices: generatePredictedPrices(data, id),
  };
};

// Generate predicted prices for the next 6 months
const generatePredictedPrices = (historicalData, id) => {
  const lastPrice = historicalData[historicalData.length - 1].price;
  const predictedData = [];
  
  // Calculate average monthly growth from historical data
  let totalGrowth = 0;
  for (let i = 1; i < historicalData.length; i++) {
    const monthlyGrowth = historicalData[i].price / historicalData[i-1].price;
    totalGrowth += monthlyGrowth;
  }
  
  // Average monthly growth rate plus a small random factor
  const avgGrowthRate = (totalGrowth / (historicalData.length - 1)) || 1.01;
  
  // Add some variability based on card ID
  const seed = id.split('').reduce((acc, char) => acc + char.charCodeAt(0), 0);
  const cardSpecificFactor = (seed % 10) / 100; // 0-0.09 additional growth
  
  // Get the last date from historical data
  const lastDate = new Date(historicalData[historicalData.length - 1].date);
  let currentPrice = lastPrice;
  
  // Generate predictions for 6 months
  for (let i = 1; i <= 6; i++) {
    const date = new Date(lastDate);
    date.setMonth(lastDate.getMonth() + i);
    
    // Add some randomness to the growth rate
    const randomFactor = 0.95 + (Math.random() * 0.1); // 0.95-1.05
    const monthlyGrowth = avgGrowthRate * randomFactor + cardSpecificFactor;
    
    currentPrice = currentPrice * monthlyGrowth;
    
    predictedData.push({
      date: date.toISOString().substring(0, 10),
      price: parseFloat(currentPrice.toFixed(2)),
      isProjection: true
    });
  }
  
  return predictedData;
};

// Generate investment potential rating based on card data and price history
export const getInvestmentPotentialRating = async (cardId) => {
  try {
    // Get card details and price history
    const [cardData, priceHistory] = await Promise.all([
      getCardById(cardId),
      getCardPriceHistory(cardId)
    ]);
    
    const card = cardData.data;
    
    // Factors that influence investment potential
    const factors = {
      rarity: getRarityScore(card.rarity),
      priceGrowth: getPriceGrowthScore(priceHistory),
      setRotation: getSetRotationScore(card.set.releaseDate),
      popularity: getPopularityScore(cardId)
    };
    
    // Calculate overall score (out of 5)
    const overallScore = (
      factors.rarity * 0.35 + 
      factors.priceGrowth * 0.30 + 
      factors.setRotation * 0.15 + 
      factors.popularity * 0.20
    );
    
    return {
      rating: parseFloat(overallScore.toFixed(1)),
      factors: factors,
      confidenceLevel: getConfidenceLevel(factors)
    };
  } catch (error) {
    console.error(`Error calculating investment potential for card ${cardId}:`, error);
    throw error;
  }
};

// Helper function to score rarity
const getRarityScore = (rarity) => {
  const rarityScores = {
    'Common': 1,
    'Uncommon': 1.5,
    'Rare': 2.5,
    'Rare Holo': 3,
    'Rare Ultra': 3.5,
    'Rare Holo EX': 4,
    'Rare Holo GX': 4,
    'Rare Holo V': 4,
    'Rare Holo VMAX': 4.5,
    'Rare Holo VSTAR': 4.5,
    'Rare Secret': 5,
    'Rare Rainbow': 5,
    'Rare Shiny': 5,
    'Rare Shiny GX': 5
  };
  
  return rarityScores[rarity] || 2.5;
};

// Helper function to score price growth
const getPriceGrowthScore = (priceHistory) => {
  if (!priceHistory || !priceHistory.priceHistory || priceHistory.priceHistory.length < 2) {
    return 3; // Default score if not enough data
  }
  
  const prices = priceHistory.priceHistory;
  const firstPrice = prices[0].price;
  const lastPrice = prices[prices.length - 1].price;
  
  // Calculate percentage growth
  const growthPercent = ((lastPrice - firstPrice) / firstPrice) * 100;
  
  // Score based on annual growth rate
  if (growthPercent > 50) return 5;
  if (growthPercent > 30) return 4.5;
  if (growthPercent > 20) return 4;
  if (growthPercent > 10) return 3.5;
  if (growthPercent > 5) return 3;
  if (growthPercent > 0) return 2.5;
  if (growthPercent > -5) return 2;
  if (growthPercent > -10) return 1.5;
  return 1;
};

// Helper function to score set rotation (newer sets typically score higher)
const getSetRotationScore = (releaseDate) => {
  if (!releaseDate) return 3;
  
  const releaseYear = new Date(releaseDate).getFullYear();
  const currentYear = new Date().getFullYear();
  const yearDiff = currentYear - releaseYear;
  
  // Newer sets score higher (more likely to be in standard format)
  if (yearDiff <= 1) return 4.5;  
  if (yearDiff <= 2) return 4;
  if (yearDiff <= 3) return 3.5;
  if (yearDiff <= 5) return 3;
  if (yearDiff <= 10) return 2.5; // Vintage starting to have collector value
  if (yearDiff <= 15) return 3;   // Vintage has more collector value
  if (yearDiff <= 20) return 3.5; // Older vintage has higher collector value
  return 4; // Very old cards have highest collector value
};

// Helper function to score popularity (based on card ID as a mock)
const getPopularityScore = (cardId) => {
  // Use card ID to generate a deterministic popularity score
  const seed = cardId.split('').reduce((acc, char) => acc + char.charCodeAt(0), 0);
  
  // Cards with certain names like "Charizard" would normally score higher
  const isPopular = cardId.toLowerCase().includes('charizard') || 
                   cardId.toLowerCase().includes('pikachu') ||
                   cardId.toLowerCase().includes('mew');
  
  let baseScore = (seed % 40) / 10 + 1; // 1-5 score
  
  // Popular Pokemon get a boost
  if (isPopular) {
    baseScore = Math.min(baseScore + 1.5, 5);
  }
  
  return parseFloat(baseScore.toFixed(1));
};

// Helper function to calculate confidence level
const getConfidenceLevel = (factors) => {
  // More consistent factors = higher confidence
  const values = Object.values(factors);
  const mean = values.reduce((a, b) => a + b, 0) / values.length;
  
  // Calculate variance
  const variance = values.reduce((acc, val) => acc + Math.pow(val - mean, 2), 0) / values.length;
  
  // Lower variance = higher confidence
  if (variance < 0.5) return 'High';
  if (variance < 1.0) return 'Medium';
  return 'Low';
};

// Export API service object
const apiService = {
  getCards,
  getCardById,
  getSets,
  getSetById,
  getTypes,
  getSubtypes,
  getRarities,
  getCardMarketPrices,
  getCardPriceHistory,
  getInvestmentPotentialRating
};

export default apiService;