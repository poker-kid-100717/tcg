import React, { useState } from 'react';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js';
import { Line } from 'react-chartjs-2';
import { formatPrice, calculatePriceTrend } from '../utils/formatters';

// Register ChartJS components
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
);

export default function PriceChart({ priceHistory, predictedPrices, className = '' }) {
  const [timeframe, setTimeframe] = useState('all'); // 'all', '6m', '3m', '1m'
  
  if (!priceHistory || priceHistory.length === 0) {
    return (
      <div className={`bg-white rounded-lg shadow-sm p-4 ${className}`}>
        <div className="flex justify-between items-center mb-4">
          <h3 className="text-lg font-semibold">Price History</h3>
          <div className="text-sm text-gray-500">No price data available</div>
        </div>
        <div className="bg-gray-100 rounded-lg flex items-center justify-center h-64">
          <p className="text-gray-500">Price history data not available for this card</p>
        </div>
      </div>
    );
  }
  
  // Calculate price trends
  const overallTrend = calculatePriceTrend(priceHistory);
  
  // Filter data based on timeframe
  const getFilteredData = () => {
    if (timeframe === 'all') {
      return { 
        historical: priceHistory, 
        predicted: predictedPrices 
      };
    }
    
    const now = new Date();
    let monthsAgo;
    
    switch (timeframe) {
      case '6m': monthsAgo = 6; break;
      case '3m': monthsAgo = 3; break;
      case '1m': default: monthsAgo = 1; break;
    }
    
    const cutoffDate = new Date();
    cutoffDate.setMonth(now.getMonth() - monthsAgo);
    
    const filtered = {
      historical: priceHistory.filter(data => new Date(data.date) >= cutoffDate),
      predicted: predictedPrices
    };
    
    return filtered;
  };
  
  const { historical, predicted } = getFilteredData();
  
  // Chart data
  const data = {
    labels: [...historical.map(d => d.date), ...predicted.map(d => d.date)],
    datasets: [
      {
        label: 'Market Price',
        data: [...historical.map(d => d.price), ...Array(predicted.length).fill(null)],
        borderColor: 'rgb(59, 130, 246)',
        backgroundColor: 'rgba(59, 130, 246, 0.1)',
        borderWidth: 2,
        pointBackgroundColor: 'rgb(59, 130, 246)',
        pointRadius: 3,
        pointHoverRadius: 5,
        fill: true,
        tension: 0.2
      },
      {
        label: 'Predicted Price',
        data: [...Array(historical.length).fill(null), ...predicted.map(d => d.price)],
        borderColor: 'rgba(234, 88, 12, 0.8)',
        borderWidth: 2,
        borderDash: [6, 4],
        pointBackgroundColor: 'rgba(234, 88, 12, 0.8)',
        pointRadius: 3,
        pointHoverRadius: 5,
        tension: 0.2
      }
    ]
  };
  
  // Chart options
  const options = {
    responsive: true,
    maintainAspectRatio: false,
    scales: {
      x: {
        grid: {
          display: false
        },
        ticks: {
          maxTicksLimit: 6,
          maxRotation: 0
        }
      },
      y: {
        grid: {
          borderDash: [2, 4],
          color: 'rgba(0, 0, 0, 0.06)'
        },
        ticks: {
          callback: function(value) {
            return '$' + value;
          }
        }
      }
    },
    plugins: {
      legend: {
        display: true,
        position: 'top',
        labels: {
          boxWidth: 12,
          usePointStyle: true,
          pointStyle: 'circle'
        }
      },
      tooltip: {
        mode: 'index',
        intersect: false,
        callbacks: {
          label: function(context) {
            let label = context.dataset.label || '';
            if (label) {
              label += ': ';
            }
            if (context.parsed.y !== null) {
              label += formatPrice(context.parsed.y);
            }
            return label;
          }
        }
      }
    }
  };

  // Time filter buttons
  const timeFilters = [
    { label: 'All Time', value: 'all' },
    { label: '6 Months', value: '6m' },
    { label: '3 Months', value: '3m' },
    { label: '1 Month', value: '1m' }
  ];
  
  return (
    <div className={`bg-white rounded-lg shadow-sm p-4 ${className}`}>
      <div className="flex flex-col sm:flex-row justify-between sm:items-center mb-2 space-y-2 sm:space-y-0">
        <h3 className="text-lg font-semibold">Price History & Prediction</h3>
        
        <div className="flex items-center space-x-1">
          {timeFilters.map(filter => (
            <button
              key={filter.value}
              onClick={() => setTimeframe(filter.value)}
              className={`px-2 py-1 text-xs rounded ${
                timeframe === filter.value
                  ? 'bg-primary-100 text-primary-800 font-medium'
                  : 'text-gray-600 hover:bg-gray-100'
              }`}
            >
              {filter.label}
            </button>
          ))}
        </div>
      </div>
      
      {/* Price trend indicators */}
      <div className="flex flex-wrap gap-2 mb-4">
        <div className={`text-sm rounded-full px-3 py-1 font-medium inline-flex items-center ${
          overallTrend.direction === 'up' 
            ? 'bg-green-100 text-green-800' 
            : overallTrend.direction === 'down' 
              ? 'bg-red-100 text-red-800'
              : 'bg-gray-100 text-gray-800'
        }`}>
          {overallTrend.direction === 'up' ? (
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4 mr-1">
              <path fillRule="evenodd" d="M10 17a.75.75 0 01-.75-.75V5.612L5.29 9.77a.75.75 0 01-1.08-1.04l5.25-5.5a.75.75 0 011.08 0l5.25 5.5a.75.75 0 11-1.08 1.04l-3.96-4.158V16.25A.75.75 0 0110 17z" clipRule="evenodd" />
            </svg>
          ) : overallTrend.direction === 'down' ? (
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4 mr-1">
              <path fillRule="evenodd" d="M10 3a.75.75 0 01.75.75v10.638l3.96-4.158a.75.75 0 111.08 1.04l-5.25 5.5a.75.75 0 01-1.08 0l-5.25-5.5a.75.75 0 111.08-1.04l3.96 4.158V3.75A.75.75 0 0110 3z" clipRule="evenodd" />
            </svg>
          ) : (
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4 mr-1">
              <path d="M4 10a.75.75 0 01.75-.75h10.5a.75.75 0 010 1.5H4.75A.75.75 0 014 10z" />
            </svg>
          )}
          {overallTrend.trend}% {overallTrend.direction === 'up' ? 'Increase' : overallTrend.direction === 'down' ? 'Decrease' : 'Change'}
        </div>
        
        <div className="text-sm rounded-full px-3 py-1 bg-gray-100 text-gray-800 inline-flex items-center">
          <span className="font-medium mr-1">Current:</span> {formatPrice(historical[historical.length - 1].price)}
        </div>
        
        <div className="text-sm rounded-full px-3 py-1 bg-orange-100 text-orange-800 inline-flex items-center">
          <span className="font-medium mr-1">Forecast (6m):</span> {formatPrice(predicted[predicted.length - 1].price)}
        </div>
      </div>
      
      {/* Chart container */}
      <div className="h-64 md:h-80">
        <Line data={data} options={options} />
      </div>
      
      <div className="mt-4 pt-3 border-t border-gray-100 text-xs text-gray-500">
        Predictions are based on historical data and market trends. Not financial advice.
      </div>
    </div>
  );
}