namespace ChatApp.API.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public required string SenderId { get; set; }
        public string? ReceiverId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? SenderName { get; set; }
        public bool IsPrivate { get; set; }
        public MessageStatus Status { get; set; } = MessageStatus.Sent;
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    public enum MessageStatus
    {
        Sent = 0,      // Single tick
        Delivered = 1, // Double tick (gray)
        Read = 2       // Double tick (blue)
    }
}
