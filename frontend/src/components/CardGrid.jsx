import React from 'react';
import { Link } from 'react-router-dom';
import { motion } from 'framer-motion';
import { formatPrice, formatRarity } from '../utils/formatters';

export default function CardGrid({ cards, loading = false }) {
  // If loading, show skeleton loaders
  if (loading) {
    return (
      <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4">
        {Array(12).fill().map((_, index) => (
          <div key={index} className="card animate-pulse">
            <div className="aspect-[2/3] bg-gray-200 rounded"></div>
            <div className="p-3 space-y-2">
              <div className="h-4 bg-gray-200 rounded w-3/4"></div>
              <div className="h-4 bg-gray-200 rounded w-1/2"></div>
              <div className="flex justify-between items-center mt-2">
                <div className="h-6 bg-gray-200 rounded w-1/3"></div>
                <div className="h-6 bg-gray-200 rounded w-1/4"></div>
              </div>
            </div>
          </div>
        ))}
      </div>
    );
  }

  // If no cards, show empty state
  if (!cards || cards.length === 0) {
    return (
      <div className="text-center py-12">
        <div className="text-gray-400 mb-4">
          <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-16 h-16 mx-auto">
            <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
          </svg>
        </div>
        <h3 className="text-xl font-semibold text-gray-700">No cards found</h3>
        <p className="text-gray-500 mt-2">Try adjusting your filters or search criteria.</p>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4">
      {cards.map((card, index) => (
        <CardItem key={card.id} card={card} index={index} />
      ))}
    </div>
  );
}

// Individual card component
function CardItem({ card, index }) {
  const rarityInfo = formatRarity(card.rarity);
  
  // Animation variants for staggered grid animation
  const cardVariants = {
    hidden: { opacity: 0, y: 20 },
    visible: (i) => ({
      opacity: 1,
      y: 0,
      transition: {
        delay: i * 0.05,
        duration: 0.3,
        ease: "easeOut"
      }
    })
  };
  
  // Base price calculation (using tcgplayer data if available or a fallback)
  const price = card.tcgplayer?.prices?.holofoil?.market || 
                card.tcgplayer?.prices?.normal?.market || 
                card.cardmarket?.prices?.averageSellPrice || 
                (Math.random() * 50 + 1).toFixed(2);
  
  return (
    <motion.div
      custom={index}
      initial="hidden"
      animate="visible"
      variants={cardVariants}
      className="card card-hover card-3d-effect"
    >
      <Link to={`/cards/${card.id}`} className="block">
        <div className="card-inner">
          <img 
            src={card.images.small} 
            alt={card.name} 
            className="w-full"
            loading="lazy"
          />
          <div className="p-3">
            <h3 className="font-medium text-sm md:text-base line-clamp-1">{card.name}</h3>
            <p className="text-gray-500 text-xs md:text-sm line-clamp-1">{card.set.name}</p>
            
            <div className="flex justify-between items-center mt-2">
              <span className={`badge text-xs ${rarityInfo.className}`}>
                {rarityInfo.label}
              </span>
              <span className="font-semibold text-sm md:text-base">
                {formatPrice(price)}
              </span>
            </div>
          </div>
        </div>
      </Link>
    </motion.div>
  );
}