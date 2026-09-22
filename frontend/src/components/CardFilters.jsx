import React, { useState } from 'react';

export default function CardFilters({ 
  filters = {}, 
  setFilter,
  clearFilters,
  sets = [],
  rarities = [],
  types = [],
  loading = false
}) {
  const [isFiltersOpen, setIsFiltersOpen] = useState(false);
  
  // Handle checkbox change for multi-select filters
  const handleCheckboxChange = (filterName, value) => {
    // Get current values for this filter
    const currentValues = filters[filterName] || [];
    
    // Toggle the value
    if (currentValues.includes(value)) {
      setFilter(filterName, currentValues.filter(v => v !== value));
    } else {
      setFilter(filterName, [...currentValues, value]);
    }
  };

  // Handle sort change
  const handleSortChange = (e) => {
    setFilter('sort', e.target.value);
  };

  // Close filters panel on mobile when apply is clicked
  const applyFilters = () => {
    setIsFiltersOpen(false);
  };

  if (loading) {
    return (
      <div className="bg-white p-4 rounded-lg shadow-sm mb-6 animate-pulse">
        <div className="h-8 bg-gray-200 rounded mb-4 w-1/3"></div>
        <div className="h-6 bg-gray-200 rounded mb-2 w-1/2"></div>
        <div className="h-6 bg-gray-200 rounded mb-2 w-2/3"></div>
        <div className="h-6 bg-gray-200 rounded mb-4 w-1/2"></div>
        <div className="h-10 bg-gray-200 rounded"></div>
      </div>
    );
  }

  return (
    <div className="bg-white rounded-lg shadow-sm mb-6">
      {/* Mobile Filter Toggle */}
      <div className="md:hidden p-4 border-b border-gray-200">
        <button 
          onClick={() => setIsFiltersOpen(!isFiltersOpen)}
          className="w-full flex items-center justify-between bg-gray-100 p-2 rounded"
        >
          <span className="font-medium">Filters</span>
          <svg
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
            strokeWidth={1.5}
            stroke="currentColor"
            className={`w-5 h-5 transition-transform ${isFiltersOpen ? 'transform rotate-180' : ''}`}
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="m19.5 8.25-7.5 7.5-7.5-7.5" />
          </svg>
        </button>
      </div>

      {/* Desktop Filters always visible / Mobile Filters collapsed */}
      <div className={`${isFiltersOpen ? 'block' : 'hidden'} md:block p-4`}>
        <div className="flex flex-col md:flex-row md:items-center md:justify-between mb-4">
          <h2 className="text-lg font-semibold">Filter Cards</h2>
          <button 
            onClick={clearFilters}
            className="text-primary-600 hover:text-primary-800 text-sm font-medium"
          >
            Clear Filters
          </button>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {/* Set Filter */}
          <div>
            <label className="block font-medium mb-2">Set</label>
            <select 
              className="input"
              value={filters.setId || ''}
              onChange={(e) => setFilter('setId', e.target.value)}
            >
              <option value="">All Sets</option>
              {sets.map(set => (
                <option key={set.id} value={set.id}>
                  {set.name}
                </option>
              ))}
            </select>
          </div>

          {/* Rarity Filter */}
          <div>
            <label className="block font-medium mb-2">Rarity</label>
            <select 
              className="input"
              value={filters.rarity || ''}
              onChange={(e) => setFilter('rarity', e.target.value)}
            >
              <option value="">All Rarities</option>
              {rarities.map(rarity => (
                <option key={rarity} value={rarity}>
                  {rarity}
                </option>
              ))}
            </select>
          </div>

          {/* Type Filter */}
          <div>
            <label className="block font-medium mb-2">Type</label>
            <select 
              className="input"
              value={filters.types || ''}
              onChange={(e) => setFilter('types', e.target.value)}
            >
              <option value="">All Types</option>
              {types.map(type => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </div>

          {/* Sort Filter */}
          <div>
            <label className="block font-medium mb-2">Sort By</label>
            <select 
              className="input"
              value={filters.sort || 'name'}
              onChange={handleSortChange}
            >
              <option value="name">Name (A-Z)</option>
              <option value="-name">Name (Z-A)</option>
              <option value="set.releaseDate">Release Date (Oldest)</option>
              <option value="-set.releaseDate">Release Date (Newest)</option>
              <option value="number">Card Number (Low-High)</option>
              <option value="-number">Card Number (High-Low)</option>
              <option value="price">Price (Low-High)</option>
              <option value="-price">Price (High-Low)</option>
            </select>
          </div>
        </div>

        {/* Price Range Slider (could be added) */}

        {/* Additional Filters */}
        <div className="mt-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {/* Supertype Filter */}
          <div>
            <label className="block font-medium mb-2">Card Type</label>
            <div className="space-y-1">
              {['Pokémon', 'Trainer', 'Energy'].map(supertype => (
                <div key={supertype} className="flex items-center">
                  <input
                    id={`supertype-${supertype}`}
                    type="checkbox"
                    checked={(filters.supertype || []).includes(supertype)}
                    onChange={() => handleCheckboxChange('supertype', supertype)}
                    className="h-4 w-4 text-primary-600 border-gray-300 rounded"
                  />
                  <label htmlFor={`supertype-${supertype}`} className="ml-2 text-sm text-gray-700">
                    {supertype}
                  </label>
                </div>
              ))}
            </div>
          </div>

          {/* Legality Filter */}
          <div>
            <label className="block font-medium mb-2">Format Legality</label>
            <div className="space-y-1">
              {['standard', 'expanded', 'unlimited'].map(format => (
                <div key={format} className="flex items-center">
                  <input
                    id={`legality-${format}`}
                    type="checkbox"
                    checked={(filters.legalities || []).includes(format)}
                    onChange={() => handleCheckboxChange('legalities', format)}
                    className="h-4 w-4 text-primary-600 border-gray-300 rounded"
                  />
                  <label htmlFor={`legality-${format}`} className="ml-2 text-sm text-gray-700 capitalize">
                    {format}
                  </label>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Apply Button (Mobile Only) */}
        <div className="mt-6 md:hidden">
          <button
            onClick={applyFilters}
            className="btn-primary w-full"
          >
            Apply Filters
          </button>
        </div>
      </div>
    </div>
  );
}