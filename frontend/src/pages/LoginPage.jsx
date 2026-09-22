import React, { useState, useEffect } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import authService from '../services/authService';
import { useForm, useLocalStorage } from '../utils/hooks';

export default function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [user, setUser] = useLocalStorage('pokemonTCGUser', null);
  const [errorMessage, setErrorMessage] = useState('');
  
  // Get redirect URL from query params
  const searchParams = new URLSearchParams(location.search);
  const redirectUrl = searchParams.get('redirect') || '/';
  
  // Check if user is already logged in
  useEffect(() => {
    if (user) {
      // Force a page reload to ensure user state is updated everywhere
      window.location.href = redirectUrl;
    }
  }, [user, redirectUrl]);
  
  // Form handling
  const initialValues = {
    email: '',
    password: '',
    rememberMe: false
  };
  
  const handleSubmit = async (values) => {
    try {
      setErrorMessage('');
      const result = await authService.login(values.email, values.password);
      
      if (result.success) {
        setUser(result.user);
        navigate(redirectUrl);
      } else {
        setErrorMessage(result.message || 'Invalid login credentials');
      }
    } catch (error) {
      setErrorMessage('An error occurred during login');
      console.error('Login error:', error);
    }
  };
  
  const { 
    values, 
    handleChange, 
    handleBlur, 
    handleSubmit: submitForm 
  } = useForm(initialValues, handleSubmit);

  return (
    <div className="bg-pokemon-background min-h-screen py-12">
      <div className="container-custom max-w-md">
        <div className="bg-white rounded-lg shadow-sm p-6">
          <div className="text-center mb-6">
            <Link to="/" className="inline-block">
              <img 
                src="https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/items/poke-ball.png" 
                alt="Pokemon TCG Marketplace" 
                className="w-12 h-12 mx-auto mb-2"
              />
            </Link>
            <h1 className="text-2xl font-bold">Sign in to your account</h1>
            <p className="text-gray-600 mt-2">
              Welcome back to the Pokémon TCG Marketplace
            </p>
          </div>
          
          {errorMessage && (
            <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded mb-6">
              {errorMessage}
            </div>
          )}
          
          <form onSubmit={submitForm}>
            <div className="mb-4">
              <label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-1">
                Email address
              </label>
              <input
                id="email"
                name="email"
                type="email"
                required
                value={values.email}
                onChange={handleChange}
                onBlur={handleBlur}
                className="input"
                placeholder="you@example.com"
              />
            </div>
            
            <div className="mb-6">
              <div className="flex justify-between items-center mb-1">
                <label htmlFor="password" className="block text-sm font-medium text-gray-700">
                  Password
                </label>
                <Link to="/forgot-password" className="text-sm text-primary-600 hover:text-primary-700">
                  Forgot password?
                </Link>
              </div>
              <input
                id="password"
                name="password"
                type="password"
                required
                value={values.password}
                onChange={handleChange}
                onBlur={handleBlur}
                className="input"
                placeholder="••••••••"
              />
            </div>
            
            <div className="flex items-center mb-6">
              <input
                id="rememberMe"
                name="rememberMe"
                type="checkbox"
                checked={values.rememberMe}
                onChange={handleChange}
                className="h-4 w-4 text-primary-600 border-gray-300 rounded"
              />
              <label htmlFor="rememberMe" className="ml-2 block text-sm text-gray-700">
                Remember me
              </label>
            </div>
            
            <button
              type="submit"
              className="btn-primary w-full py-2.5"
            >
              Sign in
            </button>
          </form>
          
          <div className="mt-6 pt-6 border-t border-gray-100 text-center">
            <p className="text-gray-600">
              Don't have an account?{' '}
              <Link to="/register" className="text-primary-600 hover:text-primary-700 font-medium">
                Sign up
              </Link>
            </p>
          </div>
          
          <div className="mt-6 text-center text-xs text-gray-500">
            <p>Demo Login:</p>
            <p>Email: ash@pokemon.com</p>
            <p>Password: pikachu123</p>
          </div>
        </div>
      </div>
    </div>
  );
}