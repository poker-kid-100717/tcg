# Pokémon TCG Marketplace

A full-stack application that replicates the functionality of TCGplayer.com, focused exclusively on Pokémon cards. This application allows users to browse, search, and view details of Pokémon cards, as well as simulated marketplace features.

## Features

- **Pokémon Card Browsing**: Browse and filter through thousands of Pokémon cards
- **Set Explorer**: View and browse all Pokémon card sets
- **Search Functionality**: Search for specific cards by name or other attributes
- **Card Details**: View detailed information about each card, including images and pricing
- **Price History**: View price history charts and trends for cards
- **Investment Analysis**: Get predictions on card value and investment potential
- **Shopping Cart**: Add cards to cart and manage quantities
- **User Authentication**: Sign in/sign up functionality with JWT authentication
- **Wishlist**: Save favorite cards to your wishlist
- **Order History**: View past orders and their details

## Technology Stack

### Frontend
- **Framework**: React.js with functional components and hooks
- **Styling**: TailwindCSS for responsive design
- **Animations**: Framer Motion for smooth animations
- **Charts**: Chart.js for price history visualization
- **Routing**: React Router for navigation

### Backend
- **.NET Core 8**: Latest version of the .NET framework
- **Entity Framework Core**: For database operations (In-Memory for this demo)
- **JWT Authentication**: For secure user authentication
- **RESTful API**: Clean API design following REST principles
- **Swagger**: API documentation

## Getting Started

### Prerequisites

- Node.js (v14+)
- .NET SDK 8.0
- npm or yarn

### Installation

1. Clone the repository

2. Set up the backend:
   ```
   cd backend
   dotnet restore
   dotnet run
   ```

3. Set up the frontend:
   ```
   cd frontend
   yarn install
   yarn start
   ```

4. Open http://localhost:3000 in your browser

## Demo Credentials

For testing the user authentication features, you can use the following credentials:

- **Email**: ash@pokemon.com
- **Password**: pikachu123

## API Integration

This application uses the [Pokémon TCG API](https://docs.pokemontcg.io/) for card data. The backend serves as a proxy to this API while adding additional features like:

- Caching for improved performance
- User authentication and authorization
- Wishlist management
- Order history
- Investment analysis and price prediction

## Deployment

### Frontend Deployment to Vercel

1. Create a Vercel account if you don't have one
2. Install the Vercel CLI:
   ```
   npm install -g vercel
   ```
3. Navigate to the frontend directory:
   ```
   cd frontend
   ```
4. Run the deployment command:
   ```
   vercel
   ```
5. Follow the prompts to deploy your application

### Backend Deployment to Azure

1. Create an Azure account if you don't have one
2. Install the Azure CLI
3. Login to Azure:
   ```
   az login
   ```
4. Create an App Service plan:
   ```
   az appservice plan create --name myPlan --resource-group myResourceGroup --sku FREE
   ```
5. Create a Web App:
   ```
   az webapp create --resource-group myResourceGroup --plan myPlan --name myApp --runtime "DOTNET|8.0"
   ```
6. Deploy your code:
   ```
   az webapp deployment source config-local-git --name myApp --resource-group myResourceGroup
   ```

## Architecture

The application follows a clean architecture pattern:

- **Controllers**: Handle HTTP requests and responses
- **Services**: Contain business logic and interact with external APIs
- **Models**: Define data structures
- **Data**: Handles database operations

Enjoy exploring the world of Pokémon TCG!