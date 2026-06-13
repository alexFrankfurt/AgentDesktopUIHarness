namespace ihsbmodern.Models;

public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Folder";
}
