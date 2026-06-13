using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ihsbmodern.Services;

public class ToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public ToolCallFunction Function { get; set; } = new();
}

public class ToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
}

public class ChatResult
{
    public string Content { get; set; } = "";
    public List<ToolCall> ToolCalls { get; set; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;
    public List<(string toolName, string summary)> ToolActivity { get; set; } = new();
}

public class DeepSeekService
{
    private const string BaseUrl = "https://api.deepseek.com/v1/chat/completions";
    private readonly HttpClient _httpClient = new();

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "deepseek-chat";
    public string ReasoningEffort { get; set; } = "medium";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    private static JsonElement GetToolsJson() => JsonSerializer.Deserialize<JsonElement>(@"[
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""list_files"",
                ""description"": ""List files and directories at a given path relative to the working directory. Use '.' for the root."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""path"": { ""type"": ""string"", ""description"": ""Relative path from working directory. Use '.' for root."" }
                    },
                    ""required"": [""path""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""read_file"",
                ""description"": ""Read the full contents of a file at the given path relative to the working directory."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""path"": { ""type"": ""string"", ""description"": ""Relative path to the file from working directory."" }
                    },
                    ""required"": [""path""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""edit_file"",
                ""description"": ""Edit a file by replacing exact old text with new text. The old_text must match exactly including whitespace."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""path"": { ""type"": ""string"", ""description"": ""Relative path to the file."" },
                        ""old_text"": { ""type"": ""string"", ""description"": ""Exact text to find and replace."" },
                        ""new_text"": { ""type"": ""string"", ""description"": ""Replacement text."" }
                    },
                    ""required"": [""path"", ""old_text"", ""new_text""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""create_file"",
                ""description"": ""Create or overwrite a file with the given content. Parent directories are created automatically."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""path"": { ""type"": ""string"", ""description"": ""Relative path for the new file."" },
                        ""content"": { ""type"": ""string"", ""description"": ""File content to write."" }
                    },
                    ""required"": [""path"", ""content""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""grep"",
                ""description"": ""Search file contents using a regex pattern. Returns matching lines with file paths and line numbers. Searches recursively from the given path."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""pattern"": { ""type"": ""string"", ""description"": ""Regex pattern to search for."" },
                        ""path"": { ""type"": ""string"", ""description"": ""Relative directory or file path to search in. Use '.' for entire working directory."" },
                        ""glob"": { ""type"": ""string"", ""description"": ""Optional file glob filter, e.g. '*.cs' or '*.py'. Defaults to all files."" }
                    },
                    ""required"": [""pattern"", ""path""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""run_command"",
                ""description"": ""Run a PowerShell command and wait for its output. Use for build, test, git, install, lint, or any command where you need stdout/stderr. Returns after the command finishes."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""command"": { ""type"": ""string"", ""description"": ""The PowerShell command to execute."" }
                    },
                    ""required"": [""command""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""launch_process"",
                ""description"": ""Start a background process that keeps running after the call returns. Use for dev servers, watchers, or any long-running command. Does NOT capture output."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""command"": { ""type"": ""string"", ""description"": ""The PowerShell command to run in the background."" }
                    },
                    ""required"": [""command""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""open_file"",
                ""description"": ""Open a file or URL in the user's default application (browser, editor, etc.). Use this to show files to the user, NOT run_command."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""path"": { ""type"": ""string"", ""description"": ""Relative file path or URL to open."" }
                    },
                    ""required"": [""path""]
                }
            }
        }
    ]");

    public async Task<ChatResult> SendMessageAsync(
        IEnumerable<(string role, string content)> messages,
        Func<string, string, string>? toolExecutor = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("API key is not configured.");

        var conversationMessages = new List<object>();
        foreach (var m in messages)
            conversationMessages.Add(new { role = m.role, content = m.content });

        return await SendWithToolsAsync(conversationMessages, toolExecutor, ct);
    }

    private async Task<ChatResult> SendWithToolsAsync(
        List<object> conversationMessages,
        Func<string, string, string>? toolExecutor,
        CancellationToken ct,
        int depth = 0)
    {
        if (depth > 10)
            return new ChatResult { Content = "Error: Too many tool call iterations." };

        var useTools = toolExecutor != null;
        var toolsElement = useTools ? GetToolsJson() : (JsonElement?)null;
        var messageArray = conversationMessages.ToArray();

        Dictionary<string, object?> payload;
        if (Model == "deepseek-reasoner")
        {
            payload = new Dictionary<string, object?>
            {
                ["model"] = Model,
                ["messages"] = messageArray,
                ["stream"] = false,
                ["reasoning_effort"] = ReasoningEffort
            };
        }
        else
        {
            payload = new Dictionary<string, object?>
            {
                ["model"] = Model,
                ["messages"] = messageArray,
                ["stream"] = false
            };
        }

        if (toolsElement.HasValue)
            payload["tools"] = toolsElement.Value;

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API error {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var choice = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var result = new ChatResult();
        var content = choice.GetProperty("content").GetString() ?? "";

        if (Model == "deepseek-reasoner" && choice.TryGetProperty("reasoning_content", out var reasoning))
        {
            var reasoningText = reasoning.GetString();
            if (!string.IsNullOrWhiteSpace(reasoningText))
                content = $"💭 *Reasoning:*\n{reasoningText}\n\n---\n\n*Answer:*\n{content}";
        }

        result.Content = content;

        if (choice.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCallsElement.EnumerateArray())
            {
                result.ToolCalls.Add(new ToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? "",
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                        Arguments = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                    }
                });
            }
        }

        if (!result.HasToolCalls || toolExecutor == null)
            return result;

        conversationMessages.Add(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = result.ToolCalls.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new { name = tc.Function.Name, arguments = tc.Function.Arguments }
            }).ToArray()
        });

        foreach (var tc in result.ToolCalls)
        {
            string toolResult;
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments);
                toolResult = tc.Function.Name switch
                {
                    "list_files" => toolExecutor("list_files", args!["path"].GetString() ?? "."),
                    "read_file" => toolExecutor("read_file", args!["path"].GetString() ?? ""),
                    "edit_file" => toolExecutor("edit_file", JsonSerializer.Serialize(new
                    {
                        path = args!["path"].GetString(),
                        old_text = args["old_text"].GetString(),
                        new_text = args["new_text"].GetString()
                    })),
                    "create_file" => toolExecutor("create_file", JsonSerializer.Serialize(new
                    {
                        path = args!["path"].GetString(),
                        content = args["content"].GetString()
                    })),
                    "grep" => toolExecutor("grep", JsonSerializer.Serialize(new
                    {
                        pattern = args!["pattern"].GetString(),
                        path = args["path"].GetString(),
                        glob = args.TryGetValue("glob", out var gv) ? gv.GetString() : null
                    })),
                    "run_command" => toolExecutor("run_command", JsonSerializer.Serialize(new
                    {
                        command = args!["command"].GetString()
                    })),
                    "launch_process" => toolExecutor("launch_process", JsonSerializer.Serialize(new
                    {
                        command = args!["command"].GetString()
                    })),
                    "open_file" => toolExecutor("open_file", JsonSerializer.Serialize(new
                    {
                        path = args!["path"].GetString()
                    })),
                    _ => $"Unknown tool: {tc.Function.Name}"
                };
            }
            catch (Exception ex)
            {
                toolResult = $"Error executing {tc.Function.Name}: {ex.Message}";
            }

            var summary = tc.Function.Name switch
            {
                "list_files" => $"list_files({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["path"].GetString()})",
                "read_file" => $"read_file({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["path"].GetString()})",
                "edit_file" => $"edit_file({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["path"].GetString()})",
                "create_file" => $"create_file({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["path"].GetString()})",
                "grep" => $"grep({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["pattern"].GetString()})",
                "run_command" => $"run_command({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["command"].GetString()})",
                "launch_process" => $"launch_process({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["command"].GetString()})",
                "open_file" => $"open_file({JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tc.Function.Arguments)!["path"].GetString()})",
                _ => tc.Function.Name
            };
            result.ToolActivity.Add((tc.Function.Name, summary));

            conversationMessages.Add(new
            {
                role = "tool",
                tool_call_id = tc.Id,
                content = toolResult
            });
        }

        var followUp = await SendWithToolsAsync(conversationMessages, toolExecutor, ct, depth + 1);
        result.Content = followUp.Content;
        result.ToolCalls = followUp.ToolCalls;
        result.ToolActivity.AddRange(followUp.ToolActivity);
        return result;
    }
}
