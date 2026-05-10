using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NimChatGui
{
    /// <summary>
    /// Executes MCP tools by spawning server processes and communicating via stdio.
    /// Handles the MCP JSON-RPC protocol for tool execution.
    /// Full admin access - no path sandboxing or restrictions.
    /// </summary>
    public class McpToolExecutor
    {
        private readonly McpCatalogClient _catalog;
        private readonly Dictionary<string, McpServerProcess> _activeServers;

        public McpToolExecutor(McpCatalogClient catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _activeServers = new Dictionary<string, McpServerProcess>();
        }

        /// <summary>
        /// Execute a tool on the appropriate MCP server.
        /// Routes based on tool name prefix (e.g., fs_*, memory_*, web_*).
        /// </summary>
        public async Task<string> ExecuteToolAsync(ToolCall toolCall)
        {
            if (toolCall?.Function?.Name == null)
            {
                return ToolError("Invalid tool call");
            }

            var toolName = toolCall.Function.Name;
            var argsJson = toolCall.Function.Arguments ?? "{}";

            try
            {
                // Route to appropriate handler based on tool prefix
                return toolName switch
                {
                    // Filesystem tools (run locally, don't spawn server)
                    var t when t.StartsWith("fs_") => await ExecuteFilesystemToolAsync(toolName, argsJson),
                    
                    // Memory tools
                    var t when t.StartsWith("memory_") => await ExecuteMemoryToolAsync(toolName, argsJson),
                    
                    // Todo tools
                    var t when t.StartsWith("todo_") => await ExecuteMcpToolAsync("todo", toolName, argsJson),
                    
                    // Web tools
                    var t when t.StartsWith("web_") => await ExecuteWebToolAsync(toolName, argsJson),
                    
                    // Browser tools
                    var t when t.StartsWith("browser_") => await ExecuteMcpToolAsync("puppeteer", toolName, argsJson),
                    
                    // Git tools
                    var t when t.StartsWith("git_") => await ExecuteMcpToolAsync("git", toolName, argsJson),
                    
                    // GitHub tools
                    var t when t.StartsWith("github_") => await ExecuteMcpToolAsync("github", toolName, argsJson),
                    
                    // Database tools
                    var t when t.StartsWith("db_") => await ExecuteMcpToolAsync("sqlite", toolName, argsJson),
                    var t when t.StartsWith("pg_") => await ExecuteMcpToolAsync("postgres", toolName, argsJson),
                    
                    // Slack tools
                    var t when t.StartsWith("slack_") => await ExecuteMcpToolAsync("slack", toolName, argsJson),
                    
                    // Linear tools
                    var t when t.StartsWith("linear_") => await ExecuteMcpToolAsync("linear", toolName, argsJson),

                    // Generic MCP tools generated for installed servers, e.g. everart_execute
                    var t when t.EndsWith("_execute", StringComparison.OrdinalIgnoreCase) => await ExecuteGenericMcpToolAsync(toolName, argsJson),
                    
                    _ => ToolError($"Unknown tool: {toolName}")
                };
            }
            catch (Exception ex)
            {
                return ToolError($"Tool execution failed: {ex.Message}");
            }
        }

        private async Task<string> ExecuteFilesystemToolAsync(string toolName, string argsJson)
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

        private async Task<string> ExecuteWebToolAsync(string toolName, string argsJson)
        {
            using var argsDoc = JsonDocument.Parse(argsJson);
            var args = argsDoc.RootElement;

            if (toolName == "web_fetch")
            {
                var url = GetArgString(args, "url", "");
                if (string.IsNullOrWhiteSpace(url))
                    return ToolError("URL is required");

                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var response = await client.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();
                    
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        url,
                        status = (int)response.StatusCode,
                        content = content.Length > 5000 ? content.Substring(0, 5000) + "..." : content
                    });
                }
                catch (Exception ex)
                {
                    return ToolError($"Failed to fetch {url}: {ex.Message}");
                }
            }

            if (toolName == "web_search")
            {
                // This requires BRAVE_API_KEY env var to be set
                // For now, return a placeholder
                return ToolError("Web search requires Brave Search server to be installed and BRAVE_API_KEY set");
            }

            return ToolError($"Unknown web tool: {toolName}");
        }

        private async Task<string> ExecuteMcpToolAsync(string serverName, string toolName, string argsJson)
        {
            var installed = _catalog.GetInstalledServers();
            var server = installed.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));

            if (server == null)
            {
                return ToolError($"MCP server '{serverName}' is not installed. Install it from the MCP panel.");
            }

            var missingEnvKeys = server.Env?
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            if (missingEnvKeys != null && missingEnvKeys.Count > 0)
            {
                return ToolError($"MCP server '{serverName}' is installed but not configured. Set the required environment variable(s) first: {string.Join(", ", missingEnvKeys)}");
            }

            try
            {
                var serverProcess = await GetOrCreateServerAsync(server);
                var serverTools = await GetServerToolsAsync(serverProcess);
                var resolvedToolName = ResolveServerToolName(serverTools, toolName, argsJson);
                var toolArguments = ResolveToolArguments(toolName, argsJson, resolvedToolName);
                var toolResponse = await InvokeServerToolAsync(serverProcess, resolvedToolName, toolArguments);

                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    server = serverName,
                    tool = resolvedToolName,
                    result = toolResponse
                });
            }
            catch (Exception ex)
            {
                return ToolError($"Error executing {toolName} on {serverName}: {ex.Message}");
            }
        }

        private async Task<string> ExecuteMemoryToolAsync(string toolName, string argsJson)
        {
            if (toolName.Equals("memory_store", StringComparison.OrdinalIgnoreCase) ||
                toolName.Equals("memory_retrieve", StringComparison.OrdinalIgnoreCase))
            {
                return ToolError("Legacy memory tools 'memory_store' and 'memory_retrieve' are no longer supported. Use 'memory_execute' with an explicit 'action' and optional 'args'.");
            }

            return await ExecuteMcpToolAsync("memory", toolName, argsJson);
        }

        private async Task<string> ExecuteGenericMcpToolAsync(string toolName, string argsJson)
        {
            var serverName = toolName.Substring(0, toolName.Length - "_execute".Length);
            return await ExecuteMcpToolAsync(serverName, toolName, argsJson);
        }

        private async Task<McpServerProcess> GetOrCreateServerAsync(McpServerDefinition server)
        {
            lock (_activeServers)
            {
                if (_activeServers.TryGetValue(server.Name, out var existing) && existing.Process is { HasExited: false })
                {
                    return existing;
                }

                if (_activeServers.TryGetValue(server.Name, out var stale))
                {
                    stale.Dispose();
                    _activeServers.Remove(server.Name);
                }
            }

            var process = StartServerProcess(server);
            await InitializeServerAsync(process);

            lock (_activeServers)
            {
                _activeServers[server.Name] = process;
            }

            return process;
        }

        private McpServerProcess StartServerProcess(McpServerDefinition server)
        {
            var (fileName, arguments) = ResolveServerLaunchCommand(server);
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (server.Env != null)
            {
                foreach (var env in server.Env)
                {
                    startInfo.Environment[env.Key] = env.Value;
                }
            }

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start MCP server '{server.Name}'.");
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Debug.WriteLine($"[mcp:{server.Name}] {e.Data}");
                }
            };
            process.BeginErrorReadLine();

            return new McpServerProcess
            {
                ServerName = server.Name,
                Process = process,
                StdinWriter = process.StandardInput,
                StdoutReader = process.StandardOutput,
            };
        }

        private static (string FileName, string Arguments) ResolveServerLaunchCommand(McpServerDefinition server)
        {
            if (!server.Command.Equals("npx", StringComparison.OrdinalIgnoreCase))
            {
                var args = server.Args != null && server.Args.Count > 0
                    ? string.Join(" ", server.Args.Select(QuoteArgument))
                    : string.Empty;

                return (server.Command, args);
            }

            var npxScript = ResolveNpxCommandPath();
            var npxArgs = server.Args != null && server.Args.Count > 0
                ? string.Join(" ", server.Args.Select(QuoteArgument))
                : string.Empty;

            return (npxScript, npxArgs);
        }

        private static string ResolveNpxCommandPath()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npx.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npx.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npx.ps1"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npx.ps1")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Unable to locate npx.cmd or npx.ps1 under Program Files.");
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return value.Contains(' ') || value.Contains('"') || value.Contains('\t')
                ? '"' + value.Replace("\"", "\\\"") + '"'
                : value;
        }

        private async Task InitializeServerAsync(McpServerProcess serverProcess)
        {
            _ = await SendJsonRpcRequestAsync(serverProcess, "initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new
                {
                    name = "NimChatGui",
                    version = "1.0.0"
                }
            });
        }

        private async Task<List<McpToolMetadata>> GetServerToolsAsync(McpServerProcess serverProcess)
        {
            if (serverProcess.CachedTools != null && serverProcess.CachedTools.Count > 0)
            {
                return serverProcess.CachedTools;
            }

            var responseJson = await SendJsonRpcRequestAsync(serverProcess, "tools/list", new { });
            var tools = new List<McpToolMetadata>();

            using var responseDoc = JsonDocument.Parse(responseJson);
            if (responseDoc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("tools", out var toolsElement) &&
                toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolElement in toolsElement.EnumerateArray())
                {
                    tools.Add(new McpToolMetadata
                    {
                        Name = GetJsonString(toolElement, "name"),
                        Description = GetJsonString(toolElement, "description"),
                        InputSchema = toolElement.TryGetProperty("inputSchema", out var schema) ? schema.Clone() : null
                    });
                }
            }

            serverProcess.CachedTools = tools;
            return tools;
        }

        private static string ResolveServerToolName(List<McpToolMetadata> tools, string genericToolName, string argsJson)
        {
            if (tools.Count == 0)
            {
                throw new InvalidOperationException($"The server for '{genericToolName}' did not expose any tools.");
            }

            var exactToolMatch = tools.FirstOrDefault(tool => tool.Name.Equals(genericToolName, StringComparison.OrdinalIgnoreCase));
            if (exactToolMatch != null)
            {
                return exactToolMatch.Name;
            }

            using var argsDoc = JsonDocument.Parse(argsJson);
            var args = argsDoc.RootElement;

            var desiredToolName = GetJsonString(args, "action")
                ?? GetJsonString(args, "tool")
                ?? GetJsonString(args, "tool_name")
                ?? GetJsonString(args, "name")
                ?? GetJsonString(args, "resource");

            if (!string.IsNullOrWhiteSpace(desiredToolName))
            {
                var exactMatch = tools.FirstOrDefault(tool => tool.Name.Equals(desiredToolName, StringComparison.OrdinalIgnoreCase));
                if (exactMatch != null)
                {
                    return exactMatch.Name;
                }

                throw new InvalidOperationException($"Tool '{desiredToolName}' is not available on this server. Available tools: {string.Join(", ", tools.Select(t => t.Name))}");
            }

            if (tools.Count == 1)
            {
                return tools[0].Name;
            }

            if (genericToolName.EndsWith("_execute", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"This server exposes multiple tools. Specify one using the 'action' argument. Available tools: {string.Join(", ", tools.Select(t => t.Name))}");
            }

            throw new InvalidOperationException($"Tool '{genericToolName}' is not available on this server. Available tools: {string.Join(", ", tools.Select(t => t.Name))}");
        }

        private static JsonElement ResolveToolArguments(string genericToolName, string argsJson, string resolvedToolName)
        {
            using var argsDoc = JsonDocument.Parse(argsJson);
            var args = argsDoc.RootElement;

            if (args.TryGetProperty("args", out var nestedArgs) && nestedArgs.ValueKind == JsonValueKind.Object)
            {
                return nestedArgs.Clone();
            }

            var filtered = new Dictionary<string, object?>();
            foreach (var property in args.EnumerateObject())
            {
                if (property.NameEquals("action") || property.NameEquals("tool") || property.NameEquals("tool_name") || property.NameEquals("name") || property.NameEquals("resource"))
                {
                    continue;
                }

                filtered[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
            }

            if (filtered.Count > 0)
            {
                return JsonSerializer.SerializeToElement(filtered);
            }

            return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
        }

        private async Task<string> InvokeServerToolAsync(McpServerProcess serverProcess, string toolName, JsonElement toolArguments)
        {
            var responseJson = await SendJsonRpcRequestAsync(serverProcess, "tools/call", new
            {
                name = toolName,
                arguments = toolArguments
            });

            using var responseDoc = JsonDocument.Parse(responseJson);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                return ToolError(GetJsonString(error, "message") ?? error.GetRawText());
            }

            if (!root.TryGetProperty("result", out var result))
            {
                return responseJson;
            }

            if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                var serverMessage = ExtractResponseText(result);
                if (serverProcess.ServerName.Equals("everart", StringComparison.OrdinalIgnoreCase)
                    && serverMessage.Contains("out of credits", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonSerializer.Serialize(new
                    {
                        ok = false,
                        server = serverProcess.ServerName,
                        tool = toolName,
                        error = "EverArt billing is exhausted. Add credits or upgrade the EverArt plan, then try again.",
                        details = serverMessage
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    server = serverProcess.ServerName,
                    tool = toolName,
                    error = serverMessage,
                    result
                });
            }

            var contentText = ExtractResponseText(result);
            if (!string.IsNullOrWhiteSpace(contentText))
            {
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    server = serverProcess.ServerName,
                    tool = toolName,
                    content = contentText
                });
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                server = serverProcess.ServerName,
                tool = toolName,
                result
            });
        }

        private async Task<string> SendJsonRpcRequestAsync(McpServerProcess serverProcess, string method, object parameters)
        {
            await serverProcess.RequestLock.WaitAsync();
            try
            {
                var requestId = Interlocked.Increment(ref serverProcess.NextRequestId);
                var requestJson = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    method,
                    @params = parameters
                });

                await serverProcess.StdinWriter.WriteLineAsync(requestJson);
                await serverProcess.StdinWriter.FlushAsync();

                while (true)
                {
                    var readTask = serverProcess.StdoutReader.ReadLineAsync();
                    var completed = await Task.WhenAny(readTask, Task.Delay(30000));
                    if (completed != readTask)
                    {
                        throw new TimeoutException($"Timed out waiting for MCP server '{serverProcess.ServerName}' response to '{method}'.");
                    }

                    var line = await readTask;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (TryGetJsonRpcResponseId(line, out var responseId) && responseId == requestId)
                    {
                        return line;
                    }
                }
            }
            finally
            {
                serverProcess.RequestLock.Release();
            }
        }

        private async Task SendJsonRpcNotificationAsync(McpServerProcess serverProcess, string method, object parameters)
        {
            var notificationJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters
            });

            await serverProcess.StdinWriter.WriteLineAsync(notificationJson);
            await serverProcess.StdinWriter.FlushAsync();
        }

        private static bool TryGetJsonRpcResponseId(string json, out int responseId)
        {
            responseId = 0;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    return false;
                }

                if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out responseId))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string? GetJsonString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static string ExtractResponseText(JsonElement result)
        {
            if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var texts = content
                    .EnumerateArray()
                    .Select(item =>
                        item.TryGetProperty("type", out var typeElement) &&
                        typeElement.ValueKind == JsonValueKind.String &&
                        typeElement.GetString() == "text" &&
                        item.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String
                            ? textElement.GetString()
                            : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                if (texts.Count > 0)
                {
                    return string.Join(Environment.NewLine, texts!);
                }
            }

            if (result.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        #region Filesystem Tool Implementations

        private string ExecuteListFiles(JsonElement args)
        {
            var pathArg = GetArgString(args, "path", Environment.CurrentDirectory);
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
            var pathArg = GetArgString(args, "path", Environment.CurrentDirectory);
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

        #endregion

        #region Helpers

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

        #endregion
    }

    /// <summary>
    /// Represents an active MCP server process communicating via stdio.
    /// </summary>
    public class McpServerProcess : IDisposable
    {
        public string ServerName { get; set; }
        public Process? Process { get; set; }
        public StreamWriter? StdinWriter { get; set; }
        public StreamReader? StdoutReader { get; set; }
        public SemaphoreSlim RequestLock { get; } = new SemaphoreSlim(1, 1);
        public int NextRequestId;
        public List<McpToolMetadata>? CachedTools { get; set; }

        public void Dispose()
        {
            RequestLock.Dispose();
            StdinWriter?.Dispose();
            StdoutReader?.Dispose();
            Process?.Dispose();
        }
    }

    public class McpToolMetadata
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public JsonElement? InputSchema { get; set; }
    }
}
