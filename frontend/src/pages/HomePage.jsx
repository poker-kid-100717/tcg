import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { motion } from 'framer-motion';
import apiService from '../services/api';
import CardGrid from '../components/CardGrid';

export default function HomePage() {
  const [featuredCards, setFeaturedCards] = useState([]);
  const [popularSets, setPopularSets] = useState([]);
  const [loading, setLoading] = useState(true);
  
  useEffect(() => {
    const fetchHomePageData = async () => {
      try {
        setLoading(true);
        
        // Fetch some featured cards (newest and popular)
        const cardsResponse = await apiService.getCards({ 
          pageSize: 12,
          q: 'supertype:Pokémon rarity:"Rare Holo"',  // Focus on holographic rares
          orderBy: '-set.releaseDate'
        });
        
        // Fetch popular sets
        const setsResponse = await apiService.getSets();
        
        setFeaturedCards(cardsResponse.data || []);
        
        // Sort sets by release date (newest first) and take the first 6
        const sortedSets = setsResponse.data
          ? [...setsResponse.data]
              .sort((a, b) => new Date(b.releaseDate) - new Date(a.releaseDate))
              .slice(0, 6)
          : [];
          
        setPopularSets(sortedSets);
      } catch (error) {
        console.error('Error fetching homepage data:', error);
      } finally {
        setLoading(false);
      }
    };
    
    fetchHomePageData();
  }, []);

  return (
    <div className="bg-pokemon-background pb-12">
      {/* Hero Banner */}
      <section className="bg-pokemon-pokeblue text-white py-12 md:py-20 relative overflow-hidden">
        <div className="absolute inset-0 z-0 opacity-15">
          <img 
            src="https://images.unsplash.com/photo-1613771404784-3a5684aa1aca" 
            alt="Pokemon Cards Collection" 
            className="w-full h-full object-cover"
          />
        </div>
        <div className="container-custom relative z-10">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-8 items-center">
            <motion.div
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.5 }}
            >
              <h1 className="text-4xl md:text-5xl lg:text-6xl font-bold mb-4">
                The Ultimate <span className="text-pokemon-yellow">Pokémon TCG</span> Marketplace
              </h1>
              <p className="text-lg md:text-xl opacity-90 mb-8">
                Buy, sell, and discover Pokémon cards from the world's largest trading card game community.
              </p>
              <div className="flex flex-col sm:flex-row gap-4">
                <Link to="/shop" className="btn-primary text-center py-3 px-8">
                  Shop Now
                </Link>
                <Link to="/sets" className="btn bg-white text-pokemon-pokeblue hover:bg-gray-100 text-center py-3 px-8">
                  Browse Sets
                </Link>
              </div>
            </motion.div>
            
            <motion.div
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              transition={{ duration: 0.5, delay: 0.2 }}
              className="hidden md:block"
            >
              <img 
                src="https://images.unsplash.com/photo-1605979257913-1704eb7b6246" 
                alt="Pokémon Cards" 
                className="w-full max-w-md mx-auto rounded-lg shadow-lg transform rotate-3"
              />
            </motion.div>
          </div>
        </div>
      </section>

      {/* Featured Cards */}
      <section className="py-12">
        <div className="container-custom">
          <div className="flex justify-between items-center mb-8">
            <h2 className="text-2xl md:text-3xl font-bold">Featured Cards</h2>
            <Link to="/shop" className="text-primary-600 hover:text-primary-700 font-medium flex items-center">
              View All
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 ml-1">
                <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
              </svg>
            </Link>
          </div>
          
          <CardGrid cards={featuredCards} loading={loading} />
        </div>
      </section>

      {/* Popular Sets */}
      <section className="py-12 bg-gray-50">
        <div className="container-custom">
          <div className="flex justify-between items-center mb-8">
            <h2 className="text-2xl md:text-3xl font-bold">Latest Sets</h2>
            <Link to="/sets" className="text-primary-600 hover:text-primary-700 font-medium flex items-center">
              View All Sets
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 ml-1">
                <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
              </svg>
            </Link>
          </div>
          
          {loading ? (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
              {Array(6).fill().map((_, index) => (
                <div key={index} className="bg-white rounded-lg shadow-sm p-4 animate-pulse">
                  <div className="w-full h-40 bg-gray-200 rounded mb-4"></div>
                  <div className="h-6 bg-gray-200 rounded w-3/4 mb-2"></div>
                  <div className="h-4 bg-gray-200 rounded w-1/2"></div>
                </div>
              ))}
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
              {popularSets.map((set, index) => (
                <motion.div
                  key={set.id}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.3, delay: index * 0.1 }}
                >
                  <Link 
                    to={`/sets/${set.id}`}
                    className="block bg-white rounded-lg shadow-sm overflow-hidden transition-all duration-200 hover:shadow-md hover:-translate-y-1"
                  >
                    <div className="relative">
                      <img 
                        src={set.images.logo} 
                        alt={set.name}
                        className="w-full h-40 object-contain p-4"
                      />
                      <div className="absolute inset-0 bg-gradient-to-t from-black/50 to-transparent flex items-end">
                        <div className="p-4 w-full">
                          <h3 className="text-white text-xl font-bold">{set.name}</h3>
                          <p className="text-white/80 text-sm">Released: {new Date(set.releaseDate).toLocaleDateString()}</p>
                        </div>
                      </div>
                    </div>
                    <div className="p-4 border-t border-gray-100">
                      <div className="flex justify-between items-center">
                        <span className="text-sm text-gray-500">{set.printedTotal} Cards</span>
                        <span className="badge bg-pokemon-pokeblue text-white">
                          {set.series}
                        </span>
                      </div>
                    </div>
                  </Link>
                </motion.div>
              ))}
            </div>
          )}
        </div>
      </section>

      {/* Investment Insights */}
      <section className="py-12">
        <div className="container-custom">
          <div className="bg-white rounded-xl shadow-sm overflow-hidden">
            <div className="grid grid-cols-1 md:grid-cols-2">
              <div className="p-8 md:p-12 flex flex-col justify-center">
                <span className="text-primary-600 font-semibold text-sm mb-2">EXCLUSIVE FEATURE</span>
                <h2 className="text-2xl md:text-3xl font-bold mb-4">Card Investment Analysis</h2>
                <p className="text-gray-600 mb-6">
                  Make informed investments in Pokémon cards with our exclusive price prediction and investment potential analysis. Our advanced algorithms analyze market trends, card rarity, and historical data to give you an edge.
                </p>
                <ul className="space-y-3 mb-6">
                  <li className="flex items-start">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 text-green-500 mr-2 mt-0.5">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                    </svg>
                    <span>Price history and future value predictions</span>
                  </li>
                  <li className="flex items-start">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 text-green-500 mr-2 mt-0.5">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                    </svg>
                    <span>Investment potential rating and analysis</span>
                  </li>
                  <li className="flex items-start">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 text-green-500 mr-2 mt-0.5">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                    </svg>
                    <span>Market trend indicators and insights</span>
                  </li>
                </ul>
                <Link to="/cards" className="btn-primary text-center py-3 max-w-xs">
                  Explore Cards
                </Link>
              </div>
              
              <div className="bg-gray-100 p-6 flex items-center">
                <img 
                  src="https://images.unsplash.com/photo-1628694647734-bf4aedeede1e" 
                  alt="Investment Analysis" 
                  className="w-full rounded-lg shadow-lg"
                />
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Collector Stats */}
      <section className="py-12 bg-pokemon-pokeblue text-white">
        <div className="container-custom">
          <h2 className="text-2xl md:text-3xl font-bold text-center mb-12">The World's Largest Pokémon TCG Marketplace</h2>
          
          <div className="grid grid-cols-2 md:grid-cols-4 gap-8">
            <div className="text-center">
              <div className="text-4xl md:text-5xl font-bold mb-2 text-pokemon-yellow">50M+</div>
              <p className="text-gray-300">Cards Available</p>
            </div>
            
            <div className="text-center">
              <div className="text-4xl md:text-5xl font-bold mb-2 text-pokemon-yellow">2M+</div>
              <p className="text-gray-300">Active Collectors</p>
            </div>
            
            <div className="text-center">
              <div className="text-4xl md:text-5xl font-bold mb-2 text-pokemon-yellow">10K+</div>
              <p className="text-gray-300">Daily Trades</p>
            </div>
            
            <div className="text-center">
              <div className="text-4xl md:text-5xl font-bold mb-2 text-pokemon-yellow">99%</div>
              <p className="text-gray-300">Satisfied Customers</p>
            </div>
          </div>
        </div>
      </section>

      {/* Newsletter */}
      <section className="py-12">
        <div className="container-custom">
          <div className="bg-gray-50 rounded-xl p-8 md:p-12">
            <div className="max-w-3xl mx-auto text-center">
              <h2 className="text-2xl md:text-3xl font-bold mb-4">Stay Updated on New Releases</h2>
              <p className="text-gray-600 mb-6">
                Subscribe to our newsletter to get the latest news, set releases, card additions, and exclusive deals.
              </p>
              
              <form className="flex flex-col sm:flex-row gap-3 max-w-xl mx-auto">
                <input
                  type="email"
                  placeholder="Your email address"
                  className="input flex-grow"
                  required
                />
                <button type="submit" className="btn-primary whitespace-nowrap">
                  Subscribe
                </button>
              </form>
              
              <p className="text-xs text-gray-500 mt-4">
                By subscribing, you agree to our Privacy Policy and consent to receive updates from our company.
              </p>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}