import React, { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { motion } from 'framer-motion';
import apiService from '../services/api';
import cartService from '../services/cartService';
import authService from '../services/authService';
import { formatPrice, formatRarity, formatDate, generateSellerData } from '../utils/formatters';
import { useLocalStorage } from '../utils/hooks';
import PriceChart from '../components/PriceChart';
import InvestmentAnalysis from '../components/InvestmentAnalysis';

export default function CardDetailPage() {
  const { cardId } = useParams();
  const [card, setCard] = useState(null);
  const [priceHistory, setPriceHistory] = useState(null);
  const [investmentData, setInvestmentData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [selectedImage, setSelectedImage] = useState('small');
  const [quantity, setQuantity] = useState(1);
  const [isWishlisted, setIsWishlisted] = useState(false);
  const [user] = useLocalStorage('pokemonTCGUser', null);
  const [cart, setCart] = useLocalStorage('pokemonTCGCart', cartService.getCart());
  const [sellerData, setSellerData] = useState([]);
  const [selectedSeller, setSelectedSeller] = useState(null);
  const [addedToCart, setAddedToCart] = useState(false);
  
  // Fetch card data
  useEffect(() => {
    const fetchCardData = async () => {
      try {
        setLoading(true);
        
        // Fetch card details, price history, and investment potential
        const [cardResponse, priceHistoryResponse, investmentResponse] = await Promise.all([
          apiService.getCardById(cardId),
          apiService.getCardPriceHistory(cardId),
          apiService.getInvestmentPotentialRating(cardId)
        ]);
        
        setCard(cardResponse.data);
        setPriceHistory(priceHistoryResponse);
        setInvestmentData(investmentResponse);
        
        // Generate seller data for this card
        setSellerData(generateSellerData(cardId));
        // Select lowest priced seller by default
        const sellers = generateSellerData(cardId);
        setSelectedSeller(sellers[0]);
        
        // Check if card is in user's wishlist
        if (user && user.wishlist) {
          setIsWishlisted(user.wishlist.includes(cardId));
        }
      } catch (error) {
        console.error('Error fetching card data:', error);
      } finally {
        setLoading(false);
      }
    };
    
    fetchCardData();
  }, [cardId, user]);
  
  // Handle adding to cart
  const handleAddToCart = () => {
    if (!card) return;
    
    const price = selectedSeller?.price || 
                 card.tcgplayer?.prices?.holofoil?.market || 
                 card.tcgplayer?.prices?.normal?.market || 
                 card.cardmarket?.prices?.averageSellPrice || 
                 9.99;
                 
    const updatedCart = cartService.addItemToCart(card, quantity, price);
    setCart(updatedCart);
    setAddedToCart(true);
    
    // Reset added to cart message after 3 seconds
    setTimeout(() => {
      setAddedToCart(false);
    }, 3000);
  };
  
  // Handle wishlist toggling
  const handleWishlistToggle = () => {
    if (!user) {
      // Redirect to login
      window.location.href = `/login?redirect=/cards/${cardId}`;
      return;
    }
    
    if (isWishlisted) {
      authService.removeFromWishlist(cardId);
    } else {
      authService.addToWishlist(cardId);
    }
    
    setIsWishlisted(!isWishlisted);
  };

  if (loading) {
    return (
      <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
        <div className="container-custom">
          <div className="bg-white rounded-lg shadow-sm p-4 md:p-8 animate-pulse">
            <div className="flex flex-col md:flex-row gap-8">
              <div className="md:w-1/2 lg:w-2/5">
                <div className="bg-gray-200 rounded-lg aspect-[2/3] w-full"></div>
              </div>
              <div className="md:w-1/2 lg:w-3/5 space-y-4">
                <div className="h-8 bg-gray-200 rounded w-3/4"></div>
                <div className="h-4 bg-gray-200 rounded w-1/2"></div>
                <div className="h-4 bg-gray-200 rounded w-1/3"></div>
                <div className="h-32 bg-gray-200 rounded w-full mt-6"></div>
                <div className="h-12 bg-gray-200 rounded w-full mt-6"></div>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (!card) {
    return (
      <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
        <div className="container-custom">
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <h1 className="text-2xl font-bold mb-4">Card Not Found</h1>
            <p className="mb-6">The card you're looking for doesn't exist or has been removed.</p>
            <Link to="/shop" className="btn-primary">
              Back to Shop
            </Link>
          </div>
        </div>
      </div>
    );
  }

  // Format rarity with badge styling
  const rarityInfo = formatRarity(card.rarity);
  
  // Base price (either from seller or card data)
  const price = selectedSeller?.price || 
               card.tcgplayer?.prices?.holofoil?.market || 
               card.tcgplayer?.prices?.normal?.market || 
               card.cardmarket?.prices?.averageSellPrice || 
               9.99;

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom">
        {/* Breadcrumbs */}
        <div className="mb-6">
          <nav className="flex">
            <ol className="flex items-center space-x-1 text-sm text-gray-500">
              <li>
                <Link to="/" className="hover:text-primary-600">Home</Link>
              </li>
              <li>
                <span className="mx-1">/</span>
              </li>
              <li>
                <Link to="/shop" className="hover:text-primary-600">Shop</Link>
              </li>
              <li>
                <span className="mx-1">/</span>
              </li>
              {card.set && (
                <>
                  <li>
                    <Link to={`/sets/${card.set.id}`} className="hover:text-primary-600">
                      {card.set.name}
                    </Link>
                  </li>
                  <li>
                    <span className="mx-1">/</span>
                  </li>
                </>
              )}
              <li className="font-medium text-gray-900 truncate max-w-[150px] sm:max-w-none">
                {card.name}
              </li>
            </ol>
          </nav>
        </div>
        
        {/* Card details card */}
        <div className="bg-white rounded-lg shadow-sm overflow-hidden mb-8">
          <div className="flex flex-col md:flex-row">
            {/* Card image */}
            <div className="md:w-1/2 lg:w-2/5 p-6 md:border-r border-gray-100">
              <div className="sticky top-6">
                <motion.div
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ duration: 0.5 }}
                >
                  <div className="card-3d-effect mx-auto max-w-xs">
                    <div className="card-inner">
                      <img 
                        src={card.images[selectedImage]} 
                        alt={card.name}
                        className="w-full rounded-lg"
                      />
                    </div>
                  </div>
                </motion.div>
                
                {/* Image selector */}
                <div className="mt-4 flex justify-center space-x-4">
                  <button
                    onClick={() => setSelectedImage('small')}
                    className={`p-2 rounded ${selectedImage === 'small' ? 'bg-gray-100' : ''}`}
                  >
                    Small
                  </button>
                  <button
                    onClick={() => setSelectedImage('large')}
                    className={`p-2 rounded ${selectedImage === 'large' ? 'bg-gray-100' : ''}`}
                  >
                    Large
                  </button>
                </div>
                
                {/* Set info */}
                {card.set && (
                  <div className="mt-6 text-center">
                    <Link 
                      to={`/sets/${card.set.id}`}
                      className="inline-block"
                    >
                      <img 
                        src={card.set.images.symbol} 
                        alt={card.set.name} 
                        className="h-8 mx-auto mb-2"
                      />
                      <h3 className="text-sm font-medium">{card.set.name}</h3>
                      <p className="text-xs text-gray-500">
                        {card.number}/{card.set.printedTotal}
                      </p>
                    </Link>
                  </div>
                )}
              </div>
            </div>
            
            {/* Card info and purchase options */}
            <div className="md:w-1/2 lg:w-3/5 p-6">
              <div>
                <div className="flex flex-wrap items-start justify-between gap-2 mb-2">
                  <h1 className="text-2xl sm:text-3xl font-bold">{card.name}</h1>
                  
                  <button
                    onClick={handleWishlistToggle}
                    className={`p-2 rounded-full ${
                      isWishlisted 
                        ? 'text-red-500 hover:bg-red-50' 
                        : 'text-gray-400 hover:bg-gray-50 hover:text-gray-700'
                    }`}
                    title={isWishlisted ? "Remove from Wishlist" : "Add to Wishlist"}
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-6 h-6">
                      <path d="m11.645 20.91-.007-.003-.022-.012a15.247 15.247 0 0 1-.383-.218 25.18 25.18 0 0 1-4.244-3.17C4.688 15.36 2.25 12.174 2.25 8.25 2.25 5.322 4.714 3 7.688 3A5.5 5.5 0 0 1 12 5.052 5.5 5.5 0 0 1 16.313 3c2.973 0 5.437 2.322 5.437 5.25 0 3.925-2.438 7.111-4.739 9.256a25.175 25.175 0 0 1-4.244 3.17 15.247 15.247 0 0 1-.383.219l-.022.012-.007.004-.003.001a.752.752 0 0 1-.704 0l-.003-.001Z" />
                    </svg>
                  </button>
                </div>
                
                <div className="flex flex-wrap gap-2 mb-4">
                  {/* Rarity badge */}
                  <span className={`badge ${rarityInfo.className}`}>
                    {rarityInfo.label}
                  </span>
                  
                  {/* Type badges */}
                  {card.types?.map(type => (
                    <span key={type} className="badge bg-gray-100 text-gray-800">
                      {type}
                    </span>
                  ))}
                  
                  {/* Regulation mark badge */}
                  {card.regulationMark && (
                    <span className="badge bg-gray-100 text-gray-800">
                      Regulation: {card.regulationMark}
                    </span>
                  )}
                </div>
                
                {/* Card description or flavor text */}
                {card.flavorText && (
                  <div className="bg-gray-50 p-3 rounded-lg italic text-gray-700 text-sm mb-6 border-l-4 border-gray-200">
                    {card.flavorText}
                  </div>
                )}
                
                {/* Card stats/details */}
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 mb-6">
                  {card.hp && (
                    <div>
                      <h4 className="text-sm font-medium text-gray-500">HP</h4>
                      <p>{card.hp}</p>
                    </div>
                  )}
                  
                  {card.artist && (
                    <div>
                      <h4 className="text-sm font-medium text-gray-500">Artist</h4>
                      <p>{card.artist}</p>
                    </div>
                  )}
                  
                  {card.set && card.set.releaseDate && (
                    <div>
                      <h4 className="text-sm font-medium text-gray-500">Release Date</h4>
                      <p>{formatDate(card.set.releaseDate)}</p>
                    </div>
                  )}
                </div>
                
                {/* Seller selection */}
                <div className="mb-6">
                  <h3 className="text-lg font-semibold mb-2">Purchase Options</h3>
                  
                  <div className="bg-gray-50 rounded-lg overflow-hidden">
                    {sellerData.map(seller => (
                      <div 
                        key={seller.id}
                        onClick={() => setSelectedSeller(seller)}
                        className={`p-4 cursor-pointer border-b border-gray-200 last:border-b-0 flex justify-between items-center ${
                          selectedSeller?.id === seller.id ? 'bg-primary-50' : ''
                        }`}
                      >
                        <div>
                          <div className="flex items-center mb-1">
                            <span className="font-medium">{seller.name}</span>
                            <div className="flex text-yellow-400 ml-2">
                              {[...Array(5)].map((_, i) => (
                                <svg key={i} xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className={`w-4 h-4 ${i < Math.floor(seller.rating) ? 'text-yellow-400' : 'text-gray-300'}`}>
                                  <path fillRule="evenodd" d="M10.788 3.21c.448-1.077 1.976-1.077 2.424 0l2.082 5.007 5.404.433c1.164.093 1.636 1.545.749 2.305l-4.117 3.527 1.257 5.273c.271 1.136-.964 2.033-1.96 1.425L12 18.354 7.373 21.18c-.996.608-2.231-.29-1.96-1.425l1.257-5.273-4.117-3.527c-.887-.76-.415-2.212.749-2.305l5.404-.433 2.082-5.006z" clipRule="evenodd" />
                                </svg>
                              ))}
                            </div>
                            <span className="text-xs text-gray-500 ml-1">({seller.sales.toLocaleString()} sales)</span>
                          </div>
                          <div className="flex items-center text-sm">
                            <span className="mr-2">Condition: <span className="font-medium">{seller.condition}</span></span>
                            <span className="text-green-600">{seller.inStock} in stock</span>
                          </div>
                        </div>
                        <div className="font-bold text-lg">{formatPrice(seller.price)}</div>
                      </div>
                    ))}
                  </div>
                </div>
                
                {/* Quantity and add to cart */}
                <div className="flex flex-col sm:flex-row gap-4 mb-4">
                  <div className="sm:w-1/3">
                    <label htmlFor="quantity" className="block text-sm font-medium text-gray-700 mb-1">
                      Quantity
                    </label>
                    <div className="flex">
                      <button
                        type="button"
                        onClick={() => setQuantity(Math.max(1, quantity - 1))}
                        className="px-3 py-2 border border-r-0 border-gray-300 rounded-l-md bg-gray-50 hover:bg-gray-100"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4">
                          <path d="M6.75 9.25a.75.75 0 000 1.5h6.5a.75.75 0 000-1.5h-6.5z" />
                        </svg>
                      </button>
                      <input
                        type="number"
                        id="quantity"
                        name="quantity"
                        min="1"
                        value={quantity}
                        onChange={(e) => setQuantity(Math.max(1, parseInt(e.target.value) || 1))}
                        className="py-2 px-3 block w-full border border-gray-300 text-center"
                      />
                      <button
                        type="button"
                        onClick={() => setQuantity(quantity + 1)}
                        className="px-3 py-2 border border-l-0 border-gray-300 rounded-r-md bg-gray-50 hover:bg-gray-100"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4">
                          <path d="M10.75 6.75a.75.75 0 00-1.5 0v2.5h-2.5a.75.75 0 000 1.5h2.5v2.5a.75.75 0 001.5 0v-2.5h2.5a.75.75 0 000-1.5h-2.5v-2.5z" />
                        </svg>
                      </button>
                    </div>
                  </div>
                  
                  <div className="flex-1">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Price
                    </label>
                    <div className="text-2xl font-bold text-primary-700">
                      {formatPrice(price * quantity)}
                    </div>
                  </div>
                </div>
                
                <div className="flex flex-col sm:flex-row gap-4">
                  <button
                    onClick={handleAddToCart}
                    className={`btn-primary py-3 flex-1 flex justify-center items-center ${addedToCart ? 'bg-green-600 hover:bg-green-700' : ''}`}
                  >
                    {addedToCart ? (
                      <>
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-5 h-5 mr-2">
                          <path fillRule="evenodd" d="M16.704 4.153a.75.75 0 01.143 1.052l-8 10.5a.75.75 0 01-1.127.075l-4.5-4.5a.75.75 0 011.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 011.05-.143z" clipRule="evenodd" />
                        </svg>
                        Added to Cart
                      </>
                    ) : (
                      <>
                        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 mr-2">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 3h1.386c.51 0 .955.343 1.087.835l.383 1.437M7.5 14.25a3 3 0 00-3 3h15.75m-12.75-3h11.218c1.121-2.3 2.1-4.684 2.924-7.138a60.114 60.114 0 00-16.536-1.84M7.5 14.25L5.106 5.272M6 20.25a.75.75 0 11-1.5 0 .75.75 0 011.5 0zm12.75 0a.75.75 0 11-1.5 0 .75.75 0 011.5 0z" />
                        </svg>
                        Add to Cart
                      </>
                    )}
                  </button>
                  
                  <button
                    onClick={handleWishlistToggle}
                    className={`py-3 px-6 rounded border font-medium transition-all duration-200 flex items-center justify-center ${
                      isWishlisted 
                        ? 'border-red-500 text-red-500 hover:bg-red-50' 
                        : 'border-gray-300 text-gray-700 hover:bg-gray-50'
                    }`}
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-5 h-5 mr-2">
                      <path d="m11.645 20.91-.007-.003-.022-.012a15.247 15.247 0 0 1-.383-.218 25.18 25.18 0 0 1-4.244-3.17C4.688 15.36 2.25 12.174 2.25 8.25 2.25 5.322 4.714 3 7.688 3A5.5 5.5 0 0 1 12 5.052 5.5 5.5 0 0 1 16.313 3c2.973 0 5.437 2.322 5.437 5.25 0 3.925-2.438 7.111-4.739 9.256a25.175 25.175 0 0 1-4.244 3.17 15.247 15.247 0 0 1-.383.219l-.022.012-.007.004-.003.001a.752.752 0 0 1-.704 0l-.003-.001Z" />
                    </svg>
                    {isWishlisted ? 'Wishlisted' : 'Add to Wishlist'}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
        
        {/* Price history and investment analysis */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 mb-8">
          <PriceChart 
            priceHistory={priceHistory?.priceHistory} 
            predictedPrices={priceHistory?.predictedPrices}
          />
          
          <InvestmentAnalysis investmentData={investmentData} />
        </div>
        
        {/* Card details and attacks */}
        {card.attacks && card.attacks.length > 0 && (
          <div className="bg-white rounded-lg shadow-sm p-6 mb-8">
            <h2 className="text-xl font-bold mb-4">Attacks & Abilities</h2>
            
            <div className="space-y-4">
              {card.attacks.map((attack, index) => (
                <div 
                  key={`${attack.name}-${index}`}
                  className="border border-gray-200 rounded-lg p-4"
                >
                  <div className="flex items-center justify-between mb-2">
                    <h3 className="font-bold">{attack.name}</h3>
                    {attack.damage && (
                      <span className="font-bold">{attack.damage}</span>
                    )}
                  </div>
                  
                  {attack.cost && (
                    <div className="flex items-center mb-2">
                      <span className="text-sm text-gray-500 mr-2">Cost:</span>
                      <div className="flex">
                        {attack.cost.map((type, i) => (
                          <span 
                            key={`${type}-${i}`}
                            className="inline-block w-6 h-6 mr-1 rounded-full bg-gray-200 text-xs flex items-center justify-center"
                            title={type}
                          >
                            {type.charAt(0)}
                          </span>
                        ))}
                      </div>
                    </div>
                  )}
                  
                  {attack.text && (
                    <p className="text-sm text-gray-700">{attack.text}</p>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}
        
        {/* Card legality */}
        {card.legalities && (
          <div className="bg-white rounded-lg shadow-sm p-6 mb-8">
            <h2 className="text-xl font-bold mb-4">Format Legality</h2>
            
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="border border-gray-200 rounded-lg p-4">
                <h3 className="font-medium mb-2">Standard</h3>
                <LegalityBadge status={card.legalities.standard} />
              </div>
              
              <div className="border border-gray-200 rounded-lg p-4">
                <h3 className="font-medium mb-2">Expanded</h3>
                <LegalityBadge status={card.legalities.expanded} />
              </div>
              
              <div className="border border-gray-200 rounded-lg p-4">
                <h3 className="font-medium mb-2">Unlimited</h3>
                <LegalityBadge status={card.legalities.unlimited} />
              </div>
            </div>
          </div>
        )}
        
        {/* Related cards */}
        {card.name && (
          <div className="bg-white rounded-lg shadow-sm p-6">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-xl font-bold">Similar Cards</h2>
              <Link 
                to={`/search?q=${encodeURIComponent(card.name.split(' ')[0])}`}
                className="text-primary-600 hover:text-primary-700 font-medium flex items-center"
              >
                View More
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 ml-1">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                </svg>
              </Link>
            </div>
            
            <div className="text-center py-8 text-gray-500">
              <p>Loading similar cards...</p>
              <p className="text-sm mt-2">Check back later for recommendations</p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

// Helper component for format legality
function LegalityBadge({ status }) {
  if (status === 'Legal') {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4 mr-1">
          <path fillRule="evenodd" d="M16.704 4.153a.75.75 0 01.143 1.052l-8 10.5a.75.75 0 01-1.127.075l-4.5-4.5a.75.75 0 011.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 011.05-.143z" clipRule="evenodd" />
        </svg>
        Legal
      </span>
    );
  }
  
  if (status === 'Banned') {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4 mr-1">
          <path fillRule="evenodd" d="M4 10a.75.75 0 01.75-.75h10.5a.75.75 0 010 1.5H4.75A.75.75 0 014 10z" clipRule="evenodd" />
        </svg>
        Banned
      </span>
    );
  }
  
  return (
    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
      Not Legal
    </span>
  );
}