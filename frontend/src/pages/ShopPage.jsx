import React, { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import apiService from '../services/api';
import CardGrid from '../components/CardGrid';
import CardFilters from '../components/CardFilters';
import { useFilters, usePagination } from '../utils/hooks';

export default function ShopPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [cards, setCards] = useState([]);
  const [totalCards, setTotalCards] = useState(0);
  const [loading, setLoading] = useState(true);
  const [sets, setSets] = useState([]);
  const [rarities, setRarities] = useState([]);
  const [types, setTypes] = useState([]);
  
  // Initialize filters from URL query params
  const initialFilters = {};
  for (const [key, value] of searchParams.entries()) {
    initialFilters[key] = value;
  }
  
  const { filters, setFilter, clearFilters, filtersToQueryString } = useFilters(initialFilters);
  
  // Pagination
  const { 
    currentPage, 
    totalPages, 
    goToPage, 
    nextPage, 
    prevPage,
    pageSize
  } = usePagination(totalCards, 24, parseInt(searchParams.get('page') || '1'));
  
  // Fetch cards with filters
  useEffect(() => {
    const fetchCards = async () => {
      try {
        setLoading(true);
        
        // Prepare API parameters
        const params = {
          page: currentPage,
          pageSize: pageSize,
          q: 'supertype:Pokémon' // Only show Pokémon cards by default
        };
        
        // Add filters
        if (filters.q) {
          params.q = `name:"${filters.q}*" supertype:Pokémon`;
        }
        
        if (filters.setId) {
          params.q += ` set.id:${filters.setId}`;
        }
        
        if (filters.rarity) {
          params.q += ` rarity:"${filters.rarity}"`;
        }
        
        if (filters.types) {
          params.q += ` types:${filters.types}`;
        }
        
        if (filters.supertype && filters.supertype.length > 0) {
          const supertypeQuery = filters.supertype
            .map(type => `supertype:${type}`)
            .join(' OR ');
          params.q += ` (${supertypeQuery})`;
        }
        
        if (filters.legalities && filters.legalities.length > 0) {
          const legalitiesQuery = filters.legalities
            .map(format => `legalities.${format}:legal`)
            .join(' OR ');
          params.q += ` (${legalitiesQuery})`;
        }
        
        // Add sort
        if (filters.sort) {
          params.orderBy = filters.sort;
        }
        
        const response = await apiService.getCards(params);
        setCards(response.data || []);
        setTotalCards(response.totalCount || 0);
      } catch (error) {
        console.error('Error fetching cards:', error);
      } finally {
        setLoading(false);
      }
    };
    
    fetchCards();
  }, [filters, currentPage, pageSize]);
  
  // Fetch filter options
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
  
  // Update URL when filters change
  useEffect(() => {
    const queryString = filtersToQueryString();
    
    // Add pagination
    const updatedParams = new URLSearchParams(queryString);
    if (currentPage > 1) {
      updatedParams.set('page', currentPage.toString());
    }
    
    setSearchParams(updatedParams);
  }, [filters, currentPage, setSearchParams, filtersToQueryString]);

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom">
        <h1 className="text-3xl md:text-4xl font-bold mb-8">Shop Pokémon Cards</h1>
        
        {/* Filters */}
        <CardFilters 
          filters={filters}
          setFilter={setFilter}
          clearFilters={clearFilters}
          sets={sets}
          rarities={rarities}
          types={types}
          loading={loading}
        />
        
        {/* Cards */}
        <div className="bg-white rounded-lg shadow-sm p-4 mb-8">
          <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center mb-6">
            <div className="mb-3 sm:mb-0">
              <h2 className="text-lg font-semibold">
                {loading ? 'Loading cards...' : `${totalCards.toLocaleString()} cards found`}
              </h2>
              {Object.keys(filters).length > 0 && !loading && (
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
      </div>
    </div>
  );
}