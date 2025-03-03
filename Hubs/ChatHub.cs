using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using ChatApp.API.Models;
using System.Collections.Concurrent;
using System.Security.Claims;
using Npgsql;
using ChatApp.API.Services;

namespace ChatApp.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, string> ConnectedUsers = new();
        private readonly DatabaseService? _dbService;

        public ChatHub(DatabaseService dbService)  // Add constructor injection
    {
        _dbService = dbService;
    }

        public async Task SendMessage(Message message)
{
    try
    {
        if (string.IsNullOrEmpty(message.Content))
        {
            throw new HubException("Message content cannot be empty");
        }

        // Save message to database
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

                message.Id = (int)await cmd.ExecuteScalarAsync();
                message.Timestamp = DateTime.UtcNow;
            }
        }

        // Send to clients
        if (!string.IsNullOrEmpty(message.ReceiverId))
        {
            var receiverConnectionId = ConnectedUsers
                .FirstOrDefault(x => x.Value == message.ReceiverId)
                .Key;

            if (!string.IsNullOrEmpty(receiverConnectionId))
            {
                await Clients.Client(receiverConnectionId)
                    .SendAsync("ReceiveMessage", message);
                if (message.SenderId != message.ReceiverId)
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", message);
                }
            }
        }
        else
        {
            await Clients.All.SendAsync("ReceiveMessage", message);
        }

        Console.WriteLine($"Message sent and saved: {message.Content}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in SendMessage: {ex}");
        throw new HubException($"Error sending message: {ex.Message}");
    }
}
        public async Task UserTyping()
        {
            try
            {
                var username = Context.User?.Identity?.Name ?? "Someone";
                await Clients.Others.SendAsync("UserTyping", username);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UserTyping: {ex}");
                throw new HubException($"Error sending typing notification: {ex.Message}");
            }
        }

        public override async Task OnConnectedAsync()
{
    try
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = Context.User?.Identity?.Name;
        
        Console.WriteLine($"User connecting - ID: {userId}, Name: {username}");

        if (!string.IsNullOrEmpty(userId))
        {
            ConnectedUsers.TryAdd(Context.ConnectionId, userId);
            
            using (var conn = _dbService.GetConnection())
            {
                await conn.OpenAsync();
                var users = new List<object>();
                
                string sql = "SELECT id, username FROM users";
                Console.WriteLine("Fetching users from database");
                
                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var user = new {
                            id = reader.GetInt32(0).ToString(), // Convert to string
                            username = reader.GetString(1),
                            isOnline = ConnectedUsers.Values.Contains(reader.GetInt32(0).ToString()) // Convert to string
                        };
                        users.Add(user);
                        Console.WriteLine($"Added user: {user.username} (Online: {user.isOnline})");
                    }
                }
                
                Console.WriteLine($"Sending {users.Count} users to clients");
                await Clients.All.SendAsync("UpdateOnlineUsers", users);
            }
        }

        await base.OnConnectedAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in OnConnectedAsync: {ex}");
        throw new HubException($"Error in OnConnectedAsync: {ex.Message}");
    }
}
        public async Task JoinChat()
        {
            try
            {
                var username = Context.User?.Identity?.Name ?? "Anonymous";
                await Clients.All.SendAsync("UserJoined", username);
                Console.WriteLine($"User {username} joined the chat");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in JoinChat: {ex}");
                throw;
            }
        }

        public async Task LeaveChat()
        {
            try
            {
                var username = Context.User?.Identity?.Name ?? "Anonymous";
                await Clients.All.SendAsync("UserLeft", username);
                Console.WriteLine($"User {username} left the chat");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in LeaveChat: {ex}");
                throw;
            }
        }
    }
}