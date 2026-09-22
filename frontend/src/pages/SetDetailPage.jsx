import React, { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import apiService from '../services/api';
import CardGrid from '../components/CardGrid';
import { formatDate } from '../utils/formatters';

export default function SetDetailPage() {
  const { setId } = useParams();
  const [set, setSet] = useState(null);
  const [cards, setCards] = useState([]);
  const [loading, setLoading] = useState(true);
  const [cardsLoading, setCardsLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('all');
  const [sortBy, setSortBy] = useState('number');
  
  // Fetch set data
  useEffect(() => {
    const fetchSetData = async () => {
      try {
        setLoading(true);
        const response = await apiService.getSetById(setId);
        setSet(response.data);
      } catch (error) {
        console.error(`Error fetching set with ID ${setId}:`, error);
      } finally {
        setLoading(false);
      }
    };
    
    fetchSetData();
  }, [setId]);
  
  // Fetch cards for this set
  useEffect(() => {
    const fetchSetCards = async () => {
      try {
        setCardsLoading(true);
        
        const params = {
          q: `set.id:${setId}`,
          orderBy: sortBy,
          pageSize: 200 // Get all cards in the set
        };
        
        const response = await apiService.getCards(params);
        setCards(response.data || []);
      } catch (error) {
        console.error(`Error fetching cards for set ${setId}:`, error);
      } finally {
        setCardsLoading(false);
      }
    };
    
    if (setId) {
      fetchSetCards();
    }
  }, [setId, sortBy]);
  
  // Filter cards based on active tab
  const filteredCards = cards.filter(card => {
    if (activeTab === 'all') return true;
    if (activeTab === 'pokemon') return card.supertype === 'Pokémon';
    if (activeTab === 'trainer') return card.supertype === 'Trainer';
    if (activeTab === 'energy') return card.supertype === 'Energy';
    
    // Filter by rarity if active tab is a rarity
    if (['common', 'uncommon', 'rare', 'holorare', 'ultrarare', 'secretrare'].includes(activeTab)) {
      const rarity = card.rarity?.toLowerCase().replace(/\s+/g, '');
      
      if (activeTab === 'common') return rarity?.includes('common');
      if (activeTab === 'uncommon') return rarity?.includes('uncommon');
      if (activeTab === 'rare') return rarity?.includes('rare') && !rarity?.includes('holo') && !rarity?.includes('ultra') && !rarity?.includes('secret');
      if (activeTab === 'holorare') return rarity?.includes('holo');
      if (activeTab === 'ultrarare') return rarity?.includes('ultra') || rarity?.includes('gx') || rarity?.includes('v');
      if (activeTab === 'secretrare') return rarity?.includes('secret') || rarity?.includes('rainbow') || rarity?.includes('shiny');
    }
    
    return true;
  });
  
  // Get card counts for each tab
  const cardCounts = {
    all: cards.length,
    pokemon: cards.filter(card => card.supertype === 'Pokémon').length,
    trainer: cards.filter(card => card.supertype === 'Trainer').length,
    energy: cards.filter(card => card.supertype === 'Energy').length,
    common: cards.filter(card => card.rarity?.toLowerCase().includes('common')).length,
    uncommon: cards.filter(card => card.rarity?.toLowerCase().includes('uncommon')).length,
    rare: cards.filter(card => {
      const rarity = card.rarity?.toLowerCase();
      return rarity?.includes('rare') && !rarity?.includes('holo') && !rarity?.includes('ultra') && !rarity?.includes('secret');
    }).length,
    holorare: cards.filter(card => card.rarity?.toLowerCase().includes('holo')).length,
    ultrarare: cards.filter(card => {
      const rarity = card.rarity?.toLowerCase();
      return rarity?.includes('ultra') || rarity?.includes('gx') || rarity?.includes('v');
    }).length,
    secretrare: cards.filter(card => {
      const rarity = card.rarity?.toLowerCase();
      return rarity?.includes('secret') || rarity?.includes('rainbow') || rarity?.includes('shiny');
    }).length
  };

  if (loading) {
    return (
      <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
        <div className="container-custom">
          <div className="bg-white rounded-lg shadow-sm p-6 animate-pulse">
            <div className="h-8 bg-gray-200 rounded w-1/3 mb-4"></div>
            <div className="h-40 bg-gray-200 rounded mb-4"></div>
            <div className="h-4 bg-gray-200 rounded w-1/2 mb-2"></div>
            <div className="h-4 bg-gray-200 rounded w-1/4 mb-4"></div>
            <div className="h-10 bg-gray-200 rounded"></div>
          </div>
        </div>
      </div>
    );
  }

  if (!set) {
    return (
      <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
        <div className="container-custom">
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <h1 className="text-2xl font-bold mb-4">Set Not Found</h1>
            <p className="mb-6">The Pokémon set you're looking for doesn't exist or has been removed.</p>
            <Link to="/sets" className="btn-primary">
              Back to Sets
            </Link>
          </div>
        </div>
      </div>
    );
  }

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
                <Link to="/sets" className="hover:text-primary-600">Sets</Link>
              </li>
              <li>
                <span className="mx-1">/</span>
              </li>
              <li className="font-medium text-gray-900">
                {set.name}
              </li>
            </ol>
          </nav>
        </div>
        
        {/* Set header */}
        <div className="bg-white rounded-lg shadow-sm overflow-hidden mb-8">
          <div className="relative h-48 sm:h-64 md:h-80 bg-gradient-to-r from-pokemon-pokeblue to-pokemon-blue flex items-center">
            <div className="absolute inset-0 opacity-10">
              <img 
                src={set.images.logo} 
                alt={set.name}
                className="w-full h-full object-cover"
              />
            </div>
            <div className="container-custom relative z-10 flex flex-col md:flex-row items-center">
              <img 
                src={set.images.logo} 
                alt={set.name}
                className="h-24 md:h-32 lg:h-40 object-contain mb-4 md:mb-0 md:mr-8"
              />
              
              <div className="text-center md:text-left text-white">
                <h1 className="text-3xl md:text-4xl font-bold mb-2">{set.name}</h1>
                <div className="flex flex-wrap justify-center md:justify-start gap-2 mb-3">
                  <span className="badge bg-white/20 backdrop-blur-sm">
                    {set.series}
                  </span>
                  <span className="badge bg-white/20 backdrop-blur-sm">
                    Released: {formatDate(set.releaseDate)}
                  </span>
                  <span className="badge bg-white/20 backdrop-blur-sm">
                    {set.printedTotal} Cards
                  </span>
                </div>
                <Link 
                  to={`/shop?setId=${set.id}`}
                  className="inline-block btn-primary mt-2"
                >
                  Shop {set.name} Cards
                </Link>
              </div>
            </div>
          </div>
        </div>
        
        {/* Tab navigation */}
        <div className="bg-white rounded-lg shadow-sm p-4 mb-6">
          <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
            <div className="overflow-x-auto pb-2 md:pb-0">
              <div className="flex space-x-2 min-w-max">
                <button
                  onClick={() => setActiveTab('all')}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    activeTab === 'all'
                      ? 'bg-primary-100 text-primary-800'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  All Cards ({cardCounts.all})
                </button>
                <button
                  onClick={() => setActiveTab('pokemon')}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    activeTab === 'pokemon'
                      ? 'bg-primary-100 text-primary-800'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  Pokémon ({cardCounts.pokemon})
                </button>
                <button
                  onClick={() => setActiveTab('trainer')}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    activeTab === 'trainer'
                      ? 'bg-primary-100 text-primary-800'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  Trainer ({cardCounts.trainer})
                </button>
                <button
                  onClick={() => setActiveTab('energy')}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    activeTab === 'energy'
                      ? 'bg-primary-100 text-primary-800'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  Energy ({cardCounts.energy})
                </button>
                <button
                  onClick={() => setActiveTab('holorare')}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    activeTab === 'holorare'
                      ? 'bg-primary-100 text-primary-800'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  Holo Rare ({cardCounts.holorare})
                </button>
                <button
                  onClick={() => setActiveTab('ultrarare')}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    activeTab === 'ultrarare'
                      ? 'bg-primary-100 text-primary-800'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  Ultra Rare ({cardCounts.ultrarare})
                </button>
              </div>
            </div>
            
            <div className="flex items-center">
              <label htmlFor="sort" className="text-sm font-medium text-gray-700 mr-2">
                Sort:
              </label>
              <select
                id="sort"
                value={sortBy}
                onChange={(e) => setSortBy(e.target.value)}
                className="text-sm border border-gray-300 rounded-md px-3 py-1"
              >
                <option value="number">Number (Low to High)</option>
                <option value="-number">Number (High to Low)</option>
                <option value="name">Name (A to Z)</option>
                <option value="-name">Name (Z to A)</option>
                <option value="rarity">Rarity (Common to Rare)</option>
                <option value="-rarity">Rarity (Rare to Common)</option>
              </select>
            </div>
          </div>
        </div>
        
        {/* Cards grid */}
        <div className="bg-white rounded-lg shadow-sm p-4 mb-8">
          <CardGrid cards={filteredCards} loading={cardsLoading} />
          
          {!cardsLoading && filteredCards.length === 0 && (
            <div className="text-center py-12">
              <h3 className="text-xl font-semibold text-gray-700">No cards found</h3>
              <p className="text-gray-500 mt-2">
                No {activeTab !== 'all' ? activeTab : ''} cards found in this set.
              </p>
              <button
                onClick={() => setActiveTab('all')}
                className="mt-4 btn-primary"
              >
                View All Cards
              </button>
            </div>
          )}
        </div>
        
        {/* Set information */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
          <div className="bg-white rounded-lg shadow-sm p-6">
            <h2 className="text-lg font-semibold mb-4">Set Information</h2>
            <div className="space-y-4">
              <div>
                <h3 className="text-sm font-medium text-gray-500">Series</h3>
                <p>{set.series}</p>
              </div>
              <div>
                <h3 className="text-sm font-medium text-gray-500">Release Date</h3>
                <p>{formatDate(set.releaseDate)}</p>
              </div>
              <div>
                <h3 className="text-sm font-medium text-gray-500">Total Cards</h3>
                <p>{set.printedTotal}</p>
              </div>
              {set.ptcgoCode && (
                <div>
                  <h3 className="text-sm font-medium text-gray-500">PTCGO Code</h3>
                  <p>{set.ptcgoCode}</p>
                </div>
              )}
            </div>
          </div>
          
          <div className="bg-white rounded-lg shadow-sm p-6">
            <h2 className="text-lg font-semibold mb-4">Card Breakdown</h2>
            <div className="space-y-2">
              <div className="flex justify-between">
                <span className="text-sm text-gray-600">Pokémon</span>
                <span className="font-medium">{cardCounts.pokemon}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-sm text-gray-600">Trainer</span>
                <span className="font-medium">{cardCounts.trainer}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-sm text-gray-600">Energy</span>
                <span className="font-medium">{cardCounts.energy}</span>
              </div>
              <div className="mt-4 pt-4 border-t border-gray-100">
                <h3 className="text-sm font-medium text-gray-500 mb-2">By Rarity</h3>
                <div className="space-y-2">
                  <div className="flex justify-between">
                    <span className="text-sm text-gray-600">Common</span>
                    <span className="font-medium">{cardCounts.common}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sm text-gray-600">Uncommon</span>
                    <span className="font-medium">{cardCounts.uncommon}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sm text-gray-600">Rare</span>
                    <span className="font-medium">{cardCounts.rare}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sm text-gray-600">Holo Rare</span>
                    <span className="font-medium">{cardCounts.holorare}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sm text-gray-600">Ultra Rare</span>
                    <span className="font-medium">{cardCounts.ultrarare}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sm text-gray-600">Secret Rare</span>
                    <span className="font-medium">{cardCounts.secretrare}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
          
          <div className="bg-white rounded-lg shadow-sm p-6">
            <h2 className="text-lg font-semibold mb-4">Market Value</h2>
            <div className="text-center py-8 text-gray-500">
              <p>Market value data is loading...</p>
              <p className="text-sm mt-2">Check back later for price information</p>
            </div>
          </div>
        </div>
        
        {/* Related sets */}
        <div className="bg-white rounded-lg shadow-sm p-6">
          <div className="flex justify-between items-center mb-4">
            <h2 className="text-lg font-semibold">Related Sets</h2>
            <Link 
              to="/sets"
              className="text-primary-600 hover:text-primary-700 font-medium flex items-center"
            >
              View All Sets
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 ml-1">
                <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
              </svg>
            </Link>
          </div>
          
          <div className="text-center py-8 text-gray-500">
            <p>Loading related sets...</p>
            <p className="text-sm mt-2">Check back later for recommendations</p>
          </div>
        </div>
      </div>
    </div>
  );
}