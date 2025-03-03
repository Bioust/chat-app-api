using Microsoft.AspNetCore.Mvc;
using Npgsql;
using ChatApp.API.Models;
using ChatApp.API.Services;

namespace ChatApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly DatabaseService _dbService;

        public UsersController(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        // GET: api/users
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();
                    var users = new List<User>();

                    string sql = "SELECT id, username, email, created_at FROM users ORDER BY username";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            users.Add(new User
                            {
                                Id = reader.GetInt32(0),
                                Username = reader.GetString(1),
                                Email = reader.GetString(2),
                                CreatedAt = reader.GetDateTime(3)
                            });
                        }
                    }
                    return Ok(users);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/users/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();
                    string sql = "SELECT id, username, email, created_at FROM users WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var user = new User
                                {
                                    Id = reader.GetInt32(0),
                                    Username = reader.GetString(1),
                                    Email = reader.GetString(2),
                                    CreatedAt = reader.GetDateTime(3)
                                };
                                return Ok(user);
                            }
                            return NotFound($"User with ID {id} not found");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // POST: api/users
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] User user)
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();
                    string sql = @"
                        INSERT INTO users (username, email, password_hash) 
                        VALUES (@username, @email, @passwordHash) 
                        RETURNING id, created_at";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("username", user.Username);
                        cmd.Parameters.AddWithValue("email", user.Email);
                        cmd.Parameters.AddWithValue("passwordHash", user.PasswordHash);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                user.Id = reader.GetInt32(0);
                                user.CreatedAt = reader.GetDateTime(1);
                            }
                        }
                    }
                    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}