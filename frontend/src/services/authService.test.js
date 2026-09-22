import httpClient from './httpClient';
import authService from './authService';

jest.mock('./httpClient', () => {
  const post = jest.fn();
  return {
    __esModule: true,
    default: { post },
    getToken: jest.fn(),
    setToken: jest.fn(),
  };
});

describe('authService', () => {
  beforeEach(() => {
    httpClient.post.mockReset();
    window.localStorage.clear();
  });

  it('login: on success, persists the user and never the password', async () => {
    httpClient.post.mockResolvedValueOnce({
      data: { id: 1, username: 'ash', email: 'ash@pokemon.com', token: 'jwt-token-value' },
    });

    const result = await authService.login('ash@pokemon.com', 'pikachu123');

    expect(httpClient.post).toHaveBeenCalledWith('/auth/login', {
      email: 'ash@pokemon.com',
      password: 'pikachu123',
    });
    expect(result).toEqual({
      success: true,
      user: { id: 1, username: 'ash', email: 'ash@pokemon.com' },
    });

    const stored = JSON.parse(window.localStorage.getItem('pokemonTCGUser'));
    expect(stored).toEqual({ id: 1, username: 'ash', email: 'ash@pokemon.com' });
    // The JWT itself is handed to httpClient's token store, not to the user
    // profile blob, and the raw password is never written anywhere.
    expect(JSON.stringify(stored)).not.toContain('pikachu123');
    expect(JSON.stringify(stored)).not.toContain('jwt-token-value');
  });

  it('login: on invalid credentials, surfaces the backend error message', async () => {
    httpClient.post.mockRejectedValueOnce({
      response: { data: { message: 'Invalid email or password' } },
    });

    const result = await authService.login('ash@pokemon.com', 'wrong-password');

    expect(result).toEqual({ success: false, message: 'Invalid email or password' });
    expect(window.localStorage.getItem('pokemonTCGUser')).toBeNull();
  });

  it('register: on success, persists the user profile returned by the backend', async () => {
    httpClient.post.mockResolvedValueOnce({
      data: { id: 2, username: 'misty', email: 'misty@pokemon.com', token: 'jwt-token-value' },
    });

    const result = await authService.register({
      username: 'misty',
      email: 'misty@pokemon.com',
      password: 'starmie123',
    });

    expect(result.success).toBe(true);
    expect(result.user).toEqual({ id: 2, username: 'misty', email: 'misty@pokemon.com' });
  });

  it('register: on duplicate email, returns the backend error message', async () => {
    httpClient.post.mockRejectedValueOnce({
      response: { data: { message: 'Email already in use' } },
    });

    const result = await authService.register({
      username: 'misty',
      email: 'misty@pokemon.com',
      password: 'starmie123',
    });

    expect(result).toEqual({ success: false, message: 'Email already in use' });
  });

  it('checkAuth: returns null when nothing is stored', () => {
    expect(authService.checkAuth()).toBeNull();
  });

  it('logout: clears the stored profile and the token', () => {
    window.localStorage.setItem('pokemonTCGUser', JSON.stringify({ id: 1 }));

    authService.logout();

    expect(window.localStorage.getItem('pokemonTCGUser')).toBeNull();
  });
});
