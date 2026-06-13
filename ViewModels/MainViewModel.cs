using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ihsbmodern.Models;
using ihsbmodern.Services;

namespace ihsbmodern.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DeepSeekService _deepSeek = new();
    private readonly FileSystemService _fileSystem = new();

    [ObservableProperty]
    private Folder? _selectedFolder;

    [ObservableProperty]
    private Chat? _selectedChat;

    [ObservableProperty]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    private string _newChatTitle = string.Empty;

    [ObservableProperty]
    private bool _isEditingFolderName;

    [ObservableProperty]
    private string _editingFolderName = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _isWaitingForResponse;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _selectedModel = "deepseek-chat";

    [ObservableProperty]
    private string _selectedReasoningEffort = "medium";

    [ObservableProperty]
    private bool _showReasoningEffort;

    [ObservableProperty]
    private string? _workingDirectory;

    public ObservableCollection<Folder> Folders { get; } = new();
    public ObservableCollection<Chat> Chats { get; } = new();
    public ObservableCollection<ChatMessage> CurrentMessages { get; } = new();

    public string[] AvailableModels { get; } = ["deepseek-chat", "deepseek-reasoner"];
    public string[] AvailableEfforts { get; } = ["low", "medium", "high"];

    public bool IsApiKeySet => _deepSeek.IsConfigured;
    public bool IsWorkingDirectorySet => _fileSystem.IsConfigured;

    public MainViewModel()
    {
        var saved = LoadSettings();
        if (!string.IsNullOrWhiteSpace(saved.ApiKey))
        {
            _deepSeek.ApiKey = saved.ApiKey;
            ApiKey = saved.ApiKey;
        }
        if (!string.IsNullOrWhiteSpace(saved.Model))
        {
            _deepSeek.Model = saved.Model;
            SelectedModel = saved.Model;
        }
        if (!string.IsNullOrWhiteSpace(saved.Effort))
        {
            _deepSeek.ReasoningEffort = saved.Effort;
            SelectedReasoningEffort = saved.Effort;
        }
        if (!string.IsNullOrWhiteSpace(saved.WorkDir) && Directory.Exists(saved.WorkDir))
        {
            _fileSystem.RootPath = saved.WorkDir;
            WorkingDirectory = saved.WorkDir;
        }
        ShowReasoningEffort = SelectedModel == "deepseek-reasoner";

        var defaultFolder = new Folder { Name = "General" };
        Folders.Add(defaultFolder);

        var defaultChat = new Chat { Title = "Welcome Chat", FolderId = defaultFolder.Id };
        defaultChat.Messages.Add(new ChatMessage
        {
            Content = "Hello! Type a message below to start chatting with DeepSeek.\n\nSet a working directory in the sidebar to enable file read/edit capabilities.",
            IsUser = false
        });
        Chats.Add(defaultChat);
        SelectedFolder = defaultFolder;
        SelectedChat = defaultChat;
    }

    partial void OnApiKeyChanged(string value)
    {
        _deepSeek.ApiKey = value;
        SaveSettings();
        OnPropertyChanged(nameof(IsApiKeySet));
    }

    partial void OnSelectedModelChanged(string value)
    {
        _deepSeek.Model = value;
        ShowReasoningEffort = value == "deepseek-reasoner";
        SaveSettings();
    }

    partial void OnSelectedReasoningEffortChanged(string value)
    {
        _deepSeek.ReasoningEffort = value;
        SaveSettings();
    }

    partial void OnSelectedFolderChanged(Folder? value)
    {
        OnPropertyChanged(nameof(ChatsInSelectedFolder));
    }

    partial void OnSelectedChatChanged(Chat? value)
    {
        CurrentMessages.Clear();
        if (value != null)
        {
            foreach (var msg in value.Messages)
                CurrentMessages.Add(msg);
        }
        OnPropertyChanged(nameof(HasActiveChat));
    }

    public IEnumerable<Chat> ChatsInSelectedFolder =>
        SelectedFolder == null
            ? Chats.Where(c => c.FolderId == null)
            : Chats.Where(c => c.FolderId == SelectedFolder.Id);

    public bool HasActiveChat => SelectedChat != null;

    [RelayCommand]
    private void SetWorkingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            WorkingDirectory = null;
            _fileSystem.RootPath = null;
        }
        else
        {
            WorkingDirectory = path;
            _fileSystem.RootPath = path;
        }
        SaveSettings();
        OnPropertyChanged(nameof(IsWorkingDirectorySet));
    }

    private string ExecuteTool(string toolName, string args)
    {
        return toolName switch
        {
            "list_files" => _fileSystem.ListFiles(args),
            "read_file" => _fileSystem.ReadFile(args),
            "edit_file" =>
                JsonSerializer.Deserialize<JsonElement>(args) is var el &&
                el.TryGetProperty("path", out var p) &&
                el.TryGetProperty("old_text", out var o) &&
                el.TryGetProperty("new_text", out var n)
                    ? _fileSystem.EditFile(p.GetString()!, o.GetString()!, n.GetString()!)
                    : "Error: invalid arguments",
            "create_file" =>
                JsonSerializer.Deserialize<JsonElement>(args) is var el2 &&
                el2.TryGetProperty("path", out var p2) &&
                el2.TryGetProperty("content", out var c)
                    ? _fileSystem.CreateFile(p2.GetString()!, c.GetString()!)
                    : "Error: invalid arguments",
            "grep" =>
                JsonSerializer.Deserialize<JsonElement>(args) is var el4 &&
                el4.TryGetProperty("pattern", out var pat) &&
                el4.TryGetProperty("path", out var gpath)
                    ? _fileSystem.Grep(pat.GetString()!, gpath.GetString()!, el4.TryGetProperty("glob", out var g) ? g.GetString() : null)
                    : "Error: invalid arguments",
            "run_command" =>
                JsonSerializer.Deserialize<JsonElement>(args) is var el3 &&
                el3.TryGetProperty("command", out var cmd)
                    ? _fileSystem.RunCommand(cmd.GetString()!)
                    : "Error: invalid arguments",
            "launch_process" =>
                JsonSerializer.Deserialize<JsonElement>(args) is var el5 &&
                el5.TryGetProperty("command", out var cmd2)
                    ? _fileSystem.LaunchProcess(cmd2.GetString()!)
                    : "Error: invalid arguments",
            "open_file" =>
                JsonSerializer.Deserialize<JsonElement>(args) is var el6 &&
                el6.TryGetProperty("path", out var opath)
                    ? _fileSystem.OpenFile(opath.GetString()!)
                    : "Error: invalid arguments",
            _ => $"Unknown tool: {toolName}"
        };
    }

    [RelayCommand]
    private void AddFolder()
    {
        var name = string.IsNullOrWhiteSpace(NewFolderName) ? "New Folder" : NewFolderName.Trim();
        var folder = new Folder { Name = name };
        Folders.Add(folder);
        NewFolderName = string.Empty;
        SelectedFolder = folder;
    }

    [RelayCommand]
    private void DeleteFolder(Folder? folder)
    {
        if (folder == null) return;

        var chatsToRemove = Chats.Where(c => c.FolderId == folder.Id).ToList();
        foreach (var chat in chatsToRemove)
            Chats.Remove(chat);

        Folders.Remove(folder);

        if (SelectedFolder == folder)
            SelectedFolder = Folders.FirstOrDefault();
    }

    [RelayCommand]
    private void StartEditFolderName(Folder? folder)
    {
        if (folder == null) return;
        EditingFolderName = folder.Name;
        IsEditingFolderName = true;
    }

    [RelayCommand]
    private void SaveFolderName(Folder? folder)
    {
        if (folder == null) return;
        if (!string.IsNullOrWhiteSpace(EditingFolderName))
            folder.Name = EditingFolderName.Trim();
        IsEditingFolderName = false;
    }

    [RelayCommand]
    private void CancelEditFolderName()
    {
        IsEditingFolderName = false;
    }

    [RelayCommand]
    private void AddChat()
    {
        var title = string.IsNullOrWhiteSpace(NewChatTitle) ? $"Chat {Chats.Count + 1}" : NewChatTitle.Trim();
        var chat = new Chat
        {
            Title = title,
            FolderId = SelectedFolder?.Id
        };
        Chats.Add(chat);
        NewChatTitle = string.Empty;
        SelectedChat = chat;
        OnPropertyChanged(nameof(ChatsInSelectedFolder));
    }

    [RelayCommand]
    private void DeleteChat(Chat? chat)
    {
        if (chat == null) return;
        Chats.Remove(chat);
        if (SelectedChat == chat)
        {
            SelectedChat = ChatsInSelectedFolder.FirstOrDefault();
        }
        OnPropertyChanged(nameof(ChatsInSelectedFolder));
    }

    [RelayCommand]
    private void MoveChatToFolder(Chat? chat)
    {
        if (chat == null || SelectedFolder == null) return;
        chat.FolderId = SelectedFolder.Id;
        OnPropertyChanged(nameof(ChatsInSelectedFolder));
    }

    [RelayCommand]
    private async Task SendMessageAsync(CancellationToken ct)
    {
        if (SelectedChat == null || string.IsNullOrWhiteSpace(MessageInput)) return;

        var userText = MessageInput.Trim();
        MessageInput = string.Empty;

        var userMessage = new ChatMessage { Content = userText, IsUser = true };
        SelectedChat.Messages.Add(userMessage);
        CurrentMessages.Add(userMessage);

        if (!_deepSeek.IsConfigured)
        {
            var noKey = new ChatMessage
            {
                Content = "Please set your DeepSeek API key in the sidebar to get responses.",
                IsUser = false
            };
            SelectedChat.Messages.Add(noKey);
            CurrentMessages.Add(noKey);
            return;
        }

        IsWaitingForResponse = true;
        StatusMessage = $"Thinking ({SelectedModel})...";

        try
        {
            var conversationHistory = new List<(string role, string content)>
            {
                ("system", BuildSystemPrompt())
            };

            foreach (var msg in SelectedChat.Messages)
            {
                conversationHistory.Add((msg.IsUser ? "user" : "assistant", msg.Content));
            }

            Func<string, string, string>? toolExecutor = _fileSystem.IsConfigured ? ExecuteTool : null;

            var result = await _deepSeek.SendMessageAsync(conversationHistory, toolExecutor, ct);

            var aiMessage = new ChatMessage
            {
                Content = result.Content,
                IsUser = false,
                ToolActivity = result.ToolActivity.Select(t => t.summary).ToList()
            };
            SelectedChat.Messages.Add(aiMessage);
            CurrentMessages.Add(aiMessage);
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            var errorMsg = new ChatMessage
            {
                Content = $"Error: {ex.Message}",
                IsUser = false
            };
            SelectedChat.Messages.Add(errorMsg);
            CurrentMessages.Add(errorMsg);
            StatusMessage = "Error occurred";
        }
        finally
        {
            IsWaitingForResponse = false;
        }
    }

    private string BuildSystemPrompt()
    {
        var prompt = "You are a helpful assistant. Be concise and clear.";

        if (_fileSystem.IsConfigured)
        {
            prompt += $@"

You have access to the user's file system. The working directory is: {_fileSystem.RootPath}
Available tools:
- list_files / read_file / grep — explore the codebase
- edit_file / create_file — modify files
- run_command — run a command and get its output (build, test, git, lint, install)
- launch_process — start a background process that keeps running (dev servers, watchers)
- open_file — open a file or URL in the user's default app (browser, editor)

When editing files, always read the file first to see the current content, then make precise edits.
After making changes, run relevant commands to verify (build, test, lint).
Use open_file (NOT run_command) to show files to the user.
Use launch_process (NOT run_command) for long-running processes like dev servers.";
        }

        return prompt;
    }

    [RelayCommand]
    private void NewChat()
    {
        AddChat();
    }

    private static string SettingsPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ihsbmodern",
            "settings.json");

    private static (string? ApiKey, string? Model, string? Effort, string? WorkDir) LoadSettings()
    {
        try
        {
            if (System.IO.File.Exists(SettingsPath))
            {
                var json = System.IO.File.ReadAllText(SettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return (
                    root.TryGetProperty("apiKey", out var k) ? k.GetString() : null,
                    root.TryGetProperty("model", out var m) ? m.GetString() : null,
                    root.TryGetProperty("effort", out var e) ? e.GetString() : null,
                    root.TryGetProperty("workDir", out var w) ? w.GetString() : null
                );
            }
        }
        catch { }
        return (null, null, null, null);
    }

    private void SaveSettings()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(SettingsPath)!;
            System.IO.Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new
            {
                apiKey = ApiKey,
                model = SelectedModel,
                effort = SelectedReasoningEffort,
                workDir = WorkingDirectory
            });
            System.IO.File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
