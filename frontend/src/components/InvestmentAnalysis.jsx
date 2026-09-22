import React from 'react';
import { formatRatingAsStars } from '../utils/formatters';

export default function InvestmentAnalysis({ investmentData, className = '' }) {
  if (!investmentData) {
    return (
      <div className={`bg-white rounded-lg shadow-sm p-4 ${className}`}>
        <h3 className="text-lg font-semibold mb-2">Investment Potential</h3>
        <div className="flex items-center justify-center h-40 bg-gray-100 rounded-lg">
          <p className="text-gray-500">Investment analysis is not available for this card</p>
        </div>
      </div>
    );
  }

  const { rating, factors, confidenceLevel } = investmentData;
  const stars = formatRatingAsStars(rating);
  
  // Get color class based on rating
  const getRatingColorClass = (rating) => {
    if (rating >= 4.5) return 'text-green-600';
    if (rating >= 3.5) return 'text-blue-600';
    if (rating >= 2.5) return 'text-yellow-600';
    if (rating >= 1.5) return 'text-orange-500';
    return 'text-red-500';
  };

  // Get description based on rating
  const getRatingDescription = (rating) => {
    if (rating >= 4.5) return 'Excellent investment potential';
    if (rating >= 3.5) return 'Good investment potential';
    if (rating >= 2.5) return 'Moderate investment potential';
    if (rating >= 1.5) return 'Limited investment potential';
    return 'Poor investment potential';
  };

  // Get confidence level color
  const getConfidenceLevelColor = (level) => {
    switch (level) {
      case 'High': return 'bg-green-100 text-green-800';
      case 'Medium': return 'bg-yellow-100 text-yellow-800';
      case 'Low': return 'bg-red-100 text-red-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  };

  return (
    <div className={`bg-white rounded-lg shadow-sm p-4 ${className}`}>
      <div className="flex justify-between items-center mb-4">
        <h3 className="text-lg font-semibold">Investment Potential</h3>
        <span className={`text-sm font-medium px-2 py-1 rounded-full ${getConfidenceLevelColor(confidenceLevel)}`}>
          {confidenceLevel} Confidence
        </span>
      </div>

      {/* Star Rating */}
      <div className="flex items-center mb-4">
        <div className="flex mr-2">
          {/* Full stars */}
          {Array(stars.full).fill().map((_, i) => (
            <svg key={`full-${i}`} xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-6 h-6 text-yellow-400">
              <path fillRule="evenodd" d="M10.788 3.21c.448-1.077 1.976-1.077 2.424 0l2.082 5.007 5.404.433c1.164.093 1.636 1.545.749 2.305l-4.117 3.527 1.257 5.273c.271 1.136-.964 2.033-1.96 1.425L12 18.354 7.373 21.18c-.996.608-2.231-.29-1.96-1.425l1.257-5.273-4.117-3.527c-.887-.76-.415-2.212.749-2.305l5.404-.433 2.082-5.006z" clipRule="evenodd" />
            </svg>
          ))}
          
          {/* Half star */}
          {stars.half > 0 && (
            <div className="relative">
              <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-6 h-6 text-yellow-400">
                <path strokeLinecap="round" strokeLinejoin="round" d="M11.48 3.499a.562.562 0 011.04 0l2.125 5.111a.563.563 0 00.475.345l5.518.442c.499.04.701.663.321.988l-4.204 3.602a.563.563 0 00-.182.557l1.285 5.385a.562.562 0 01-.84.61l-4.725-2.885a.563.563 0 00-.586 0L6.982 20.54a.562.562 0 01-.84-.61l1.285-5.386a.562.562 0 00-.182-.557l-4.204-3.602a.563.563 0 01.321-.988l5.518-.442a.563.563 0 00.475-.345L11.48 3.5z" />
              </svg>
              <div className="absolute inset-0 overflow-hidden w-1/2">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-6 h-6 text-yellow-400">
                  <path fillRule="evenodd" d="M10.788 3.21c.448-1.077 1.976-1.077 2.424 0l2.082 5.007 5.404.433c1.164.093 1.636 1.545.749 2.305l-4.117 3.527 1.257 5.273c.271 1.136-.964 2.033-1.96 1.425L12 18.354 7.373 21.18c-.996.608-2.231-.29-1.96-1.425l1.257-5.273-4.117-3.527c-.887-.76-.415-2.212.749-2.305l5.404-.433 2.082-5.006z" clipRule="evenodd" />
                </svg>
              </div>
            </div>
          )}
          
          {/* Empty stars */}
          {Array(stars.empty).fill().map((_, i) => (
            <svg key={`empty-${i}`} xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-6 h-6 text-gray-300">
              <path strokeLinecap="round" strokeLinejoin="round" d="M11.48 3.499a.562.562 0 011.04 0l2.125 5.111a.563.563 0 00.475.345l5.518.442c.499.04.701.663.321.988l-4.204 3.602a.563.563 0 00-.182.557l1.285 5.385a.562.562 0 01-.84.61l-4.725-2.885a.563.563 0 00-.586 0L6.982 20.54a.562.562 0 01-.84-.61l1.285-5.386a.562.562 0 00-.182-.557l-4.204-3.602a.563.563 0 01.321-.988l5.518-.442a.563.563 0 00.475-.345L11.48 3.5z" />
            </svg>
          ))}
        </div>
        
        <span className={`text-lg font-bold ${getRatingColorClass(rating)}`}>
          {rating}
        </span>
        <span className="text-sm text-gray-500 ml-2">
          ({getRatingDescription(rating)})
        </span>
      </div>

      {/* Key Factors */}
      <div className="space-y-3">
        <h4 className="font-medium text-sm">Key Factors</h4>
        
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
          {/* Rarity Factor */}
          <div className="bg-gray-50 p-3 rounded">
            <div className="flex justify-between items-center mb-1">
              <span className="text-sm font-medium">Rarity</span>
              <span className="text-sm font-bold">{factors.rarity.toFixed(1)}/5</span>
            </div>
            <div className="w-full bg-gray-200 rounded-full h-1.5">
              <div
                className="bg-primary-600 h-1.5 rounded-full"
                style={{ width: `${(factors.rarity / 5) * 100}%` }}
              ></div>
            </div>
          </div>
          
          {/* Price Growth Factor */}
          <div className="bg-gray-50 p-3 rounded">
            <div className="flex justify-between items-center mb-1">
              <span className="text-sm font-medium">Price Growth</span>
              <span className="text-sm font-bold">{factors.priceGrowth.toFixed(1)}/5</span>
            </div>
            <div className="w-full bg-gray-200 rounded-full h-1.5">
              <div
                className="bg-green-500 h-1.5 rounded-full"
                style={{ width: `${(factors.priceGrowth / 5) * 100}%` }}
              ></div>
            </div>
          </div>
          
          {/* Set Rotation Factor */}
          <div className="bg-gray-50 p-3 rounded">
            <div className="flex justify-between items-center mb-1">
              <span className="text-sm font-medium">Set Rotation</span>
              <span className="text-sm font-bold">{factors.setRotation.toFixed(1)}/5</span>
            </div>
            <div className="w-full bg-gray-200 rounded-full h-1.5">
              <div
                className="bg-blue-500 h-1.5 rounded-full"
                style={{ width: `${(factors.setRotation / 5) * 100}%` }}
              ></div>
            </div>
          </div>
          
          {/* Popularity Factor */}
          <div className="bg-gray-50 p-3 rounded">
            <div className="flex justify-between items-center mb-1">
              <span className="text-sm font-medium">Popularity</span>
              <span className="text-sm font-bold">{factors.popularity.toFixed(1)}/5</span>
            </div>
            <div className="w-full bg-gray-200 rounded-full h-1.5">
              <div
                className="bg-purple-500 h-1.5 rounded-full"
                style={{ width: `${(factors.popularity / 5) * 100}%` }}
              ></div>
            </div>
          </div>
        </div>
      </div>

      {/* Investment Recommendations */}
      <div className="mt-4 pt-3 border-t border-gray-100">
        <h4 className="font-medium text-sm mb-2">Investment Recommendations</h4>
        <ul className="text-sm text-gray-700 space-y-1 list-disc list-inside">
          {rating >= 4 && (
            <li>Consider long-term hold for potential appreciation</li>
          )}
          {rating >= 3.5 && (
            <li>Card shows strong collector appeal and market demand</li>
          )}
          {rating < 3 && (
            <li>May have limited investment upside compared to other options</li>
          )}
          {factors.rarity >= 4 && (
            <li>High rarity increases collectibility and value retention</li>
          )}
          {factors.priceGrowth >= 4 && (
            <li>Consistent price appreciation trend indicates growing demand</li>
          )}
          {factors.popularity >= 4 && (
            <li>Popular character/card with sustained player interest</li>
          )}
          {factors.setRotation <= 2 && (
            <li>Set rotation could impact playability and short-term value</li>
          )}
        </ul>
      </div>
      
      <div className="mt-4 pt-3 border-t border-gray-100 text-xs text-gray-500">
        Analysis based on historical data, market trends, and card attributes. Not financial advice.
      </div>
    </div>
  );
}