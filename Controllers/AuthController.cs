using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChatApp.API.Models;
using ChatApp.API.Services;
using Npgsql;
using BCrypt.Net;

namespace ChatApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly DatabaseService _dbService;
        private readonly IConfiguration _configuration;

        public AuthController(DatabaseService dbService, IConfiguration configuration)
        {
            _dbService = dbService;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();

                    // Check if email already exists
                    string checkSql = "SELECT COUNT(*) FROM users WHERE email = @email";
                    using (var cmd = new NpgsqlCommand(checkSql, conn))
                    {
                        cmd.Parameters.AddWithValue("email", request.Email);
                        int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        if (count > 0)
                            return BadRequest("Email already registered");
                    }

                    // Hash the password
                    string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                    // Insert new user
                    string sql = @"
                        INSERT INTO users (username, email, password_hash) 
                        VALUES (@username, @email, @passwordHash) 
                        RETURNING id, username";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("username", request.Username);
                        cmd.Parameters.AddWithValue("email", request.Email);
                        cmd.Parameters.AddWithValue("passwordHash", passwordHash);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                int userId = reader.GetInt32(0);
                                string username = reader.GetString(1);
                                
                                var token = GenerateToken(userId, request.Email, username);
                                
                                return Ok(new { 
                                    Token = token,
                                    UserId = userId.ToString(),
                                    Username = username
                                });
                            }
                        }
                    }
                    return StatusCode(500, "Failed to create user");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();
                    string sql = "SELECT id, password_hash, username FROM users WHERE email = @email";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("email", request.Email);
                        
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                int userId = reader.GetInt32(0);
                                string storedHash = reader.GetString(1);
                                string username = reader.GetString(2);

                                if (BCrypt.Net.BCrypt.Verify(request.Password, storedHash))
                                {
                                    var token = GenerateToken(userId, request.Email, username);
                                    return Ok(new { 
                                        Token = token,
                                        UserId = userId.ToString(),
                                        Username = username
                                    });
                                }
                            }
                        }
                    }
                    return Unauthorized("Invalid email or password");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        private string GenerateToken(int userId, string email, string username)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, username)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                _configuration.GetSection("JWT:Key").Value!));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddDays(1),
                SigningCredentials = creds
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }
    }
}