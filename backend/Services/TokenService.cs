using Microsoft.IdentityModel.Tokens;
using PokemonTCG.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PokemonTCG.API.Services
{
    public interface ITokenService
    {
        string GenerateJwtToken(User user);
        int? ValidateJwtToken(string token);
    }

    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyValue = _configuration["JwtSettings:Key"]
                ?? throw new InvalidOperationException("JwtSettings:Key is not configured.");
            var key = Encoding.ASCII.GetBytes(keyValue);
            var durationMinutes = double.TryParse(_configuration["JwtSettings:DurationInMinutes"], out var minutes)
                ? minutes
                : 60;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email)
                }),
                Expires = DateTime.UtcNow.AddMinutes(durationMinutes),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _configuration["JwtSettings:Issuer"],
                Audience = _configuration["JwtSettings:Audience"]
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public int? ValidateJwtToken(string token)
        {
            if (token == null)
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            var keyValue = _configuration["JwtSettings:Key"];
            if (string.IsNullOrEmpty(keyValue))
                return null;
            var key = Encoding.ASCII.GetBytes(keyValue);

            try
            {
                // ValidateToken maps the raw short JWT claim names (e.g. "nameid") back
                // onto the standard ClaimTypes used when the token was issued, so we read
                // the identifier from the returned ClaimsPrincipal rather than the raw
                // JwtSecurityToken payload, whose claim types are not yet mapped.
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["JwtSettings:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = _configuration["JwtSettings:Audience"],
                    // Set clockskew to zero so tokens expire exactly at token expiration time
                    ClockSkew = TimeSpan.Zero
                }, out _);

                var userId = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);

                return userId;
            }
            catch
            {
                // Return null if validation fails
                return null;
            }
        }
    }
}