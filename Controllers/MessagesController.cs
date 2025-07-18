// Controllers/MessagesController.cs
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using ChatApp.API.Models;
using ChatApp.API.Services;
using NpgsqlTypes;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Security.Claims;

namespace ChatApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly DatabaseService _dbService;

        public MessagesController(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        // GET: api/messages
        // [HttpGet]
    //     public async Task<IActionResult> GetMessages()
    // {
    //     try
    //     {
    //         using (var conn = _dbService.GetConnection())
    //         {
    //             await conn.OpenAsync();
    //             var messages = new List<Message>();

    //             string sql = @"
    //                 SELECT m.id, m.content, m.sender_id, m.receiver_id, m.timestamp,
    //                        u.username as sender_name
    //                 FROM messages m
    //                 LEFT JOIN users u ON m.sender_id = u.id::varchar
    //                 ORDER BY m.timestamp DESC
    //                 LIMIT 50";

    //             using (var cmd = new NpgsqlCommand(sql, conn))
    //             using (var reader = await cmd.ExecuteReaderAsync())
    //             {
    //                 while (await reader.ReadAsync())
    //                 {
    //                     messages.Add(new Message
    //                     {
    //                         Id = reader.GetInt32(0),
    //                         Content = reader.GetString(1),
    //                         SenderId = reader.GetString(2),
    //                         ReceiverId = !reader.IsDBNull(3) ? reader.GetString(3) : null,
    //                         Timestamp = reader.GetDateTime(4),
    //                         SenderName = !reader.IsDBNull(5) ? reader.GetString(5) : null
    //                     });
    //                 }
    //             }
    //             return Ok(messages.OrderBy(m => m.Timestamp));
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         Console.WriteLine($"Error getting messages: {ex}");
    //         return StatusCode(500, "Failed to load messages");
    //     }
    // }
        // GET: api/messages/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMessage(int id)
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();
                    string sql = "SELECT * FROM messages WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var message = new Message
                                {
                                    Id = reader.GetInt32(0),
                                    Content = reader.GetString(1),
                                    SenderId = reader.GetString(2),
                                    ReceiverId = reader.GetString(3),
                                    Timestamp = reader.GetDateTime(4)
                                };
                                return Ok(message);
                            }
                            return NotFound($"Message with ID {id} not found");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // POST: api/messages
        [HttpPost]
public async Task<IActionResult> SendMessage([FromBody] Message message)
{
    try
    {
        using (var conn = _dbService.GetConnection())
        {
            await conn.OpenAsync();
            string sql = @"
                INSERT INTO messages (content, sender_id, receiver_id, timestamp)
                VALUES (@content, @senderId, @receiverId, @timestamp)
                RETURNING id";

            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("content", message.Content);
                cmd.Parameters.AddWithValue("senderId", int.Parse(message.SenderId));
                cmd.Parameters.AddWithValue("receiverId", 
                    !string.IsNullOrEmpty(message.ReceiverId) ? 
                    int.Parse(message.ReceiverId) as object : 
                    DBNull.Value);
                cmd.Parameters.AddWithValue("timestamp", DateTime.UtcNow);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    message.Id = Convert.ToInt32(result);
                }
                message.Timestamp = DateTime.UtcNow;

                return Ok(message);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error sending message: {ex}");
        return StatusCode(500, "Failed to send message");
    }
}

[HttpGet]
public async Task<IActionResult> GetMessages()
{
    try
    {
        using (var conn = _dbService.GetConnection())
        {
            await conn.OpenAsync();
            var messages = new List<Message>();

            string sql = @"
                SELECT m.id, m.content, m.sender_id, m.receiver_id, m.timestamp,
                       u.username as sender_name
                FROM messages m
                LEFT JOIN users u ON m.sender_id = u.id
                ORDER BY m.timestamp DESC
                LIMIT 50";

            using (var cmd = new NpgsqlCommand(sql, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    messages.Add(new Message
                    {
                        Id = reader.GetInt32(0),
                        Content = reader.GetString(1),
                        SenderId = reader.GetInt32(2).ToString(),
                        ReceiverId = !reader.IsDBNull(3) ? 
                            reader.GetInt32(3).ToString() : null,
                        Timestamp = reader.GetDateTime(4),
                        SenderName = !reader.IsDBNull(5) ? 
                            reader.GetString(5) : null
                    });
                }
            }
            return Ok(messages.OrderBy(m => m.Timestamp));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error getting messages: {ex}");
        return StatusCode(500, "Failed to load messages");
    }
}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMessage(int id)
        {
            try
            {
                using (var conn = _dbService.GetConnection())
                {
                    await conn.OpenAsync();
                    string sql = "DELETE FROM messages WHERE id = @id";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        int affected = await cmd.ExecuteNonQueryAsync();

                        if (affected > 0)
                            return NoContent();
                        return NotFound($"Message with ID {id} not found");
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/messages/chat/{userId}
        [HttpGet("chat/{userId}")]
public async Task<IActionResult> GetChatMessages(string userId)
{
    try
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        using (var conn = _dbService.GetConnection())
        {
            await conn.OpenAsync();
            var messages = new List<Message>();

            string sql = @"
                SELECT m.id, m.content, m.sender_id, m.receiver_id, m.timestamp,
                       u.username as sender_name,
                m.status,      
                m.delivered_at,
                m.read_at 
                FROM messages m
                LEFT JOIN users u ON m.sender_id = u.id
                WHERE (m.sender_id = @userId1 AND m.receiver_id = @userId2)
                   OR (m.sender_id = @userId2 AND m.receiver_id = @userId1)
                ORDER BY m.timestamp DESC
                LIMIT 50";

            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("userId1", int.Parse(userId));
                cmd.Parameters.AddWithValue("userId2", int.Parse(currentUserId));

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        messages.Add(new Message
                        {
                            Id = reader.GetInt32(0),
                            Content = reader.GetString(1),
                            SenderId = reader.GetInt32(2).ToString(),
                            ReceiverId = !reader.IsDBNull(3) ? reader.GetInt32(3).ToString() : null,
                            Timestamp = reader.GetDateTime(4),
                            SenderName = !reader.IsDBNull(5) ? reader.GetString(5) : null,
                Status = (MessageStatus)reader.GetInt32(6),  // Add this
                DeliveredAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),  // Add this
                ReadAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
                        });
                    }
                }
            }
            return Ok(messages.OrderBy(m => m.Timestamp));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error getting private messages: {ex}");
        return StatusCode(500, "Failed to load private messages");
    }
}}
    

    
}