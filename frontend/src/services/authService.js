// Auth Service - talks to the real ASP.NET Core backend (AuthController).
// The backend hashes passwords with BCrypt and issues a JWT; this module
// never stores a plaintext password anywhere, only the returned token and
// the public profile fields the API sends back.
import httpClient, { setToken } from './httpClient';

const USER_STORAGE_KEY = 'pokemonTCGUser';

const persistSession = (user, token) => {
  setToken(token);
  try {
    window.localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  } catch {
    // Non-fatal: the app still works for this session without persistence.
  }
};

const extractErrorMessage = (error, fallback) =>
  error?.response?.data?.message || fallback;

// Check if user is already logged in (based on the last profile we persisted)
export const checkAuth = () => {
  try {
    const user = window.localStorage.getItem(USER_STORAGE_KEY);
    return user ? JSON.parse(user) : null;
  } catch {
    return null;
  }
};

// Login user against the backend
export const login = async (email, password) => {
  try {
    const { data } = await httpClient.post('/auth/login', { email, password });
    const { token, ...user } = data;
    persistSession(user, token);
    return { success: true, user };
  } catch (error) {
    return { success: false, message: extractErrorMessage(error, 'Invalid email or password') };
  }
};

// Register a new user against the backend
export const register = async ({ username, email, password }) => {
  try {
    const { data } = await httpClient.post('/auth/register', { username, email, password });
    const { token, ...user } = data;
    persistSession(user, token);
    return { success: true, user };
  } catch (error) {
    return { success: false, message: extractErrorMessage(error, 'Registration failed') };
  }
};

// Logout user - clears both the JWT and the cached profile
export const logout = () => {
  setToken(null);
  try {
    window.localStorage.removeItem(USER_STORAGE_KEY);
  } catch {
    // ignore
  }
  return { success: true };
};

const authService = {
  checkAuth,
  login,
  register,
  logout,
};

export default authService;
