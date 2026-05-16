using System.Text;
using System.Text.Json;

namespace NimChatGui;

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

public partial class MainPage : ContentPage
{
    private const string SecretsFileName = "api-secrets.json";
    private const string ConversationMemoryFileName = "conversation-memory.json";
    private const int MaxMemoryMessages = 60;
    private readonly NimApiClient _apiClient;
    private readonly McpCatalogClient _mcpCatalog;
    private readonly McpToolGenerator _toolGenerator;
    private readonly McpToolExecutor _toolExecutor;

    private readonly List<ChatMessage> _messages = new();
    private readonly List<ModelInfo> _models = new();

    private string _selectedModelId = "";
    private bool _isBusy;
    private bool _isWebUiReady;
    private UiSettings _settings = new();

    public MainPage()
    {
        InitializeComponent();

        _apiClient = new NimApiClient();
        _mcpCatalog = new McpCatalogClient();
        _toolGenerator = new McpToolGenerator(_mcpCatalog);
        _toolExecutor = new McpToolExecutor(_mcpCatalog);

        ChatWebView.Navigating += OnWebViewNavigating;
        ChatWebView.Navigated += OnWebViewNavigated;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        LoadApiSecrets();
        LoadConversationMemory();

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

        await LoadWebViewAsync();
    }

    private void LoadSettings()
    {
        _settings.WhisperEndpoint = Preferences.Get("WhisperEndpoint", "");
        _settings.EnableVoice = Preferences.Get("EnableVoice", false);
        _settings.ShowTokens = Preferences.Get("ShowTokens", true);
        _settings.ShowContext = Preferences.Get("ShowContext", true);
        _settings.AutoScroll = Preferences.Get("AutoScroll", true);
        _settings.ShowTimeAgo = Preferences.Get("ShowTimeAgo", true);
        _settings.ApiEndpoint = Preferences.Get("ApiEndpoint", "https://integrate.api.nvidia.com/v1/");

        if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint))
        {
            _apiClient.BaseUrl = _settings.ApiEndpoint.TrimEnd('/') + "/";
        }
    }

    private void SaveSettings(UiSettings updated)
    {
        _settings = updated;

        Preferences.Set("WhisperEndpoint", _settings.WhisperEndpoint);
        Preferences.Set("EnableVoice", _settings.EnableVoice);
        Preferences.Set("ShowTokens", _settings.ShowTokens);
        Preferences.Set("ShowContext", _settings.ShowContext);
        Preferences.Set("AutoScroll", _settings.AutoScroll);
        Preferences.Set("ShowTimeAgo", _settings.ShowTimeAgo);
        Preferences.Set("ApiEndpoint", _settings.ApiEndpoint);

        if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint))
        {
            _apiClient.BaseUrl = _settings.ApiEndpoint.TrimEnd('/') + "/";
        }
    }

    private async Task LoadWebViewAsync()
    {
        var uiPath = ResolveUiEntryPath();
        if (!string.IsNullOrWhiteSpace(uiPath) && File.Exists(uiPath))
        {
            ChatWebView.Source = new HtmlWebViewSource
            {
                Html = File.ReadAllText(uiPath)
            };
            return;
        }

        // Try loading from embedded raw assets
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("WebUi/index.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            ChatWebView.Source = new HtmlWebViewSource { Html = html };
            return;
        }
        catch
        {
        }

        ChatWebView.Source = new HtmlWebViewSource
        {
            Html = "<html><body style='font-family:sans-serif;background:#0a101f;color:#fff;padding:20px'>Loading Web UI...</body></html>"
        };
    }

    private static string ResolveUiEntryPath()
    {
        var candidates = new[]
        {
            Path.Combine(FileSystem.AppDataDirectory, "WebUi", "index.html"),
            Path.Combine(Environment.CurrentDirectory, "WebUi", "index.html"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url == null || !e.Url.StartsWith("nimchat://"))
            return;

        e.Cancel = true;

        var payload = e.Url["nimchat://".Length..];
        payload = Uri.UnescapeDataString(payload);

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? "";
            var msgPayload = root.TryGetProperty("payload", out var p) ? p : default;

            await HandleWebMessageAsync(type, msgPayload);
        }
        catch (Exception ex)
        {
            AddMessage("System", $"Bridge error: {ex.Message}");
            await SendFullStateAsync("Bridge error");
        }
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
            return;

        await InjectBridgeScriptAsync();
    }

    private async Task InjectBridgeScriptAsync()
    {
        // Inject a polyfill that mirrors the WebView2 API using URL navigation
        var script = @"
(function() {
    if (window.__nimchatBridgeInjected) return;
    window.__nimchatBridgeInjected = true;

    window.chrome = window.chrome || {};
    window.chrome.webview = window.chrome.webview || {};
    window.chrome.webview.postMessage = function(msg) {
        window.location.href = 'nimchat://' + encodeURIComponent(JSON.stringify(msg));
    };

    window.dispatchHostMessage = function(msg) {
        var evt = new CustomEvent('nimchatHostMessage', { detail: msg });
        window.dispatchEvent(evt);
    };

    // If there's a pending 'appReady' call, dispatch it
    if (window.__pendingAppReady) {
        window.chrome.webview.postMessage({ type: 'appReady', payload: {} });
    }
})();
";
        await ChatWebView.EvaluateJavaScriptAsync(script);
        _isWebUiReady = true;

        if (_messages.Count == 0)
        {
            AddMessage("System", "WebView UI loaded. Type /help for command list.");
        }
        await SendFullStateAsync("Ready");
    }

    private async Task HandleWebMessageAsync(string type, JsonElement payload)
    {
        switch (type)
        {
            case "appReady":
                _isWebUiReady = true;
                if (_messages.Count == 0)
                {
                    AddMessage("System", "WebView UI loaded. Type /help for command list.");
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
            return current;

        return new UiSettings
        {
            WhisperEndpoint = GetString(payload, "whisperEndpoint"),
            ApiEndpoint = string.IsNullOrWhiteSpace(GetString(payload, "apiEndpoint"))
                ? current.ApiEndpoint
                : GetString(payload, "apiEndpoint"),
            EnableVoice = payload.TryGetProperty("enableVoice", out var ev) && ev.GetBoolean(),
            ShowTokens = !payload.TryGetProperty("showTokens", out var st) || st.GetBoolean(),
            ShowContext = !payload.TryGetProperty("showContext", out var sc) || sc.GetBoolean(),
            AutoScroll = !payload.TryGetProperty("autoScroll", out var asb) || asb.GetBoolean(),
            ShowTimeAgo = !payload.TryGetProperty("showTimeAgo", out var sta) || sta.GetBoolean()
        };
    }

    private async Task HandleSendMessageAsync(string userText, string modelId)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(userText))
            return;

        if (!string.IsNullOrWhiteSpace(modelId))
            _selectedModelId = modelId;

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
            if (!string.IsNullOrWhiteSpace(partialThinking))
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
            if (!string.IsNullOrWhiteSpace(thinking))
                assistantDraft.Thinking = thinking;
            assistantDraft.IsStreaming = false;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(assistantDraft.Content) && string.IsNullOrWhiteSpace(assistantDraft.Thinking))
                _messages.Remove(assistantDraft);
            AddMessage("System", $"Error: {ex.Message}");
        }
        finally
        {
            assistantDraft.IsStreaming = false;
            SaveConversationMemory();
            _isBusy = false;
            await SendFullStateAsync("Ready");
        }
    }

    private List<ApiChatMessage> BuildConversationContextMessages(int maxMessages = 12)
    {
        return _messages
            .Where(m => !m.IsStreaming)
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Where(m => !m.Role.Equals("System", StringComparison.OrdinalIgnoreCase))
            .Select(m => new ApiChatMessage
            {
                Role = m.Role.Equals("You", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                Content = m.Content
            })
            .TakeLast(maxMessages)
            .ToList();
    }

    private async Task<(string response, string? thinking)> SendMessageWithFilesystemToolsAsync(
        string userText,
        Action<string, string?>? onStreamUpdate = null)
    {
        if (IsImageGenerationModel(_selectedModelId))
        {
            var (ok, message, imageUrl) = await _apiClient.GenerateImageAsync(_selectedModelId, userText);
            if (!ok || string.IsNullOrWhiteSpace(imageUrl))
                return (message, null);

            var imageResponse = $"{message}\n\n![Generated image]({imageUrl})";
            onStreamUpdate?.Invoke(imageResponse, null);
            return (imageResponse, null);
        }

        var tools = BuildFilesystemTools();
        if (tools.Count == 0)
        {
            var simpleMessages = BuildConversationContextMessages();
            if (simpleMessages.Count == 0)
                simpleMessages.Add(new ApiChatMessage { Role = "user", Content = userText });

            var (directResponse, directThinking, _) = await _apiClient.SendMessageWithToolsStreamingAsync(
                _selectedModelId, simpleMessages, null, "none", onStreamUpdate);

            return (string.IsNullOrWhiteSpace(directResponse) ? "No response" : directResponse, directThinking);
        }

        var messages = new List<ApiChatMessage>
        {
            new ApiChatMessage
            {
                Role = "system",
                Content = "When tool results are available, provide a direct final answer. Avoid repeating the same tool call with identical arguments unless the user explicitly requests a rerun."
            }
        };

        var contextMessages = BuildConversationContextMessages();
        if (contextMessages.Count == 0)
            contextMessages.Add(new ApiChatMessage { Role = "user", Content = userText });
        messages.AddRange(contextMessages);

        var seenCalls = new HashSet<string>(StringComparer.Ordinal);
        var lastToolResults = new List<string>();
        var latestResponse = string.Empty;
        var latestThinkingBuilder = new StringBuilder();

        for (var i = 0; i < 8; i++)
        {
            var (response, thinking, toolCalls) = await _apiClient.SendMessageWithToolsStreamingAsync(
                _selectedModelId, messages, tools, "auto", onStreamUpdate);

            latestResponse = response ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                if (latestThinkingBuilder.Length > 0)
                {
                    latestThinkingBuilder.AppendLine();
                    latestThinkingBuilder.AppendLine();
                }
                latestThinkingBuilder.Append(thinking);
            }

            if (toolCalls == null || toolCalls.Count == 0)
            {
                var fullThinking = latestThinkingBuilder.Length > 0 ? latestThinkingBuilder.ToString() : null;
                return (string.IsNullOrWhiteSpace(latestResponse) ? "No response" : latestResponse, fullThinking);
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
                    thisRoundRepeatedOnly = false;

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
                break;
        }

        if (lastToolResults.Count > 0)
        {
            var lastResult = lastToolResults[^1];
            var prefix = string.IsNullOrWhiteSpace(latestResponse)
                ? "I executed filesystem tools, but the model kept requesting duplicate calls."
                : latestResponse;
            var fullThinking = latestThinkingBuilder.Length > 0 ? latestThinkingBuilder.ToString() : null;
            return ($"{prefix}\n\nLatest tool result:\n{lastResult}", fullThinking);
        }

        return ("I could not complete filesystem tool execution for this request. Try a more specific file path or command.", null);
    }

    private static bool IsImageGenerationModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        var id = modelId.ToLowerInvariant();
        return id.Contains("flux")
            || id.StartsWith("pollination/")
            || id.Contains("image")
            || id.Contains("vision-gen");
    }

    private List<ToolDefinition> BuildFilesystemTools()
    {
        var tools = _toolGenerator.GenerateToolsForInstalledServers();

        if (tools.Count == 0)
        {
            AddMessage("System", "No MCP servers installed. Open the MCP panel (/tools) to install servers and unlock additional tools.");
        }

        return tools;
    }

    private Task<string> ExecuteFilesystemToolAsync(ToolCall toolCall)
    {
        try
        {
            return _toolExecutor.ExecuteToolAsync(toolCall);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolError($"Tool execution failed: {ex.Message}"));
        }
    }

    private static string ToolError(string message)
    {
        return JsonSerializer.Serialize(new { ok = false, error = message });
    }

    private string GetFilesystemRootPath()
    {
        return FileSystem.AppDataDirectory;
    }

    private async Task ExecuteTerminalCommandAsync(string command)
    {
        // Android does not support arbitrary process spawning.
        // Stub implementation returning an informational message.
        await PostToWebAsync(new
        {
            type = "terminalResult",
            payload = new
            {
                ok = true,
                command,
                output = "Terminal commands are not supported on Android. Use an ADB shell or Termux for command execution.",
                exitCode = 0
            }
        });
    }

    private bool TryHandleSlashCommand(string input)
    {
        if (!input.StartsWith("/"))
            return false;

        var parts = input.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";
        var arg2 = parts.Length > 2 ? parts[2].Trim() : "";

        switch (cmd)
        {
            case "/clear":
                _messages.Clear();
                AddMessage("System", "Conversation cleared.");
                SaveConversationMemory();
                break;
            case "/new":
                _messages.Clear();
                AddMessage("System", "New conversation started.");
                SaveConversationMemory();
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
            AddMessage("System", $"\u2713 Hugging Face API key applied for this session.");
            return;
        }

        if (normalizedService is "nvidia" or "nim")
        {
            _apiClient.NvidiaApiKey = apiKey;
            SaveApiSecrets();
            AddMessage("System", "\u2713 NVIDIA API key applied for this session.");
            return;
        }

        try
        {
            var configDir = Path.Combine(FileSystem.AppDataDirectory, "mcp");
            var configPath = Path.Combine(configDir, "installed-servers.json");

            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            List<McpServerDefinition> servers = new();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    servers = JsonSerializer.Deserialize<List<McpServerDefinition>>(json) ?? new();
            }

            var server = servers.FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            if (server == null)
            {
                AddMessage("System", $"Service '{serviceName}' not found in MCP catalog. Install it from the /tools panel first.");
                return;
            }

            if (server.Env == null)
                server.Env = new Dictionary<string, string>();

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

            var jsonOut = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, jsonOut);

            AddMessage("System", $"\u2713 API key for '{serviceName}' saved successfully.\n({envKey} = {apiKey[..Math.Min(10, apiKey.Length)]}...)");
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
            return;

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
                    _selectedModelId = _models[0].Id;
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
            return;

        var catalog = await _mcpCatalog.FetchCatalogAsync();
        var server = catalog.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
        if (server == null)
            return;

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
                continue;
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

        SaveConversationMemory();
    }

    private static string GetSecretsFilePath()
    {
        var dir = FileSystem.AppDataDirectory;
        return Path.Combine(dir, SecretsFileName);
    }

    private static string GetConversationMemoryFilePath()
    {
        return Path.Combine(FileSystem.AppDataDirectory, ConversationMemoryFileName);
    }

    private void LoadConversationMemory()
    {
        try
        {
            var path = GetConversationMemoryFilePath();
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var saved = JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? new List<ChatMessage>();
            _messages.Clear();
            _messages.AddRange(saved
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .TakeLast(MaxMemoryMessages));
        }
        catch
        {
        }
    }

    private void SaveConversationMemory()
    {
        try
        {
            var path = GetConversationMemoryFilePath();
            var toSave = _messages
                .Where(m => !m.IsStreaming)
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .TakeLast(MaxMemoryMessages)
                .ToList();

            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private void LoadApiSecrets()
    {
        try
        {
            var path = GetSecretsFilePath();
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("nvidiaApiKey", out var nk) && nk.ValueKind == JsonValueKind.String)
                _apiClient.NvidiaApiKey = nk.GetString() ?? _apiClient.NvidiaApiKey;

            if (root.TryGetProperty("huggingFaceApiKey", out var hk) && hk.ValueKind == JsonValueKind.String)
                _apiClient.HuggingFaceApiKey = hk.GetString() ?? _apiClient.HuggingFaceApiKey;

            if (root.TryGetProperty("replicateApiKey", out var rk) && rk.ValueKind == JsonValueKind.String)
                _apiClient.ReplicateApiKey = rk.GetString() ?? _apiClient.ReplicateApiKey;
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
            return;

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
        var json = JsonSerializer.Serialize(message);
        var script = $"window.dispatchHostMessage({json});";
        await ChatWebView.EvaluateJavaScriptAsync(script);
    }

    private void ExportChat()
    {
        try
        {
            if (_messages.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"# Chat Export - {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            foreach (var msg in _messages)
            {
                sb.AppendLine($"**{msg.Role}** ({msg.Timestamp:yyyy-MM-dd HH:mm:ss})");
                if (!string.IsNullOrWhiteSpace(msg.Thinking))
                    sb.AppendLine($"> Thinking: {msg.Thinking}");
                sb.AppendLine(msg.Content);
                sb.AppendLine();
            }

            var fileName = $"chat_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
            File.WriteAllText(filePath, sb.ToString());

            AddMessage("System", $"Chat exported to: {filePath}");
        }
        catch (Exception ex)
        {
            AddMessage("System", $"Export failed: {ex.Message}");
            _ = SendFullStateAsync("Export failed");
        }
    }
}
