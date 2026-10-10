using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ConnectBackend.Models;
using Microsoft.IdentityModel.Tokens;

namespace ConnectBackend.Services;

public interface IJwtTokenService
{
    string GenerateToken(User user, string role = "EV_OWNER");
}

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user, string role = "EV_OWNER")
    {
        var secret = _configuration["Jwt:Key"]
            ?? "ConnectEV_SuperSecretSecurityKey_2027_PowerUpAnywhereAnytime!";
        var issuer = _configuration["Jwt:Issuer"] ?? "ConnectEVBackend";
        var audience = _configuration["Jwt:Audience"] ?? "ConnectEVMobileApp";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        string fullName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrEmpty(s)));
        if (string.IsNullOrWhiteSpace(fullName)) fullName = "EV Driver";

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(ClaimTypes.Role, role),
            new Claim("UserCode", user.UserCode),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
