using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Models;

namespace PokemonTCG.API.Services
{
    public interface IUserService
    {
        Task<User?> GetByIdAsync(int id);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByUsernameAsync(string username);
        Task<bool> ValidatePasswordAsync(User user, string password);
        Task<User> RegisterAsync(string username, string email, string password);
        Task<List<WishlistItem>> GetWishlistAsync(int userId);
        Task<WishlistItem> AddToWishlistAsync(int userId, string cardId);
        Task<bool> RemoveFromWishlistAsync(int userId, string cardId);
        Task<List<Order>> GetOrdersAsync(int userId);
        Task<Order> CreateOrderAsync(int userId, decimal total, List<OrderItem> items);
    }

    public class UserService : IUserService
    {
        private readonly AppDbContext _context;

        public UserService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<User?> GetByIdAsync(int id)
        {
            return await _context.Users.FindAsync(id);
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        public Task<bool> ValidatePasswordAsync(User user, string password)
        {
            return Task.FromResult(BCrypt.Net.BCrypt.Verify(password, user.PasswordHash));
        }

        public async Task<User> RegisterAsync(string username, string email, string password)
        {
            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Avatar = $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{new Random().Next(1, 152)}.png"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task<List<WishlistItem>> GetWishlistAsync(int userId)
        {
            return await _context.WishlistItems
                .Where(w => w.UserId == userId)
                .ToListAsync();
        }

        public async Task<WishlistItem> AddToWishlistAsync(int userId, string cardId)
        {
            var existingItem = await _context.WishlistItems
                .FirstOrDefaultAsync(w => w.UserId == userId && w.CardId == cardId);

            if (existingItem != null)
            {
                return existingItem;
            }

            var wishlistItem = new WishlistItem
            {
                UserId = userId,
                CardId = cardId,
                AddedAt = DateTime.UtcNow
            };

            _context.WishlistItems.Add(wishlistItem);
            await _context.SaveChangesAsync();

            return wishlistItem;
        }

        public async Task<bool> RemoveFromWishlistAsync(int userId, string cardId)
        {
            var wishlistItem = await _context.WishlistItems
                .FirstOrDefaultAsync(w => w.UserId == userId && w.CardId == cardId);

            if (wishlistItem == null)
            {
                return false;
            }

            _context.WishlistItems.Remove(wishlistItem);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<List<Order>> GetOrdersAsync(int userId)
        {
            return await _context.Orders
                .Include(o => o.Items)
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<Order> CreateOrderAsync(int userId, decimal total, List<OrderItem> items)
        {
            var order = new Order
            {
                UserId = userId,
                Total = total,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                Items = items
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return order;
        }
    }
}