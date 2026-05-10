using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace NimChatGui
{
    public class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Thinking { get; set; }
        public bool IsStreaming { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int CharacterCount => Content?.Length ?? 0;
        public int WordCount => string.IsNullOrWhiteSpace(Content)
            ? 0
            : Content.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public sealed class UiSettings
    {
        public string WhisperEndpoint { get; set; } = "";
        public bool EnableVoice { get; set; }
        public bool ShowTokens { get; set; } = true;
        public bool ShowContext { get; set; } = true;
        public bool AutoScroll { get; set; } = true;
        public bool ShowTimeAgo { get; set; } = true;
        public string ApiEndpoint { get; set; } = "https://integrate.api.nvidia.com/v1/";
    }

    public class MainWindow : Window
    {
        private const string SecretsFileName = "api-secrets.json";
        private readonly NimApiClient _apiClient;
        private readonly McpCatalogClient _mcpCatalog;
        private readonly McpToolGenerator _toolGenerator;
        private readonly McpToolExecutor _toolExecutor;
        private readonly WebView2 _webView;

        private readonly List<ChatMessage> _messages = new();
        private readonly List<ModelInfo> _models = new();

        private string _selectedModelId = "";
        private bool _isBusy;
        private bool _isWebUiReady;
        private UiSettings _settings = new();

        public MainWindow()
        {
            Title = "NIM Chat";
            Width = 1220;
            Height = 860;
            MinWidth = 760;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _apiClient = new NimApiClient();
            _mcpCatalog = new McpCatalogClient();
            _toolGenerator = new McpToolGenerator(_mcpCatalog);
            _toolExecutor = new McpToolExecutor(_mcpCatalog);
            _webView = new WebView2();

            Content = _webView;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadApiSecrets();

            var apiKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _apiClient.NvidiaApiKey = apiKey;
            }

            var hfKey = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY")
                ?? Environment.GetEnvironmentVariable("HF_TOKEN");
            if (!string.IsNullOrWhiteSpace(hfKey))
            {
                _apiClient.HuggingFaceApiKey = hfKey;
            }

            var replicateKey = Environment.GetEnvironmentVariable("REPLICATE_API_KEY");
            if (!string.IsNullOrWhiteSpace(replicateKey))
            {
                _apiClient.ReplicateApiKey = replicateKey;
            }

            LoadSettings();
            AddDefaultModels();
            _selectedModelId = _models.FirstOrDefault()?.Id ?? "";

            await InitializeWebViewAsync();
        }

        private void LoadSettings()
        {
            var props = Application.Current.Properties;

            _settings.WhisperEndpoint = props["WhisperEndpoint"] as string ?? "";
            _settings.EnableVoice = props["EnableVoice"] is bool enableVoice && enableVoice;
            _settings.ShowTokens = props["ShowTokens"] is not bool showTokens || showTokens;
            _settings.ShowContext = props["ShowContext"] is not bool showContext || showContext;
            _settings.AutoScroll = props["AutoScroll"] is not bool autoScroll || autoScroll;
            _settings.ShowTimeAgo = props["ShowTimeAgo"] is not bool showTimeAgo || showTimeAgo;
            _settings.ApiEndpoint = props["ApiEndpoint"] as string ?? _settings.ApiEndpoint;

            if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint))
            {
                _apiClient.BaseUrl = _settings.ApiEndpoint.TrimEnd('/') + "/";
            }
        }

        private void SaveSettings(UiSettings updated)
        {
            _settings = updated;

            var props = Application.Current.Properties;
            props["WhisperEndpoint"] = _settings.WhisperEndpoint;
            props["EnableVoice"] = _settings.EnableVoice;
            props["ShowTokens"] = _settings.ShowTokens;
            props["ShowContext"] = _settings.ShowContext;
            props["AutoScroll"] = _settings.AutoScroll;
            props["ShowTimeAgo"] = _settings.ShowTimeAgo;
            props["ApiEndpoint"] = _settings.ApiEndpoint;

            if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint))
            {
                _apiClient.BaseUrl = _settings.ApiEndpoint.TrimEnd('/') + "/";
            }
        }

        private async Task InitializeWebViewAsync()
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var uiPath = ResolveUiEntryPath();
            if (!string.IsNullOrWhiteSpace(uiPath))
            {
                _webView.Source = new Uri(uiPath);
                return;
            }

            _webView.NavigateToString("<html><body style='font-family:Segoe UI;background:#111;color:#fff;padding:20px'>Web UI not found. Expected Assets/WebUi/index.html in output.</body></html>");
        }

        private static string ResolveUiEntryPath()
        {
            var candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "WebUi", "index.html"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "WebUi", "index.html")),
                Path.Combine(Environment.CurrentDirectory, "Assets", "WebUi", "index.html")
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                var type = root.GetProperty("type").GetString() ?? "";
                var payload = root.TryGetProperty("payload", out var parsedPayload) ? parsedPayload : default;

                switch (type)
                {
                    case "appReady":
                        _isWebUiReady = true;
                        if (_messages.Count == 0)
                        {
                            AddMessage("System", "WebView2 UI loaded. Type /help for command list.");
                        }
                        await SendFullStateAsync("Ready");
                        break;

                    case "sendMessage":
                        {
                            var text = GetString(payload, "text");
                            var modelId = GetString(payload, "modelId");
                            await HandleSendMessageAsync(text, modelId);
                            break;
                        }

                    case "clearChat":
                        _messages.Clear();
                        AddMessage("System", "Conversation cleared.");
                        await SendFullStateAsync("Chat cleared");
                        break;

                    case "newChat":
                        _messages.Clear();
                        AddMessage("System", "New conversation started.");
                        await SendFullStateAsync("New chat");
                        break;

                    case "refreshModels":
                        await RefreshModelsAsync();
                        break;

                    case "selectModel":
                        {
                            var modelId = GetString(payload, "modelId");
                            if (_models.Any(m => m.Id == modelId))
                            {
                                _selectedModelId = modelId;
                                AddMessage("System", $"Switched to model: {modelId}");
                                await SendFullStateAsync("Model changed");
                            }
                            break;
                        }

                    case "exportChat":
                        ExportChat();
                        break;

                    case "getSettings":
                        await SendFullStateAsync("Ready");
                        break;

                    case "saveSettings":
                        {
                            var incoming = ParseSettings(payload);
                            SaveSettings(incoming);
                            AddMessage("System", "Settings saved.");
                            await SendFullStateAsync("Settings saved");
                            break;
                        }

                    case "getMcpCatalog":
                        await SendMcpCatalogStateAsync();
                        break;

                    case "toggleMcpServer":
                        {
                            var serverName = GetString(payload, "name");
                            await ToggleMcpServerAsync(serverName);
                            break;
                        }

                    case "executeTerminalCommand":
                        {
                            var command = GetString(payload, "command");
                            await ExecuteTerminalCommandAsync(command);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                AddMessage("System", $"Bridge error: {ex.Message}");
                await SendFullStateAsync("Bridge error");
            }
        }

        private static string GetString(JsonElement payload, string name)
        {
            return payload.ValueKind != JsonValueKind.Undefined
                && payload.TryGetProperty(name, out var prop)
                ? prop.GetString() ?? ""
                : "";
        }

        private UiSettings ParseSettings(JsonElement payload)
        {
            var current = _settings;
            if (payload.ValueKind == JsonValueKind.Undefined)
            {
                return current;
            }

            return new UiSettings
            {
                WhisperEndpoint = GetString(payload, "whisperEndpoint"),
                ApiEndpoint = string.IsNullOrWhiteSpace(GetString(payload, "apiEndpoint"))
                    ? current.ApiEndpoint
                    : GetString(payload, "apiEndpoint"),
                EnableVoice = payload.TryGetProperty("enableVoice", out var enableVoice) && enableVoice.GetBoolean(),
                ShowTokens = !payload.TryGetProperty("showTokens", out var showTokens) || showTokens.GetBoolean(),
                ShowContext = !payload.TryGetProperty("showContext", out var showContext) || showContext.GetBoolean(),
                AutoScroll = !payload.TryGetProperty("autoScroll", out var autoScroll) || autoScroll.GetBoolean(),
                ShowTimeAgo = !payload.TryGetProperty("showTimeAgo", out var showTimeAgo) || showTimeAgo.GetBoolean()
            };
        }

        private async Task HandleSendMessageAsync(string userText, string modelId)
        {
            if (_isBusy || string.IsNullOrWhiteSpace(userText))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(modelId))
            {
                _selectedModelId = modelId;
            }

            if (TryHandleSlashCommand(userText))
            {
                await SendFullStateAsync("Command handled");
                return;
            }

            _isBusy = true;
            AddMessage("You", userText);
            var assistantDraft = new ChatMessage
            {
                Role = "NIM Assistant",
                Content = string.Empty,
                Thinking = null,
                IsStreaming = true,
                Timestamp = DateTime.Now
            };
            _messages.Add(assistantDraft);

            var lastUiUpdate = DateTime.UtcNow;
            void OnStreamUpdate(string partialContent, string? partialThinking)
            {
                assistantDraft.Content = partialContent ?? string.Empty;
                assistantDraft.Thinking = partialThinking;

                var now = DateTime.UtcNow;
                if ((now - lastUiUpdate).TotalMilliseconds >= 120)
                {
                    lastUiUpdate = now;
                    _ = SendFullStateAsync("Generating...");
                }
            }

            await SendFullStateAsync("Generating...");

            try
            {
                var (response, thinking) = await SendMessageWithFilesystemToolsAsync(userText, OnStreamUpdate);
                assistantDraft.Content = response;
                assistantDraft.Thinking = thinking;
                assistantDraft.IsStreaming = false;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(assistantDraft.Content) && string.IsNullOrWhiteSpace(assistantDraft.Thinking))
                {
                    _messages.Remove(assistantDraft);
                }
                AddMessage("System", $"Error: {ex.Message}");
            }
            finally
            {
                assistantDraft.IsStreaming = false;
                _isBusy = false;
                await SendFullStateAsync("Ready");
            }
        }

        private async Task<(string response, string? thinking)> SendMessageWithFilesystemToolsAsync(
            string userText,
            Action<string, string?>? onStreamUpdate = null)
        {
            if (IsImageGenerationModel(_selectedModelId))
            {
                var (ok, message, imageUrl) = await _apiClient.GenerateImageAsync(_selectedModelId, userText);
                if (!ok || string.IsNullOrWhiteSpace(imageUrl))
                {
                    return (message, null);
                }

                var imageResponse = $"{message}\n\n![Generated image]({imageUrl})";
                onStreamUpdate?.Invoke(imageResponse, null);
                return (imageResponse, null);
            }

            var tools = BuildFilesystemTools();
            if (tools.Count == 0)
            {
                var simpleMessages = new List<ApiChatMessage>
                {
                    new ApiChatMessage { Role = "user", Content = userText }
                };

                var (directResponse, directThinking, _) = await _apiClient.SendMessageWithToolsStreamingAsync(
                    _selectedModelId,
                    simpleMessages,
                    null,
                    "none",
                    onStreamUpdate);

                return (string.IsNullOrWhiteSpace(directResponse) ? "No response" : directResponse, directThinking);
            }

            var messages = new List<ApiChatMessage>
            {
                new ApiChatMessage
                {
                    Role = "system",
                    Content = "When tool results are available, provide a direct final answer. Avoid repeating the same tool call with identical arguments unless the user explicitly requests a rerun."
                },
                new ApiChatMessage { Role = "user", Content = userText }
            };

            var seenCalls = new HashSet<string>(StringComparer.Ordinal);
            var lastToolResults = new List<string>();
            var latestResponse = string.Empty;
            string? latestThinking = null;

            for (var i = 0; i < 8; i++)
            {
                var (response, thinking, toolCalls) = await _apiClient.SendMessageWithToolsStreamingAsync(
                    _selectedModelId,
                    messages,
                    tools,
                    "auto",
                    onStreamUpdate);

                latestResponse = response ?? string.Empty;
                latestThinking = thinking;

                if (toolCalls == null || toolCalls.Count == 0)
                {
                    return (string.IsNullOrWhiteSpace(latestResponse) ? "No response" : latestResponse, latestThinking);
                }

                messages.Add(new ApiChatMessage
                {
                    Role = "assistant",
                    Content = latestResponse,
                    ToolCalls = toolCalls
                });

                var thisRoundRepeatedOnly = true;
                foreach (var toolCall in toolCalls)
                {
                    var signature = $"{toolCall.Function?.Name}:{toolCall.Function?.Arguments}";
                    if (seenCalls.Add(signature))
                    {
                        thisRoundRepeatedOnly = false;
                    }

                    var toolResult = await ExecuteFilesystemToolAsync(toolCall);
                    lastToolResults.Add(toolResult);
                    messages.Add(new ApiChatMessage
                    {
                        Role = "tool",
                        Content = toolResult,
                        ToolCallId = toolCall.Id
                    });
                }

                if (thisRoundRepeatedOnly && i >= 1)
                {
                    break;
                }
            }

            if (lastToolResults.Count > 0)
            {
                var lastResult = TruncateForUi(lastToolResults[^1], 2200);
                var prefix = string.IsNullOrWhiteSpace(latestResponse)
                    ? "I executed filesystem tools, but the model kept requesting duplicate calls."
                    : latestResponse;
                return ($"{prefix}\n\nLatest tool result:\n{lastResult}", latestThinking);
            }

            return ("I could not complete filesystem tool execution for this request. Try a more specific file path or command.", null);
        }

        private static bool IsImageGenerationModel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            var id = modelId.ToLowerInvariant();
            return id.Contains("flux")
                || id.StartsWith("pollination/")
                || id.Contains("image")
                || id.Contains("vision-gen");
        }

        private List<ToolDefinition> BuildFilesystemTools()
        {
            // Dynamically generate tools from installed MCP servers
            var tools = _toolGenerator.GenerateToolsForInstalledServers();
            
            // If no servers installed, provide helpful message and return empty
            if (tools.Count == 0)
            {
                AddMessage("System", "No MCP servers installed. Open the MCP panel (/tools) to install servers and unlock additional tools for the agent.");
            }

            return tools;
        }

        private Task<string> ExecuteFilesystemToolAsync(ToolCall toolCall)
        {
            try
            {
                // Delegate to the MCP tool executor which routes to appropriate handlers
                return _toolExecutor.ExecuteToolAsync(toolCall);
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolError($"Tool execution failed: {ex.Message}"));
            }
        }

        private static string ToolError(string message)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = message
            });
        }

        private string GetFilesystemRootPath()
        {
            var installed = _mcpCatalog.GetInstalledServers();
            var filesystem = installed.FirstOrDefault(s =>
                s.Name.Equals("filesystem", StringComparison.OrdinalIgnoreCase)
                && s.Args != null
                && s.Args.Count >= 3
                && Directory.Exists(s.Args[2]));

            if (filesystem != null)
            {
                return Path.GetFullPath(filesystem.Args[2]);
            }

            return Path.GetFullPath(Environment.CurrentDirectory);
        }

        private async Task ExecuteTerminalCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                await PostToWebAsync(new
                {
                    type = "terminalResult",
                    payload = new
                    {
                        ok = false,
                        command = string.Empty,
                        output = "No command provided.",
                        exitCode = -1
                    }
                });
                return;
            }

            var root = GetFilesystemRootPath();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var exitTask = process.WaitForExitAsync();

                var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(60)));
                if (completed != exitTask)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }

                    await PostToWebAsync(new
                    {
                        type = "terminalResult",
                        payload = new
                        {
                            ok = false,
                            command,
                            output = "Command timed out after 60 seconds.",
                            exitCode = -1
                        }
                    });
                    return;
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var combined = string.IsNullOrWhiteSpace(stderr)
                    ? stdout
                    : string.IsNullOrWhiteSpace(stdout)
                        ? stderr
                        : stdout + Environment.NewLine + stderr;

                await PostToWebAsync(new
                {
                    type = "terminalResult",
                    payload = new
                    {
                        ok = process.ExitCode == 0,
                        command,
                        output = TruncateForUi(combined, 12000),
                        exitCode = process.ExitCode
                    }
                });
            }
            catch (Exception ex)
            {
                await PostToWebAsync(new
                {
                    type = "terminalResult",
                    payload = new
                    {
                        ok = false,
                        command,
                        output = $"Terminal execution error: {ex.Message}",
                        exitCode = -1
                    }
                });
            }
        }

        private static string TruncateForUi(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text;
            }

            return text[..maxChars] + "\n...[truncated]";
        }

        private bool TryHandleSlashCommand(string input)
        {
            if (!input.StartsWith("/"))
            {
                return false;
            }

            var parts = input.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1].Trim() : "";
            var arg2 = parts.Length > 2 ? parts[2].Trim() : "";

            switch (cmd)
            {
                case "/clear":
                    _messages.Clear();
                    AddMessage("System", "Conversation cleared.");
                    break;
                case "/new":
                    _messages.Clear();
                    AddMessage("System", "New conversation started.");
                    break;
                case "/help":
                    AddMessage("System", BuildCommandHelp());
                    break;
                case "/model":
                    if (!string.IsNullOrWhiteSpace(arg) && _models.Any(m => m.Id == arg))
                    {
                        _selectedModelId = arg;
                        AddMessage("System", $"Switched to model: {_selectedModelId}");
                    }
                    else
                    {
                        AddMessage("System", "Usage: /model <model-id>");
                    }
                    break;
                case "/tokens":
                    AddMessage("System",
                        $"Usage Statistics:\nMessages: {_messages.Count}\n"
                        + $"Total Characters: {_messages.Sum(m => m.CharacterCount)}\n"
                        + $"Total Words: {_messages.Sum(m => m.WordCount)}");
                    break;
                case "/export":
                    ExportChat();
                    break;
                case "/voice":
                    _settings.EnableVoice = string.IsNullOrWhiteSpace(arg) || arg.Equals("on", StringComparison.OrdinalIgnoreCase);
                    SaveSettings(_settings);
                    AddMessage("System", $"Voice input: {(_settings.EnableVoice ? "enabled" : "disabled")}");
                    break;
                case "/settings":
                    AddMessage("System", "Open the Settings panel in the sidebar toolbar.");
                    break;
                case "/tools":
                    AddMessage("System", "Open the MCP panel to manage tools/servers.");
                    break;
                case "/setapikey":
                    HandleSetApiKeyCommand(arg, arg2);
                    break;
                default:
                    AddMessage("System", $"Unknown command: {cmd}. Type /help for available commands.");
                    break;
            }

            return true;
        }

        private void HandleSetApiKeyCommand(string serviceName, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(apiKey))
            {
                AddMessage("System", "Usage: /setapikey <service> <api-key>\nExample: /setapikey replicate r8_abc123xyz");
                return;
            }

            var normalizedService = serviceName.Trim().ToLowerInvariant();
            if (normalizedService is "huggingface" or "hf")
            {
                _apiClient.HuggingFaceApiKey = apiKey;
                SaveApiSecrets();
                AddMessage("System", $"✓ Hugging Face API key applied for this session.");
                return;
            }

            if (normalizedService is "nvidia" or "nim")
            {
                _apiClient.NvidiaApiKey = apiKey;
                SaveApiSecrets();
                AddMessage("System", "✓ NVIDIA API key applied for this session.");
                return;
            }



            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var configPath = Path.Combine(appData, "NimChatGui", "mcp", "installed-servers.json");
                var configDir = Path.GetDirectoryName(configPath);

                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                List<McpServerDefinition> servers = new();
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        servers = JsonSerializer.Deserialize<List<McpServerDefinition>>(json) ?? new();
                    }
                }

                var server = servers.FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                if (server == null)
                {
                    AddMessage("System", $"Service '{serviceName}' not found in MCP catalog. Install it from the /tools panel first.");
                    return;
                }

                // Update or create env dict
                if (server.Env == null)
                {
                    server.Env = new Dictionary<string, string>();
                }

                // Determine the env key based on service name
                var envKey = serviceName.ToUpperInvariant() switch
                {
                    "EVERART" => "EVERART_API_KEY",
                    "BRAVE-SEARCH" => "BRAVE_API_KEY",
                    "GITHUB" => "GITHUB_TOKEN",
                    "GITLAB" => "GITLAB_TOKEN",
                    "SLACK" => "SLACK_BOT_TOKEN",
                    "DISCORD" => "DISCORD_BOT_TOKEN",
                    "LINEAR" => "LINEAR_API_KEY",
                    "SENTRY" => "SENTRY_AUTH_TOKEN",
                    "GOOGLE-MAPS" => "GOOGLE_MAPS_API_KEY",
                    "GOOGLE-WORKSPACE" => "GOOGLE_SERVICE_ACCOUNT_KEY",
                    "AZURE-DEVOPS" => "AZURE_DEVOPS_PAT",
                    "AWS-SDK-CORE" => "AWS_ACCESS_KEY_ID",
                    "FIRECRAWL" => "FIRECRAWL_API_KEY",
                    "IP2LOCATION" => "IP2LOCATION_API_KEY",
                    "IP2WHOIS" => "IP2WHOIS_API_KEY",
                    "FINANCIAL-DATASETS" => "FINANCIAL_DATASETS_API_KEY",
                    "HUGGINGFACE" => "HUGGINGFACE_API_KEY",
                    "CONTEXT7" => "CONTEXT7_TOKEN",
                    "METRICOOL" => "METRICOOL_API_KEY",
                    "PDFCROWD" => "PDFCROWD_API_KEY",
                    _ => serviceName.ToUpperInvariant() + "_API_KEY"
                };

                server.Env[envKey] = apiKey;
                server.IsInstalled = true;

                // Save back to file
                var json_out = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json_out);

                AddMessage("System", $"✓ API key for '{serviceName}' saved successfully.\n({envKey} = {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...)");
            }
            catch (Exception ex)
            {
                AddMessage("System", $"Error saving API key: {ex.Message}");
            }
        }

        private string BuildCommandHelp()
        {
            return "Commands:\n"
                + "/clear - Clear chat\n"
                + "/new - New conversation\n"
                + "/export - Export chat\n"
                + "/model <id> - Switch model\n"
                + "/tokens - Usage stats\n"
                + "/voice [on|off] - Toggle voice\n"
                + "/setapikey <service> <key> - Configure API key\n"
                + "  services: nvidia, huggingface, or MCP service name\n"
                + "/settings - Open settings panel\n"
                + "/tools - Open MCP panel\n"
                + "/help - Show help";
        }

        private async Task RefreshModelsAsync()
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;
            await SendFullStateAsync("Refreshing model catalog...");

            try
            {
                var remoteModels = await _apiClient.GetNvidiaRemoteCatalogAsync();
                if (remoteModels.Count > 0)
                {
                    _models.Clear();
                    _models.AddRange(remoteModels);
                    EnsureEssentialModelsPresent();
                    if (!_models.Any(m => m.Id == _selectedModelId))
                    {
                        _selectedModelId = _models[0].Id;
                    }
                    AddMessage("System", $"Loaded {_models.Count} models from NVIDIA catalog.");
                }
                else
                {
                    AddMessage("System", "No models received from NVIDIA. Check API key/network and try again.");
                }
            }
            catch (Exception ex)
            {
                AddMessage("System", $"Model refresh error: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
                await SendFullStateAsync("Ready");
            }
        }

        private async Task ToggleMcpServerAsync(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return;
            }

            var catalog = await _mcpCatalog.FetchCatalogAsync();
            var server = catalog.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (server == null)
            {
                return;
            }

            if (server.IsInstalled)
            {
                _mcpCatalog.UninstallServer(server);
                AddMessage("System", $"Removed MCP server: {server.Name}");
            }
            else
            {
                _mcpCatalog.InstallServer(server);
                AddMessage("System", $"Installed MCP server: {server.Name}");
            }

            await SendFullStateAsync("MCP updated");
        }

        private void AddDefaultModels()
        {
            _models.Clear();
            _models.AddRange(new[]
            {
                new ModelInfo { Id = "meta/llama-3.1-8b-instruct", Name = "Llama 3.1 8B", Owner = "Meta", Parameters = 8_000_000_000, SupportsToolCalling = true, Description = "Fast, efficient instruction-following model" },
                new ModelInfo { Id = "meta/llama-3.1-70b-instruct", Name = "Llama 3.1 70B", Owner = "Meta", Parameters = 70_000_000_000, SupportsToolCalling = true, Description = "High-performance instruction-following model" },
                new ModelInfo { Id = "mistralai/mistral-7b-instruct-v0.3", Name = "Mistral 7B", Owner = "Mistral", Parameters = 7_000_000_000, SupportsToolCalling = true, Description = "Compact and capable model" },
                new ModelInfo { Id = "nvidia/llama-3.1-nemotron-70b-instruct", Name = "Nemotron 70B", Owner = "NVIDIA", Parameters = 70_000_000_000, SupportsToolCalling = true, IsRecommended = true, Description = "NVIDIA optimized Llama for best performance" },
                new ModelInfo { Id = "google/gemma-2-2b-it", Name = "Gemma 2 2B", Owner = "Google", Parameters = 2_000_000_000, Description = "Small but capable model" },
                new ModelInfo { Id = "meta/llama-3.3-70b-instruct", Name = "Llama 3.3 70B", Owner = "Meta", Parameters = 70_000_000_000, SupportsToolCalling = true, SupportsThinking = true, Description = "Latest Llama with reasoning capabilities" },
                new ModelInfo { Id = "deepseek-ai/DeepSeek-R1", Name = "DeepSeek R1", Owner = "DeepSeek", Parameters = 70_000_000_000, SupportsToolCalling = true, SupportsThinking = true, Description = "Advanced reasoning model" },
                new ModelInfo { Id = "black-forest-labs/flux.1-dev", Name = "FLUX.1 Dev", Owner = "Black Forest Labs", Description = "Ultra high-quality photorealistic text-to-image generation" },
                new ModelInfo { Id = "black-forest-labs/flux.1-schnell", Name = "FLUX.1 Schnell", Owner = "Black Forest Labs", IsRecommended = true, Description = "High-quality fast text-to-image generation (recommended for best quality)" },
                new ModelInfo { Id = "pollination/turbo", Name = "Pollination AI Turbo", Owner = "Pollination AI", Description = "Free high-speed image generation (no API key required)" },
                new ModelInfo { Id = "pollination/flux", Name = "Pollination AI Flux Uncensored", Owner = "Pollination AI", Description = "Free uncensored-style image generation using Pollination Flux" }
            });

            EnsureEssentialModelsPresent();
        }

        private void EnsureEssentialModelsPresent()
        {
            var essentialModels = new[]
            {
                new ModelInfo { Id = "black-forest-labs/flux.1-dev", Name = "FLUX.1 Dev", Owner = "Black Forest Labs", Description = "Ultra high-quality photorealistic text-to-image generation" },
                new ModelInfo { Id = "black-forest-labs/flux.1-schnell", Name = "FLUX.1 Schnell", Owner = "Black Forest Labs", IsRecommended = true, Description = "High-quality fast text-to-image generation" },
                new ModelInfo { Id = "pollination/turbo", Name = "Pollination AI Turbo", Owner = "Pollination AI", Description = "Free high-speed image generation (no API key required)" },
                new ModelInfo { Id = "pollination/flux", Name = "Pollination AI Flux Uncensored", Owner = "Pollination AI", Description = "Free uncensored-style image generation using Pollination Flux" }
            };

            foreach (var model in essentialModels)
            {
                if (_models.Any(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _models.Add(model);
            }
        }

        private void AddMessage(string role, string content, string? thinking = null)
        {
            _messages.Add(new ChatMessage
            {
                Role = role,
                Content = content,
                Thinking = thinking,
                Timestamp = DateTime.Now
            });
        }

        private static string GetSecretsFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "NimChatGui");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return Path.Combine(dir, SecretsFileName);
        }

        private void LoadApiSecrets()
        {
            try
            {
                var path = GetSecretsFilePath();
                if (!File.Exists(path))
                {
                    return;
                }

                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("nvidiaApiKey", out var nvidiaKey) && nvidiaKey.ValueKind == JsonValueKind.String)
                {
                    _apiClient.NvidiaApiKey = nvidiaKey.GetString() ?? _apiClient.NvidiaApiKey;
                }

                if (root.TryGetProperty("huggingFaceApiKey", out var hfKey) && hfKey.ValueKind == JsonValueKind.String)
                {
                    _apiClient.HuggingFaceApiKey = hfKey.GetString() ?? _apiClient.HuggingFaceApiKey;
                }

                if (root.TryGetProperty("replicateApiKey", out var replicateKey) && replicateKey.ValueKind == JsonValueKind.String)
                {
                    _apiClient.ReplicateApiKey = replicateKey.GetString() ?? _apiClient.ReplicateApiKey;
                }
            }
            catch (Exception ex)
            {
                AddMessage("System", $"Warning: could not load saved API secrets: {ex.Message}");
            }
        }

        private void SaveApiSecrets()
        {
            try
            {
                var path = GetSecretsFilePath();
                var payload = new
                {
                    nvidiaApiKey = _apiClient.NvidiaApiKey,
                    huggingFaceApiKey = _apiClient.HuggingFaceApiKey,
                    replicateApiKey = _apiClient.ReplicateApiKey
                };

                File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                AddMessage("System", $"Warning: could not save API secrets: {ex.Message}");
            }
        }

        private async Task SendMcpCatalogStateAsync()
        {
            var catalog = await _mcpCatalog.FetchCatalogAsync();
            await PostToWebAsync(new
            {
                type = "mcpCatalog",
                payload = new
                {
                    servers = catalog.Select(s => new
                    {
                        name = s.Name,
                        description = s.Description,
                        category = s.Category,
                        author = s.Author,
                        installed = s.IsInstalled
                    })
                }
            });
        }

        private async Task SendFullStateAsync(string status)
        {
            if (!_isWebUiReady)
            {
                return;
            }

            var payload = new
            {
                type = "state",
                payload = new
                {
                    status,
                    busy = _isBusy,
                    selectedModelId = _selectedModelId,
                    settings = _settings,
                    models = _models.Select(m => new
                    {
                        id = m.Id,
                        name = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
                        owner = m.Owner,
                        recommended = m.IsRecommended,
                        image = IsImageGenerationModel(m.Id),
                        tools = m.SupportsToolCalling,
                        thinking = m.SupportsThinking
                    }),
                    messages = _messages.Select(m => new
                    {
                        role = m.Role,
                        content = m.Content,
                        thinking = m.Thinking,
                        streaming = m.IsStreaming,
                        timestamp = m.Timestamp
                    })
                }
            };

            await PostToWebAsync(payload);
            await SendMcpCatalogStateAsync();
        }

        private async Task PostToWebAsync(object message)
        {
            if (_webView.CoreWebView2 == null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(message);
            var script = $"window.dispatchHostMessage({json});";
            await _webView.ExecuteScriptAsync(script);
        }

        private void ExportChat()
        {
            try
            {
                if (_messages.Count == 0)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|Markdown files (*.md)|*.md",
                    DefaultExt = ".txt",
                    FileName = $"chat_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"# Chat Export - {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine();

                foreach (var msg in _messages)
                {
                    sb.AppendLine($"**{msg.Role}** ({msg.Timestamp:yyyy-MM-dd HH:mm:ss})");
                    if (!string.IsNullOrWhiteSpace(msg.Thinking))
                    {
                        sb.AppendLine($"> Thinking: {msg.Thinking}");
                    }
                    sb.AppendLine(msg.Content);
                    sb.AppendLine();
                }

                File.WriteAllText(dialog.FileName, sb.ToString());
            }
            catch (Exception ex)
            {
                AddMessage("System", $"Export failed: {ex.Message}");
                _ = SendFullStateAsync("Export failed");
            }
        }
    }
}
