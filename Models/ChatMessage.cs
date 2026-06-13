namespace ihsbmodern.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<string> ToolActivity { get; set; } = new();
    public bool HasToolActivity => ToolActivity.Count > 0;
}
