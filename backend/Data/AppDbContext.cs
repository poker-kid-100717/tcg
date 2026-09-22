using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Models;

namespace PokemonTCG.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<WishlistItem> WishlistItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User
            modelBuilder.Entity<User>()
                .HasMany(u => u.Wishlist)
                .WithOne(w => w.User)
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Orders)
                .WithOne(o => o.User)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Order
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            context.Database.Migrate();

            // Look for existing users
            if (context.Users.Any())
            {
                return; // DB has been seeded
            }

            // Add sample users
            var users = new User[]
            {
                new User
                {
                    Username = "pokemaster",
                    Email = "ash@pokemon.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("pikachu123"),
                    Avatar = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/25.png"
                },
                new User
                {
                    Username = "deckbuilder",
                    Email = "gary@pokemon.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("blastoise456"),
                    Avatar = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/9.png"
                }
            };

            context.Users.AddRange(users);
            context.SaveChanges();
        }
    }
}