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

        public ChatHub(DatabaseService dbService)
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
                message.Status = MessageStatus.Sent;

                // Save message to database
                using (var conn = _dbService!.GetConnection())
                {
                    await conn.OpenAsync();
                    string sql = @"
                        INSERT INTO messages (content, sender_id, receiver_id, timestamp, status)
                        VALUES (@content, @senderId, @receiverId, @timestamp, @status)
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
                        cmd.Parameters.AddWithValue("status", (int)message.Status);

                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            message.Id = Convert.ToInt32(result);
                        }
                        message.Timestamp = DateTime.UtcNow;
                    }
                }

                // Send to clients
                if (!string.IsNullOrEmpty(message.ReceiverId))
                {
                    var receiverConnections = ConnectedUsers
                        .Where(x => x.Value == message.ReceiverId)
                        .Select(x => x.Key)
                        .ToList();

                    if (receiverConnections.Any())
                    {
                        // Mark as delivered if receiver is online
                        message.Status = MessageStatus.Delivered;
                        message.DeliveredAt = DateTime.UtcNow;
                        await UpdateMessageStatus(message.Id, MessageStatus.Delivered);

                        foreach (var connectionId in receiverConnections)
                        {
                            await Clients.Client(connectionId).SendAsync("ReceiveMessage", message);
                        }
                    }

                    // Send to sender with updated status
                    var senderConnections = ConnectedUsers
                        .Where(x => x.Value == message.SenderId)
                        .Select(x => x.Key)
                        .ToList();

                    foreach (var connectionId in senderConnections)
                    {
                        await Clients.Client(connectionId).SendAsync("ReceiveMessage", message);
                    }
                }
                else
                {
                    await Clients.All.SendAsync("ReceiveMessage", message);
                }
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

                Console.WriteLine($"=== OnConnectedAsync ===");
                Console.WriteLine($"User ID: {userId}, Username: {username}, ConnectionId: {Context.ConnectionId}");

                if (!string.IsNullOrEmpty(userId))
                {
                    ConnectedUsers.TryAdd(Context.ConnectionId, userId);
                    Console.WriteLine($"Added to ConnectedUsers: {Context.ConnectionId} -> {userId}");

                    using (var conn = _dbService!.GetConnection())
                    {
                        await conn.OpenAsync();
                        var users = new List<object>();

                        string sql = "SELECT id, username FROM users";

                        using (var cmd = new NpgsqlCommand(sql, conn))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var user = new
                                {
                                    id = reader.GetInt32(0).ToString(),
                                    username = reader.GetString(1),
                                    isOnline = ConnectedUsers.Values.Contains(reader.GetInt32(0).ToString())
                                };
                                users.Add(user);
                            }
                        }

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

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            Console.WriteLine($"=== OnDisconnectedAsync ===");
            Console.WriteLine($"User ID: {userId}, ConnectionId: {Context.ConnectionId}");

            if (!string.IsNullOrEmpty(userId))
            {
                ConnectedUsers.TryRemove(Context.ConnectionId, out _);
                Console.WriteLine($"Removed from ConnectedUsers: {Context.ConnectionId}");

                // Update online users list
                using (var conn = _dbService!.GetConnection())
                {
                    await conn.OpenAsync();
                    var users = new List<object>();

                    string sql = "SELECT id, username FROM users";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var user = new
                            {
                                id = reader.GetInt32(0).ToString(),
                                username = reader.GetString(1),
                                isOnline = ConnectedUsers.Values.Contains(reader.GetInt32(0).ToString())
                            };
                            users.Add(user);
                        }
                    }

                    await Clients.All.SendAsync("UpdateOnlineUsers", users);
                }
            }

            await base.OnDisconnectedAsync(exception);
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

        public async Task MarkMessageAsRead(int messageId, string readerId)
        {
            try
            {
                using (var conn = _dbService!.GetConnection())
                {
                    await conn.OpenAsync();

                    // Get message details
                    string selectSql = "SELECT sender_id, receiver_id FROM messages WHERE id = @id";
                    string? senderId = null;

                    using (var cmd = new NpgsqlCommand(selectSql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", messageId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                senderId = reader["sender_id"].ToString();
                            }
                        }
                    }

                    // Update status to read
                    string updateSql = @"
                        UPDATE messages 
                        SET status = @status, read_at = @readAt 
                        WHERE id = @id AND receiver_id = @readerId";

                    using (var cmd = new NpgsqlCommand(updateSql, conn))
                    {
                        cmd.Parameters.AddWithValue("id", messageId);
                        cmd.Parameters.AddWithValue("status", (int)MessageStatus.Read);
                        cmd.Parameters.AddWithValue("readAt", DateTime.UtcNow);
                        cmd.Parameters.AddWithValue("readerId", int.Parse(readerId));

                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Notify sender about read receipt
                    if (!string.IsNullOrEmpty(senderId))
                    {
                        var senderConnections = ConnectedUsers
                            .Where(x => x.Value == senderId)
                            .Select(x => x.Key)
                            .ToList();

                        foreach (var connectionId in senderConnections)
                        {
                            await Clients.Client(connectionId).SendAsync("MessageStatusUpdated", new
                            {
                                messageId = messageId,
                                status = MessageStatus.Read,
                                readAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error marking message as read: {ex}");
            }
        }

        private async Task UpdateMessageStatus(int messageId, MessageStatus status)
        {
            using (var conn = _dbService!.GetConnection())
            {
                await conn.OpenAsync();
                string sql = @"
                    UPDATE messages 
                    SET status = @status, 
                        delivered_at = CASE WHEN @status >= 1 THEN @deliveredAt ELSE delivered_at END
                    WHERE id = @id";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("id", messageId);
                    cmd.Parameters.AddWithValue("status", (int)status);
                    cmd.Parameters.AddWithValue("deliveredAt", DateTime.UtcNow);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // Call methods
        public async Task InitiateCall(string receiverId, bool isVideo)
        {
            try
            {
                string callerId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                string callerName = Context.User?.Identity?.Name ?? string.Empty;

                if (string.IsNullOrEmpty(callerId) || string.IsNullOrEmpty(receiverId))
                {
                    throw new HubException("User information is missing");
                }

                // Get receiver's connection IDs
                var receiverConnections = ConnectedUsers
                    .Where(x => x.Value == receiverId)
                    .Select(x => x.Key)
                    .ToList();

                if (!receiverConnections.Any())
                {
                    throw new HubException("Receiver is not online");
                }

                // Send call offer to all receiver's connections
                foreach (var connectionId in receiverConnections)
                {
                    await Clients.Client(connectionId).SendAsync("ReceiveCallOffer", new
                    {
                        callerId = callerId,
                        callerName = callerName,
                        isVideo = isVideo
                    });
                }

                Console.WriteLine($"Call initiated from {callerId} to {receiverId}, isVideo: {isVideo}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InitiateCall: {ex}");
                throw new HubException($"Error initiating call: {ex.Message}");
            }
        }

        public async Task SendCallSignal(string receiverId, object signal)
        {
            try
            {
                string senderId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

                if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId))
                {
                    throw new HubException("User information is missing");
                }

                // Get receiver's connection IDs
                var receiverConnections = ConnectedUsers
                    .Where(x => x.Value == receiverId)
                    .Select(x => x.Key)
                    .ToList();

                if (!receiverConnections.Any())
                {
                    throw new HubException("Receiver is not online");
                }

                // Send signal to all receiver's connections
                foreach (var connectionId in receiverConnections)
                {
                    await Clients.Client(connectionId).SendAsync("ReceiveCallSignal", new
                    {
                        senderId = senderId,
                        signal = signal
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendCallSignal: {ex}");
                throw new HubException($"Error sending call signal: {ex.Message}");
            }
        }

        public async Task AnswerCall(string callerId, bool accepted)
        {
            try
            {
                string receiverId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                string receiverName = Context.User?.Identity?.Name ?? string.Empty;

                if (string.IsNullOrEmpty(receiverId) || string.IsNullOrEmpty(callerId))
                {
                    throw new HubException("User information is missing");
                }

                // Get caller's connection IDs
                var callerConnections = ConnectedUsers
                    .Where(x => x.Value == callerId)
                    .Select(x => x.Key)
                    .ToList();

                if (!callerConnections.Any())
                {
                    throw new HubException("Caller is not online");
                }

                // Send answer to all caller's connections
                foreach (var connectionId in callerConnections)
                {
                    await Clients.Client(connectionId).SendAsync("CallAnswered", new
                    {
                        receiverId = receiverId,
                        receiverName = receiverName,
                        accepted = accepted
                    });
                }

                Console.WriteLine($"Call {(accepted ? "accepted" : "rejected")} by {receiverId} from {callerId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AnswerCall: {ex}");
                throw new HubException($"Error answering call: {ex.Message}");
            }
        }

        public async Task EndCall(string userId)
        {
            try
            {
                string enderId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

                if (string.IsNullOrEmpty(enderId) || string.IsNullOrEmpty(userId))
                {
                    throw new HubException("User information is missing");
                }

                // Get user's connection IDs
                var userConnections = ConnectedUsers
                    .Where(x => x.Value == userId)
                    .Select(x => x.Key)
                    .ToList();

                if (!userConnections.Any())
                {
                    // Log that user is not online but don't throw exception
                    Console.WriteLine($"User {userId} is not online to receive end call notification");
                    return;
                }

                // Send end call notification to all user's connections
                foreach (var connectionId in userConnections)
                {
                    await Clients.Client(connectionId).SendAsync("CallEnded", enderId);
                }

                Console.WriteLine($"Call ended by {enderId} with {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in EndCall: {ex}");
                throw new HubException($"Error ending call: {ex.Message}");
            }
        }
    }

}