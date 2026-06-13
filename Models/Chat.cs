namespace ihsbmodern.Models;

public class Chat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Chat";
    public Guid? FolderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<ChatMessage> Messages { get; set; } = new();
}
