using System.Diagnostics;
using System.Text;

namespace ihsbmodern.Services;

public class FileSystemService
{
    private string? _rootPath;

    public string? RootPath
    {
        get => _rootPath;
        set => _rootPath = value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_rootPath) && Directory.Exists(_rootPath);

    private string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_rootPath))
            throw new InvalidOperationException("No working directory configured.");

        var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

        if (!combined.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Access denied: path is outside the working directory.");

        return combined;
    }

    public string ListFiles(string relativePath = ".")
    {
        var fullPath = ResolvePath(relativePath);
        if (!Directory.Exists(fullPath))
            return $"Error: Directory not found: {relativePath}";

        var sb = new StringBuilder();
        var dirInfo = new DirectoryInfo(fullPath);

        foreach (var dir in dirInfo.GetDirectories().Take(100))
            sb.AppendLine($"[DIR]  {Path.GetRelativePath(_rootPath!, dir.FullName)}/");

        foreach (var file in dirInfo.GetFiles().Take(200))
            sb.AppendLine($"[FILE] {Path.GetRelativePath(_rootPath!, file.FullName)}  ({file.Length} bytes)");

        return sb.Length == 0 ? "(empty directory)" : sb.ToString();
    }

    public string ReadFile(string relativePath)
    {
        var fullPath = ResolvePath(relativePath);
        if (!File.Exists(fullPath))
            return $"Error: File not found: {relativePath}";

        var info = new FileInfo(fullPath);
        if (info.Length > 500_000)
            return $"Error: File too large ({info.Length} bytes). Max 500KB.";

        return File.ReadAllText(fullPath);
    }

    public string EditFile(string relativePath, string oldText, string newText)
    {
        var fullPath = ResolvePath(relativePath);
        if (!File.Exists(fullPath))
            return $"Error: File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        if (!content.Contains(oldText))
            return "Error: The specified oldText was not found in the file. The file may have changed or the text is not an exact match.";

        var newContent = content.Replace(oldText, newText, StringComparison.Ordinal);
        File.WriteAllText(fullPath, newContent);
        return "OK: File updated successfully.";
    }

    public string CreateFile(string relativePath, string content)
    {
        var fullPath = ResolvePath(relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return $"OK: File created at {relativePath}.";
    }

    public string Grep(string pattern, string relativePath = ".", string? fileGlob = null)
    {
        var fullPath = ResolvePath(relativePath);
        if (!Directory.Exists(fullPath))
            return $"Error: Directory not found: {relativePath}";

        var sb = new StringBuilder();
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
        int matchCount = 0;
        int fileCount = 0;

        var searchOption = SearchOption.AllDirectories;
        var files = Directory.EnumerateFiles(fullPath, fileGlob ?? "*.*", searchOption);

        foreach (var file in files)
        {
            if (matchCount > 500) break;

            try
            {
                var info = new FileInfo(file);
                if (info.Length > 1_000_000) continue;

                var lines = File.ReadAllLines(file);
                bool headerPrinted = false;
                var relPath = Path.GetRelativePath(_rootPath!, file);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        if (!headerPrinted)
                        {
                            if (fileCount > 0) sb.AppendLine();
                            headerPrinted = true;
                            fileCount++;
                        }
                        sb.AppendLine($"{relPath}:{i + 1}: {lines[i]}");
                        matchCount++;
                        if (matchCount > 500)
                        {
                            sb.AppendLine("... (truncated, too many matches)");
                            break;
                        }
                    }
                }
            }
            catch
            {
                // skip unreadable files
            }
        }

        return matchCount == 0 ? "No matches found." : sb.ToString();
    }

    public string RunCommand(string command, int timeoutSeconds = 30)
    {
        if (string.IsNullOrWhiteSpace(_rootPath))
            return "Error: No working directory configured.";

        var sb = new StringBuilder();
        var encodedCmd = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedCmd}",
            WorkingDirectory = _rootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return "Error: Failed to start process.";

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                try { process.Kill(true); } catch { }
                return $"Error: Command timed out after {timeoutSeconds}s.";
            }

            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            if (!string.IsNullOrEmpty(stdout))
                sb.Append(stdout);
            if (!string.IsNullOrEmpty(stderr))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("[stderr] ");
                sb.Append(stderr);
            }

            if (sb.Length == 0)
                sb.AppendLine($"(no output, exit code {process.ExitCode})");
            else if (process.ExitCode != 0)
                sb.AppendLine($"\n[exit code {process.ExitCode}]");

            var result = sb.ToString();
            if (result.Length > 50_000)
                result = result[..50_000] + "\n... (truncated)";
            return result;
        }
        catch (Exception ex)
        {
            return $"Error running command: {ex.Message}";
        }
    }

    public string LaunchProcess(string command)
    {
        if (string.IsNullOrWhiteSpace(_rootPath))
            return "Error: No working directory configured.";

        try
        {
            var encodedCmd = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var psi = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedCmd}",
                WorkingDirectory = _rootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = Process.Start(psi);
            if (process == null)
                return "Error: Failed to start process.";

            return $"Launched (PID {process.Id}). Process is running in the background.";
        }
        catch (Exception ex)
        {
            return $"Error launching process: {ex.Message}";
        }
    }

    public string OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(_rootPath))
            return "Error: No working directory configured.";

        try
        {
            var target = path.Trim().Trim('"', '\'');

            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, target));
            var exists = File.Exists(fullPath) || Directory.Exists(fullPath);
            var isUrl = Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == "http" || uri.Scheme == "https");

            if (!exists && !isUrl)
                return $"Error: Path not found: {fullPath}";

            var openTarget = exists ? fullPath : target;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{openTarget}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);

            return $"Opened: {openTarget}";
        }
        catch (Exception ex)
        {
            return $"Error opening: {ex.Message}";
        }
    }
}
