import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { motion } from 'framer-motion';
import apiService from '../services/api';
import { formatDate } from '../utils/formatters';

export default function SetsPage() {
  const [sets, setSets] = useState([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSeries, setSelectedSeries] = useState('');
  const [sortBy, setSortBy] = useState('releaseDate-desc');
  
  useEffect(() => {
    const fetchSets = async () => {
      try {
        setLoading(true);
        const response = await apiService.getSets();
        setSets(response.data || []);
      } catch (error) {
        console.error('Error fetching sets:', error);
      } finally {
        setLoading(false);
      }
    };
    
    fetchSets();
  }, []);
  
  // Get unique series from the sets
  const seriesList = Array.from(new Set(sets.map(set => set.series))).sort();
  
  // Filter sets based on search query and selected series
  const filteredSets = sets.filter(set => {
    const matchesSearch = set.name.toLowerCase().includes(searchQuery.toLowerCase());
    const matchesSeries = selectedSeries ? set.series === selectedSeries : true;
    return matchesSearch && matchesSeries;
  });
  
  // Sort sets based on the selected sort option
  const sortedSets = [...filteredSets].sort((a, b) => {
    const [field, direction] = sortBy.split('-');
    
    if (field === 'name') {
      return direction === 'asc' 
        ? a.name.localeCompare(b.name) 
        : b.name.localeCompare(a.name);
    }
    
    if (field === 'releaseDate') {
      return direction === 'asc' 
        ? new Date(a.releaseDate) - new Date(b.releaseDate) 
        : new Date(b.releaseDate) - new Date(a.releaseDate);
    }
    
    if (field === 'printedTotal') {
      return direction === 'asc' 
        ? a.printedTotal - b.printedTotal 
        : b.printedTotal - a.printedTotal;
    }
    
    return 0;
  });

  return (
    <div className="bg-pokemon-background min-h-screen py-6 md:py-12">
      <div className="container-custom">
        <h1 className="text-3xl md:text-4xl font-bold mb-8">Pokémon TCG Sets</h1>
        
        {/* Filters and search */}
        <div className="bg-white rounded-lg shadow-sm p-4 mb-8">
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {/* Search input */}
            <div>
              <label htmlFor="search" className="block text-sm font-medium text-gray-700 mb-1">
                Search Sets
              </label>
              <input
                type="text"
                id="search"
                placeholder="Search by set name..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="input"
              />
            </div>
            
            {/* Series filter */}
            <div>
              <label htmlFor="series" className="block text-sm font-medium text-gray-700 mb-1">
                Filter by Series
              </label>
              <select
                id="series"
                value={selectedSeries}
                onChange={(e) => setSelectedSeries(e.target.value)}
                className="input"
              >
                <option value="">All Series</option>
                {seriesList.map(series => (
                  <option key={series} value={series}>{series}</option>
                ))}
              </select>
            </div>
            
            {/* Sort options */}
            <div>
              <label htmlFor="sort" className="block text-sm font-medium text-gray-700 mb-1">
                Sort by
              </label>
              <select
                id="sort"
                value={sortBy}
                onChange={(e) => setSortBy(e.target.value)}
                className="input"
              >
                <option value="releaseDate-desc">Newest First</option>
                <option value="releaseDate-asc">Oldest First</option>
                <option value="name-asc">Name (A-Z)</option>
                <option value="name-desc">Name (Z-A)</option>
                <option value="printedTotal-desc">Most Cards</option>
                <option value="printedTotal-asc">Fewest Cards</option>
              </select>
            </div>
          </div>
        </div>
        
        {/* Sets grid */}
        {loading ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
            {Array(12).fill().map((_, index) => (
              <div key={index} className="bg-white rounded-lg shadow-sm overflow-hidden animate-pulse">
                <div className="h-40 bg-gray-200"></div>
                <div className="p-4 space-y-2">
                  <div className="h-6 bg-gray-200 rounded w-3/4"></div>
                  <div className="h-4 bg-gray-200 rounded w-1/2"></div>
                  <div className="h-4 bg-gray-200 rounded w-2/3"></div>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <>
            <div className="mb-4 text-gray-600">
              {filteredSets.length} {filteredSets.length === 1 ? 'set' : 'sets'} found
            </div>
            
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
              {sortedSets.map((set, index) => (
                <motion.div
                  key={set.id}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.3, delay: index * 0.05 }}
                >
                  <Link 
                    to={`/sets/${set.id}`}
                    className="block bg-white rounded-lg shadow-sm overflow-hidden transition-all duration-200 hover:shadow-md hover:-translate-y-1"
                  >
                    <div className="relative">
                      <div className="absolute top-2 right-2 z-10">
                        <span className="badge bg-pokemon-pokeblue text-white">
                          {set.series}
                        </span>
                      </div>
                      
                      <img 
                        src={set.images.logo} 
                        alt={set.name}
                        className="w-full h-40 object-contain p-4"
                      />
                      
                      <div className="absolute bottom-0 left-0 right-0 h-1/2 bg-gradient-to-t from-black/50 to-transparent"></div>
                    </div>
                    
                    <div className="p-4">
                      <h2 className="text-lg font-bold mb-1">{set.name}</h2>
                      <div className="flex justify-between items-center text-sm text-gray-600 mb-2">
                        <span>{set.printedTotal} Cards</span>
                        <span>{formatDate(set.releaseDate)}</span>
                      </div>
                      
                      <div className="flex items-center mt-3">
                        <div className="flex-1">
                          <div className="text-xs font-medium text-gray-500 mb-1">Completion</div>
                          <div className="w-full bg-gray-200 rounded-full h-1.5">
                            <div
                              className="bg-pokemon-red h-1.5 rounded-full"
                              style={{ width: `${Math.round(Math.random() * 100)}%` }}
                            ></div>
                          </div>
                        </div>
                      </div>
                    </div>
                  </Link>
                </motion.div>
              ))}
            </div>
          </>
        )}
        
        {!loading && filteredSets.length === 0 && (
          <div className="bg-white rounded-lg shadow-sm p-8 text-center">
            <h2 className="text-xl font-semibold mb-2">No sets found</h2>
            <p className="text-gray-600 mb-4">Try adjusting your search or filter criteria.</p>
            <button
              onClick={() => {
                setSearchQuery('');
                setSelectedSeries('');
              }}
              className="btn-primary"
            >
              Reset Filters
            </button>
          </div>
        )}
      </div>
    </div>
  );
}