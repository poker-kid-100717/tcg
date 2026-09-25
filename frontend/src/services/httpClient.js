import axios from 'axios';

// Base URL of the ASP.NET Core backend (see backend/Properties/launchSettings.json).
// Same-origin by default: the Worker routes /api in production, and the Vite
// dev server proxies it to the local API.
const API_BASE_URL = import.meta.env.VITE_API_URL || '/api';

const TOKEN_STORAGE_KEY = 'pokemonTCGToken';

// The JWT is kept in a module-level variable so every request can read it
// synchronously without touching storage, and mirrored into localStorage so a
// page refresh doesn't log the user out. It is never written alongside a
// password - only the signed token itself is persisted.
let currentToken = null;
try {
  currentToken = window.localStorage.getItem(TOKEN_STORAGE_KEY);
} catch {
  // localStorage can throw in private-browsing / restricted contexts; fall
  // back to an in-memory-only token for the current session.
  currentToken = null;
}

export const getToken = () => currentToken;

export const setToken = (token) => {
  currentToken = token || null;
  try {
    if (token) {
      window.localStorage.setItem(TOKEN_STORAGE_KEY, token);
    } else {
      window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    }
  } catch {
    // Ignore storage failures - the in-memory token still works for this tab.
  }
};

const httpClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

httpClient.interceptors.request.use((config) => {
  if (currentToken) {
    config.headers.Authorization = `Bearer ${currentToken}`;
  }
  return config;
});

export default httpClient;
