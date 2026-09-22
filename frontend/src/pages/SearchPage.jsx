import React, { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import apiService from '../services/api';
import CardGrid from '../components/CardGrid';
import CardFilters from '../components/CardFilters';
import { usePagination } from '../utils/hooks';

export default function SearchPage() {
  const [searchParams] = useSearchParams();
  const searchQuery = searchParams.get('q') || '';
  
  const [cards, setCards] = useState([]);
  const [totalCards, setTotalCards] = useState(0);
  const [loading, setLoading] = useState(true);
  const [sets, setSets] = useState([]);
  const [rarities, setRarities] = useState([]);
  const [types, setTypes] = useState([]);
  const [activeFilters, setActiveFilters] = useState({});
  
  // Pagination
  const { 
    currentPage, 
    totalPages, 
    goToPage, 
    nextPage, 
    prevPage,
    pageSize
  } = usePagination(totalCards, 24, parseInt(searchParams.get('page') || '1'));
  
  // Get filter options
  useEffect(() => {
    const fetchFilterOptions = async () => {
      try {
        const [setsResponse, raritiesResponse, typesResponse] = await Promise.all([
          apiService.getSets(),
          apiService.getRarities(),
          apiService.getTypes()
        ]);
        
        setSets(setsResponse.data || []);
        setRarities(raritiesResponse.data || []);
        setTypes(typesResponse.data || []);
      } catch (error) {
        console.error('Error fetching filter options:', error);
      }
    };
    
    fetchFilterOptions();
  }, []);
  
  // Search for cards
  useEffect(() => {
    const searchCards = async () => {
      if (!searchQuery) return;
      
      try {
        setLoading(true);
        
        // Prepare API parameters
        const params = {
          q: `name:"${searchQuery}*"`,  // Search name field with wildcard
          page: currentPage,
          pageSize: pageSize
        };
        
        // Add filters
        if (activeFilters.setId) {
          params.q += ` set.id:${activeFilters.setId}`;
        }
        
        if (activeFilters.rarity) {
          params.q += ` rarity:"${activeFilters.rarity}"`;
        }
        
        if (activeFilters.types) {
          params.q += ` types:${activeFilters.types}`;
        }
        
        // Add sort
        if (activeFilters.sort) {
          params.orderBy = activeFilters.sort;
        }
        
        const response = await apiService.getCards(params);
        setCards(response.data || []);
        setTotalCards(response.totalCount || 0);
      } catch (error) {
        console.error('Error searching cards:', error);
      } finally {
        setLoading(false);
      }
    };
    
    if (searchQuery) {
      searchCards();
    }
  }, [searchQuery, currentPage, pageSize, activeFilters]);
  
  // Handle filter changes
  const handleFilterChange = (filterName, value) => {
    setActiveFilters(prev => ({
      ...prev,
      [filterName]: value
    }));
  };
  
  // Clear all filters
  const handleClearFilters = () => {
    setActiveFilters({});
  };

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom">
        <h1 className="text-3xl md:text-4xl font-bold mb-2">Search Results</h1>
        <p className="text-gray-600 mb-8">
          {searchQuery ? (
            <>
              Showing results for "<span className="font-medium">{searchQuery}</span>"
            </>
          ) : (
            'Enter a search term to find Pokémon cards'
          )}
        </p>
        
        {/* Filters */}
        <CardFilters 
          filters={activeFilters}
          setFilter={handleFilterChange}
          clearFilters={handleClearFilters}
          sets={sets}
          rarities={rarities}
          types={types}
          loading={loading}
        />
        
        {/* No search query message */}
        {!searchQuery && (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <div className="text-gray-400 mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-16 h-16 mx-auto">
                <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
              </svg>
            </div>
            <h2 className="text-2xl font-semibold mb-4">Search for Pokémon Cards</h2>
            <p className="text-gray-600 mb-6">
              Enter a search term in the search box above to find your favorite Pokémon cards.
            </p>
            <p className="text-gray-500">
              You can search by card name, Pokémon name, set name, or card text.
            </p>
          </div>
        )}
        
        {/* Search results */}
        {searchQuery && (
          <div className="bg-white rounded-lg shadow-sm p-4 mb-8">
            <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center mb-6">
              <div className="mb-3 sm:mb-0">
                <h2 className="text-lg font-semibold">
                  {loading ? 'Loading cards...' : `${totalCards.toLocaleString()} cards found`}
                </h2>
                {Object.keys(activeFilters).length > 0 && !loading && (
                  <p className="text-sm text-gray-500">Filtered results</p>
                )}
              </div>
              
              <div className="flex items-center space-x-1">
                <button
                  onClick={prevPage}
                  disabled={currentPage === 1 || loading}
                  className={`p-2 rounded ${
                    currentPage === 1 || loading
                      ? 'text-gray-300 cursor-not-allowed'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
                  </svg>
                </button>
                
                <span className="text-sm">
                  Page {currentPage} of {totalPages || 1}
                </span>
                
                <button
                  onClick={nextPage}
                  disabled={currentPage >= totalPages || loading}
                  className={`p-2 rounded ${
                    currentPage >= totalPages || loading
                      ? 'text-gray-300 cursor-not-allowed'
                      : 'text-gray-700 hover:bg-gray-100'
                  }`}
                >
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5">
                    <path strokeLinecap="round" strokeLinejoin="round" d="m8.25 4.5 7.5 7.5-7.5 7.5" />
                  </svg>
                </button>
              </div>
            </div>
            
            <CardGrid cards={cards} loading={loading} />
            
            {/* Pagination */}
            {totalPages > 1 && !loading && (
              <div className="mt-8 flex justify-center">
                <nav className="flex items-center space-x-1">
                  <button
                    onClick={prevPage}
                    disabled={currentPage === 1}
                    className={`p-2 rounded ${
                      currentPage === 1
                        ? 'text-gray-300 cursor-not-allowed'
                        : 'text-gray-700 hover:bg-gray-100'
                    }`}
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
                    </svg>
                  </button>
                  
                  {/* Page numbers */}
                  {Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
                    // Show pages around current page
                    let pageNum;
                    if (totalPages <= 5) {
                      pageNum = i + 1;
                    } else if (currentPage <= 3) {
                      pageNum = i + 1;
                    } else if (currentPage >= totalPages - 2) {
                      pageNum = totalPages - 4 + i;
                    } else {
                      pageNum = currentPage - 2 + i;
                    }
                    
                    return (
                      <button
                        key={pageNum}
                        onClick={() => goToPage(pageNum)}
                        className={`w-10 h-10 rounded ${
                          currentPage === pageNum
                            ? 'bg-primary-600 text-white'
                            : 'text-gray-700 hover:bg-gray-100'
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}
                  
                  <button
                    onClick={nextPage}
                    disabled={currentPage >= totalPages}
                    className={`p-2 rounded ${
                      currentPage >= totalPages
                        ? 'text-gray-300 cursor-not-allowed'
                        : 'text-gray-700 hover:bg-gray-100'
                    }`}
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m8.25 4.5 7.5 7.5-7.5 7.5" />
                    </svg>
                  </button>
                </nav>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}