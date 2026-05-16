using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NimChatGui
{
    public class McpToolExecutor
    {
        private readonly McpCatalogClient _catalog;

        public McpToolExecutor(McpCatalogClient catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public Task<string> ExecuteToolAsync(ToolCall toolCall)
        {
            if (toolCall?.Function?.Name == null)
            {
                return Task.FromResult(ToolError("Invalid tool call"));
            }

            var toolName = toolCall.Function.Name;
            var argsJson = toolCall.Function.Arguments ?? "{}";

            try
            {
                return toolName switch
                {
                    var t when t.StartsWith("fs_") => Task.FromResult(ExecuteFilesystemTool(toolName, argsJson)),
                    _ => Task.FromResult(ToolError($"Tool '{toolName}' is not supported on Android. MCP servers require npx/Node.js which is not available on this platform."))
                };
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolError($"Tool execution failed: {ex.Message}"));
            }
        }

        private string ExecuteFilesystemTool(string toolName, string argsJson)
        {
            using var argsDoc = JsonDocument.Parse(argsJson);
            var args = argsDoc.RootElement;

            return toolName switch
            {
                "fs_list_files" => ExecuteListFiles(args),
                "fs_read_file" => ExecuteReadTextFile(args),
                "fs_search_files" => ExecuteSearchFiles(args),
                "fs_write_file" => ExecuteWriteTextFile(args),
                _ => ToolError($"Unknown filesystem tool: {toolName}")
            };
        }

        private string ExecuteListFiles(JsonElement args)
        {
            var pathArg = GetArgString(args, "path", FileSystem.AppDataDirectory);
            var recursive = GetArgBool(args, "recursive", false);
            var maxResults = Math.Clamp(GetArgInt(args, "max_results", 200), 1, 10000);

            var resolvedPath = Path.GetFullPath(pathArg);

            if (!Directory.Exists(resolvedPath))
            {
                return ToolError($"Directory not found: {resolvedPath}");
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = Directory
                .EnumerateFileSystemEntries(resolvedPath, "*", option)
                .Take(maxResults)
                .Select(path => new
                {
                    path = path.Replace('\\', '/'),
                    type = Directory.Exists(path) ? "directory" : "file"
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                ok = true,
                root = resolvedPath.Replace('\\', '/'),
                count = entries.Count,
                entries
            });
        }

        private string ExecuteReadTextFile(JsonElement args)
        {
            var pathArg = GetArgString(args, "path", "");
            var maxChars = Math.Clamp(GetArgInt(args, "max_chars", 12000), 256, 50000);

            if (string.IsNullOrWhiteSpace(pathArg))
            {
                return ToolError("The 'path' argument is required.");
            }

            var resolvedPath = Path.GetFullPath(pathArg);

            if (!File.Exists(resolvedPath))
            {
                return ToolError($"File not found: {resolvedPath}");
            }

            var content = File.ReadAllText(resolvedPath);
            var truncated = content.Length > maxChars;
            if (truncated)
            {
                content = content[..maxChars];
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                path = resolvedPath.Replace('\\', '/'),
                truncated,
                content
            });
        }

        private string ExecuteSearchFiles(JsonElement args)
        {
            var pathArg = GetArgString(args, "path", FileSystem.AppDataDirectory);
            var pattern = GetArgString(args, "pattern", "");
            var maxResults = Math.Clamp(GetArgInt(args, "max_results", 100), 1, 1000);

            if (string.IsNullOrWhiteSpace(pattern))
            {
                return ToolError("The 'pattern' argument is required.");
            }

            var resolvedPath = Path.GetFullPath(pathArg);

            if (!Directory.Exists(resolvedPath))
            {
                return ToolError($"Directory not found: {resolvedPath}");
            }

            var results = Directory
                .EnumerateFiles(resolvedPath, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Take(maxResults)
                .Select(path => path.Replace('\\', '/'))
                .ToList();

            return JsonSerializer.Serialize(new
            {
                ok = true,
                pattern,
                count = results.Count,
                files = results
            });
        }

        private string ExecuteWriteTextFile(JsonElement args)
        {
            var pathArg = GetArgString(args, "path", "");
            var content = GetArgString(args, "content", "");

            if (string.IsNullOrWhiteSpace(pathArg))
            {
                return ToolError("The 'path' argument is required.");
            }

            var resolvedPath = Path.GetFullPath(pathArg);

            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(resolvedPath, content);
            return JsonSerializer.Serialize(new
            {
                ok = true,
                path = resolvedPath.Replace('\\', '/'),
                bytesWritten = System.Text.Encoding.UTF8.GetByteCount(content)
            });
        }

        private static string GetArgString(JsonElement args, string name, string fallback)
        {
            return args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static bool GetArgBool(JsonElement args, string name, bool fallback)
        {
            return args.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : fallback;
        }

        private static int GetArgInt(JsonElement args, string name, int fallback)
        {
            return args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
                ? parsed
                : fallback;
        }

        private static string ToolError(string message)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = message
            });
        }
    }
}
