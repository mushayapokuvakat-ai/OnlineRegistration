using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OnlineRegistrationSystem.Services
{
    /// <summary>
    /// Generates and validates JWT tokens for authentication.
    /// </summary>
    public class JwtService
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationMinutes;

        public JwtService(IConfiguration config)
        {
            _secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");
            _issuer = config["Jwt:Issuer"] ?? "OnlineRegistrationSystem";
            _audience = config["Jwt:Audience"] ?? "OnlineRegistrationSystem";
            _expirationMinutes = int.Parse(config["Jwt:ExpirationMinutes"] ?? "60");
        }

        /// <summary>
        /// Generates a JWT token for the authenticated user.
        /// </summary>
        public string GenerateToken(int userId, string email, string fullName, string role)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Name, fullName),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
