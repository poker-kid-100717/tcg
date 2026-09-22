import React, { useState, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import authService from '../services/authService';
import { useForm, useLocalStorage } from '../utils/hooks';

export default function RegisterPage() {
  const navigate = useNavigate();
  const [user, setUser] = useLocalStorage('pokemonTCGUser', null);
  const [errorMessage, setErrorMessage] = useState('');
  
  // Check if user is already logged in
  useEffect(() => {
    if (user) {
      navigate('/');
    }
  }, [user, navigate]);
  
  // Form handling
  const initialValues = {
    username: '',
    email: '',
    password: '',
    confirmPassword: '',
    agreeTerms: false
  };
  
  const validateForm = (values) => {
    const errors = {};
    
    if (!values.username) {
      errors.username = 'Username is required';
    }
    
    if (!values.email) {
      errors.email = 'Email is required';
    } else if (!/\S+@\S+\.\S+/.test(values.email)) {
      errors.email = 'Email is invalid';
    }
    
    if (!values.password) {
      errors.password = 'Password is required';
    } else if (values.password.length < 6) {
      errors.password = 'Password must be at least 6 characters';
    }
    
    if (values.password !== values.confirmPassword) {
      errors.confirmPassword = 'Passwords do not match';
    }
    
    if (!values.agreeTerms) {
      errors.agreeTerms = 'You must agree to the terms and conditions';
    }
    
    return errors;
  };
  
  const handleSubmit = async (values) => {
    try {
      setErrorMessage('');
      
      // Validate form
      const errors = validateForm(values);
      if (Object.keys(errors).length > 0) {
        setErrorMessage(Object.values(errors)[0]);
        return;
      }
      
      const result = await authService.register({
        username: values.username,
        email: values.email,
        password: values.password
      });
      
      if (result.success) {
        setUser(result.user);
        navigate('/');
      } else {
        setErrorMessage(result.message || 'Registration failed');
      }
    } catch (error) {
      setErrorMessage('An error occurred during registration');
      console.error('Registration error:', error);
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
            <h1 className="text-2xl font-bold">Create an account</h1>
            <p className="text-gray-600 mt-2">
              Join the Pokémon TCG Marketplace community
            </p>
          </div>
          
          {errorMessage && (
            <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded mb-6">
              {errorMessage}
            </div>
          )}
          
          <form onSubmit={submitForm}>
            <div className="mb-4">
              <label htmlFor="username" className="block text-sm font-medium text-gray-700 mb-1">
                Username
              </label>
              <input
                id="username"
                name="username"
                type="text"
                required
                value={values.username}
                onChange={handleChange}
                onBlur={handleBlur}
                className="input"
                placeholder="pokemontrainer"
              />
            </div>
            
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
            
            <div className="mb-4">
              <label htmlFor="password" className="block text-sm font-medium text-gray-700 mb-1">
                Password
              </label>
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
              <p className="text-xs text-gray-500 mt-1">
                Must be at least 6 characters
              </p>
            </div>
            
            <div className="mb-6">
              <label htmlFor="confirmPassword" className="block text-sm font-medium text-gray-700 mb-1">
                Confirm Password
              </label>
              <input
                id="confirmPassword"
                name="confirmPassword"
                type="password"
                required
                value={values.confirmPassword}
                onChange={handleChange}
                onBlur={handleBlur}
                className="input"
                placeholder="••••••••"
              />
            </div>
            
            <div className="flex items-center mb-6">
              <input
                id="agreeTerms"
                name="agreeTerms"
                type="checkbox"
                required
                checked={values.agreeTerms}
                onChange={handleChange}
                className="h-4 w-4 text-primary-600 border-gray-300 rounded"
              />
              <label htmlFor="agreeTerms" className="ml-2 block text-sm text-gray-700">
                I agree to the{' '}
                <Link to="/terms" className="text-primary-600 hover:text-primary-700">
                  Terms of Service
                </Link>
                {' '}and{' '}
                <Link to="/privacy" className="text-primary-600 hover:text-primary-700">
                  Privacy Policy
                </Link>
              </label>
            </div>
            
            <button
              type="submit"
              className="btn-primary w-full py-2.5"
            >
              Create account
            </button>
          </form>
          
          <div className="mt-6 pt-6 border-t border-gray-100 text-center">
            <p className="text-gray-600">
              Already have an account?{' '}
              <Link to="/login" className="text-primary-600 hover:text-primary-700 font-medium">
                Sign in
              </Link>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}