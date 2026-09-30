using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using ReactServerSide.DAL;

namespace ReactServerSide.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly DBServices _db;
        private readonly IConfiguration _configuration;

        public AuthController(DBServices db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "יש להזין אימייל וסיסמה." });
            }

            string normalizedEmail = System.Text.RegularExpressions.Regex.Replace(request.Email, @"[\u200B-\u200D\u200E\u200F\uFEFF]", "").Trim().ToLowerInvariant();

            AuthenticatedUser? user = _db.PostAuthenticateUser(normalizedEmail, request.Password);
            if (user != null)
            {
                (string token, DateTime expiresAtUtc, int expiresInMinutes) = CreateJwtToken(user);

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    role = user.Role,
                    userType = user.UserType,
                    fullName = user.FullName,
                    token,
                    tokenType = "Bearer",
                    tokenExpiresInMinutes = expiresInMinutes,
                    tokenExpiresAtUtc = expiresAtUtc.ToString("o")
                });
            }

            return Unauthorized(new { message = "אימייל או סיסמה שגויים." });
        }

        public class LoginRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        private (string Token, DateTime ExpiresAtUtc, int ExpiresInMinutes) CreateJwtToken(AuthenticatedUser user)
        {
            string issuer = (_configuration["Jwt:Issuer"] ?? "ReactServerSide").Trim();
            string audience = (_configuration["Jwt:Audience"] ?? "ReactProjectClient").Trim();
            string secret = (_configuration["Jwt:SecretKey"] ?? string.Empty).Trim();

            if (secret.Length < 32)
            {
                throw new InvalidOperationException("Jwt:SecretKey must be configured and at least 32 characters long.");
            }

            if (!int.TryParse(_configuration["Jwt:ExpiryMinutes"], out int expiresInMinutes) || expiresInMinutes < 5)
            {
                expiresInMinutes = 180;
            }

            DateTime expiresAtUtc = DateTime.UtcNow.AddMinutes(expiresInMinutes);

            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("userType", user.UserType),
                new Claim(ClaimTypes.Name, user.FullName ?? string.Empty)
            };

            SigningCredentials signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256);

            JwtSecurityToken jwtToken = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAtUtc,
                signingCredentials: signingCredentials);

            string serializedToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);
            return (serializedToken, expiresAtUtc, expiresInMinutes);
        }
    }
}