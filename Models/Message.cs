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
    }
}
