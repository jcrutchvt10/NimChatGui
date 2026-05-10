using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;

namespace NimChatGui
{
    public class ModelInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string License { get; set; } = "";
        public long? Parameters { get; set; }
        public string Architecture { get; set; } = "";
        public DateTime? Featured { get; set; }
        public DateTime? LastUpdated { get; set; }

        public bool SupportsToolCalling { get; set; }
        public bool SupportsThinking { get; set; }
        public bool IsRecommended { get; set; }
        
        public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;
        public override string ToString() => Id;
        public string SizeLabel
        {
            get
            {
                if (!Parameters.HasValue) return "";
                var p = Parameters.Value;
                if (p >= 1_000_000_000_000) return $"{p / 1_000_000_000_000}B";
                if (p >= 1_000_000_000) return $"{p / 1_000_000_000}B";
                if (p >= 1_000_000) return $"{p / 1_000_000}M";
                return $"{p / 1000}K";
            }
        }
    }

    public class ModelCatalogResponse
    {
        public List<ModelInfo> Models { get; set; } = new List<ModelInfo>();
        public int Total { get; set; }
    }

    // Tool definition classes for function calling
    public class ToolDefinition
    {
        public string Type { get; set; } = "function";
        public FunctionDefinition Function { get; set; }
    }

    public class FunctionDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public object Parameters { get; set; }
    }

    public class ToolCall
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public FunctionCall Function { get; set; }
    }

    public class FunctionCall
    {
        public string Name { get; set; }
        public string Arguments { get; set; }
    }

    public class ApiChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public List<ToolCall> ToolCalls { get; set; }
        public string ToolCallId { get; set; }
    }
    public class NimApiClient
    {
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _currentCts;

        // Use NVIDIA API endpoint for cloud inference
        public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1/";

        // NVIDIA API key for accessing cloud model catalog - should be set from environment or secure storage
        public string NvidiaApiKey { get; set; }
        public string HuggingFaceApiKey { get; set; }
        public string ReplicateApiKey { get; set; }

        public bool IsCancelled => _currentCts?.IsCancellationRequested ?? false;

        public void CancelCurrentRequest()
        {
            _currentCts?.Cancel();
        }

        public NimApiClient()
        {
            // Create HttpClient with optimized settings
            var handler = new HttpClientHandler();
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120) // 2 min timeout
            };

            // Load API key from environment variable if not explicitly set
            if (string.IsNullOrEmpty(NvidiaApiKey))
            {
                NvidiaApiKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY") ?? "";
            }

            if (string.IsNullOrEmpty(HuggingFaceApiKey))
            {
                HuggingFaceApiKey = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY")
                    ?? Environment.GetEnvironmentVariable("HF_TOKEN")
                    ?? "";
            }
        }

        public async Task<(string response, string? thinking)> SendMessageAsync(string modelName, string userMessage, CancellationToken? cancellationToken = null)
        {
            try
            {
                _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
                
                // Validate inputs
                if (string.IsNullOrWhiteSpace(modelName))
                    return ("Error: Model name cannot be empty.", null);
                
                if (string.IsNullOrWhiteSpace(userMessage))
                    return ("Error: Message cannot be empty.", null);

                if (string.IsNullOrEmpty(NvidiaApiKey))
                    return ("Error: NVIDIA API key not configured. Set NVIDIA_API_KEY environment variable.", null);

                if (_currentCts.Token.IsCancellationRequested)
                    return ("Request cancelled.", null);

                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Calling API with model={modelName}");

                // Optimized request for faster response
                var requestBody = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = 2048,         // Reduced from 4096 for faster response
                    temperature = 0.5,       // Lower for more focused/faster output
                    top_p = 0.95,            // Slightly higher for better quality-speed balance
                    presence_penalty = 0.0,
                    frequency_penalty = 0.0,
                    stream = false              // Non-streaming for simpler handling
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}chat/completions");
                requestMessage.Content = content;
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NvidiaApiKey);

                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Sending request to {BaseUrl}chat/completions");
                
                // Use longer timeout for complex requests but don't wait forever
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_currentCts.Token);
                cts.CancelAfter(TimeSpan.FromSeconds(120)); // 2 min max
                
                var response = await _httpClient.SendAsync(requestMessage, cts.Token);
                
                if (_currentCts.Token.IsCancellationRequested)
                    return ("Request cancelled.", null);
                
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Response status = {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Error response = {errorContent}");
                    return ($"API Error ({response.StatusCode}): {errorContent}", null);
                }

                var responseString = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Response length = {responseString.Length}");

                using var jsonDoc = JsonDocument.Parse(responseString);
                var message = jsonDoc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message");

                // Extract main reply content
                var reply = message.TryGetProperty("content", out var contentProp)
                    ? contentProp.GetString() ?? string.Empty
                    : string.Empty;

                // Extract reasoning/thinking content
                string? thinking = null;
                if (message.TryGetProperty("reasoning_content", out var reasoningProp))
                    thinking = reasoningProp.GetString();

                // Some models embed <think>...</think> tags inside the content itself
                if (string.IsNullOrEmpty(thinking) && reply.Contains("<think>"))
                {
                    var thinkStart = reply.IndexOf("<think>") + "<think>".Length;
                    var thinkEnd = reply.IndexOf("</think>");
                    if (thinkEnd > thinkStart)
                    {
                        thinking = reply.Substring(thinkStart, thinkEnd - thinkStart).Trim();
                        reply = reply.Substring(thinkEnd + "</think>".Length).Trim();
                    }
                }

                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Successfully parsed response");
                return (string.IsNullOrEmpty(reply) ? "Error: Empty response received." : reply, thinking);
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: HttpRequestException - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Inner exception - {ex.InnerException?.Message}");
                return ($"Network Error: {ex.Message}", null);
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: TaskCanceledException - Request timeout");
                return ($"Request Timeout: The API took longer than 60 seconds to respond.", null);
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: JsonException - {ex.Message}");
                return ($"Response Parse Error: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SendMessageAsync: Stack trace - {ex.StackTrace}");
                return ($"Error: {ex.Message}", null);
            }
        }

        public async Task<(bool ok, string message, string? imageUrl)> GenerateImageAsync(
            string modelName,
            string prompt,
            CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return (false, "Error: Model name cannot be empty.", null);

            if (string.IsNullOrWhiteSpace(prompt))
                return (false, "Error: Prompt cannot be empty.", null);

            // Route to Pollination AI for models prefixed with "pollination/"
            if (modelName.StartsWith("pollination/", StringComparison.OrdinalIgnoreCase))
            {
                var result = await GenerateImageWithPollinationAsync(prompt, cancellationToken);
                // If Pollination fails, fall back to FLUX.1-schnell from NVIDIA
                if (!result.ok)
                {
                    return await GenerateImageWithNvidiaAsync("black-forest-labs/flux.1-schnell", prompt, cancellationToken);
                }
                return result;
            }

            // Route to Replicate for models prefixed with "replicate/"
            // (Currently disabled due to version ID permission issues)
            // if (modelName.StartsWith("replicate/", StringComparison.OrdinalIgnoreCase))
            // {
            //     var replicateModelId = modelName.Substring(10);
            //     return await GenerateImageWithReplicateAsync(replicateModelId, prompt, cancellationToken);
            // }

            // Use NVIDIA API for FLUX and other models
            return await GenerateImageWithNvidiaAsync(modelName, prompt, cancellationToken);
        }

        private async Task<(bool ok, string message, string? imageUrl)> GenerateImageWithNvidiaAsync(
            string modelName,
            string prompt,
            CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrEmpty(NvidiaApiKey))
                return (false, "Error: NVIDIA API key not configured. Set NVIDIA_API_KEY environment variable.", null);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            var endpoints = new[]
            {
                new { Url = "https://ai.api.nvidia.com/v1/genai/images/generations", Mode = "openai" },
                new { Url = $"https://ai.api.nvidia.com/v1/genai/{modelName}", Mode = "nim" }
            };

            string? lastError = null;

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var requestBody = endpoint.Mode == "openai"
                        ? JsonSerializer.Serialize(new
                        {
                            model = modelName,
                            prompt,
                            size = "1024x1024",
                            response_format = "url"
                        })
                        : JsonSerializer.Serialize(new
                        {
                            prompt
                        });

                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
                    {
                        Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NvidiaApiKey);

                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    var responseText = await response.Content.ReadAsStringAsync(cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"API Error ({response.StatusCode}): {responseText}";
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            continue;
                        }

                        return (false, lastError, null);
                    }

                    var imageUrl = TryExtractImageUrl(responseText);
                    if (string.IsNullOrWhiteSpace(imageUrl))
                    {
                        lastError = "Image generation succeeded, but no image URL or base64 payload was returned.";
                        continue;
                    }

                    return (true, $"Generated image using {modelName}.", imageUrl);
                }
                catch (TaskCanceledException)
                {
                    return (false, "Request Timeout: The image API took longer than 120 seconds to respond.", null);
                }
                catch (Exception ex)
                {
                    lastError = $"Error: {ex.Message}";
                }
            }

            var errorMessage = string.IsNullOrWhiteSpace(lastError)
                ? "API Error (NotFound): 404 page not found. The selected image model may not be available for this key/endpoint."
                : lastError;

            return (false, errorMessage, null);
        }

        private async Task<(bool ok, string message, string? imageUrl)> GenerateImageWithHuggingFaceAsync(
            string modelId,
            string prompt,
            CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return (false, "Error: Hugging Face model id is missing.", null);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
            cts.CancelAfter(TimeSpan.FromSeconds(180));

            var candidates = GetHuggingFaceImageCandidates(modelId);
            var endpoints = new[]
            {
                new { Url = $"https://router.huggingface.co/hf-inference/models/{modelId}", Mode = "hf-inference" },
                new { Url = $"https://api-inference.huggingface.co/models/{modelId}", Mode = "hf-inference" }
            };

            string? lastError = null;

            foreach (var candidateModel in candidates)
            {
                foreach (var endpoint in endpoints)
                {
                    var requestBody = endpoint.Mode == "openai"
                        ? JsonSerializer.Serialize(new
                        {
                            model = candidateModel,
                            prompt,
                            size = "1024x1024",
                            response_format = "b64_json"
                        })
                        : JsonSerializer.Serialize(new
                        {
                            inputs = prompt,
                            options = new
                            {
                                wait_for_model = true,
                                use_cache = false
                            }
                        });

                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
                    {
                        Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                    };

                    if (!string.IsNullOrWhiteSpace(HuggingFaceApiKey))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", HuggingFaceApiKey);
                    }

                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    var body = Encoding.UTF8.GetString(bytes);

                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = $"HF API Error ({response.StatusCode}): {body}";

                        var isLegacyPathFailure = body.Contains("Cannot POST /models/", StringComparison.OrdinalIgnoreCase);
                        var unsupportedProvider = body.Contains("Model not supported by provider hf-inference", StringComparison.OrdinalIgnoreCase)
                            || body.Contains("provider hf-inference", StringComparison.OrdinalIgnoreCase);

                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound || isLegacyPathFailure || unsupportedProvider)
                        {
                            continue;
                        }

                        return (false, lastError, null);
                    }

                    if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var base64 = Convert.ToBase64String(bytes);
                        var dataUrl = $"data:{contentType};base64,{base64}";
                        return (true, $"Generated image using Hugging Face model {candidateModel}.", dataUrl);
                    }

                    var maybeImage = TryExtractImageUrl(body);
                    if (!string.IsNullOrWhiteSpace(maybeImage))
                    {
                        return (true, $"Generated image using Hugging Face model {candidateModel}.", maybeImage);
                    }

                    lastError = $"HF API returned unexpected response: {body}";
                }
            }

            return (false, lastError ?? "HF API Error (NotFound): model endpoint was not available for image generation.", null);
        }

        private async Task<(bool ok, string message, string? imageUrl)> GenerateImageWithPollinationAsync(
            string prompt,
            CancellationToken? cancellationToken = null)
        {
            // Retry logic for transient failures (522, 429, etc.)
            const int maxRetries = 2;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
                    cts.CancelAfter(TimeSpan.FromSeconds(120));

                    // Pollination AI free API - no authentication required
                    var requestBody = JsonSerializer.Serialize(new
                    {
                        prompt,
                        model = "turbo",  // Fast free model
                        seed = new Random().Next()
                    });

                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.pollinations.ai/v1/images/generations")
                    {
                        Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                    };

                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    var responseText = await response.Content.ReadAsStringAsync(cts.Token);

                    // Retry on transient errors (522, 429, 503, 504)
                    if ((int)response.StatusCode is 429 or 522 or 503 or 504)
                    {
                        if (attempt < maxRetries - 1)
                        {
                            // Wait before retry with exponential backoff
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken ?? CancellationToken.None);
                            continue;
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMsg = $"API Error ({response.StatusCode}): {responseText}";
                        return (false, errorMsg, null);
                    }

                    // Pollination returns the image URL directly in data.url
                    var imageUrl = TryExtractPollinationImageUrl(responseText);
                    if (string.IsNullOrWhiteSpace(imageUrl))
                    {
                        return (false, "Image generation succeeded, but no image URL was returned.", null);
                    }

                    return (true, "Generated image using Pollination AI (free).", imageUrl);
                }
                catch (TaskCanceledException)
                {
                    if (attempt < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken ?? CancellationToken.None);
                        continue;
                    }
                    return (false, "Request Timeout: The image API took longer than 120 seconds to respond.", null);
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken ?? CancellationToken.None);
                        continue;
                    }
                    return (false, $"Pollination AI Error: {ex.Message}", null);
                }
            }

            return (false, "Pollination AI service unavailable after retries. Falling back to FLUX.", null);
        }

        private static string? TryExtractPollinationImageUrl(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Pollination API returns { "data": { "url": "..." } } or direct URL
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    if (data.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        return urlProp.GetString();
                    }
                }

                // Also try direct url property
                if (root.TryGetProperty("url", out var directUrl) && directUrl.ValueKind == JsonValueKind.String)
                {
                    return directUrl.GetString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<(bool ok, string message, string? imageUrl)> GenerateImageWithReplicateAsync(
            string modelId,
            string prompt,
            CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return (false, "Error: Replicate model id is missing.", null);
            }

            if (string.IsNullOrEmpty(ReplicateApiKey))
            {
                return (false, "Error: Replicate API key not configured. Set REPLICATE_API_KEY environment variable or use /setapikey replicate <key>.", null);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
            cts.CancelAfter(TimeSpan.FromSeconds(300)); // Replicate can be slower

            try
            {
                // For Stable Diffusion 3, use the public model: stability-ai/stable-diffusion-3
                // Use a known public version ID for SD3 medium
                string versionId = "f521b57b5c883487434f31d7a64e43d5329409df40b08d292f64266e56ea0201"; // SD3 medium (known public version)

                var requestBody = JsonSerializer.Serialize(new
                {
                    version = versionId,
                    input = new
                    {
                        prompt,
                        num_inference_steps = 28,
                        guidance_scale = 3.5,
                        aspect_ratio = "1:1"
                    }
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.replicate.com/v1/predictions")
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", ReplicateApiKey);

                using var response = await _httpClient.SendAsync(request, cts.Token);
                var responseText = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Replicate API Error ({response.StatusCode}): {responseText}", null);
                }

                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                var predictionUrl = root.GetProperty("urls").GetProperty("get").GetString();

                // Poll for completion
                int maxAttempts = 120; // 2 minutes max with 1-second checks
                int attempt = 0;
                while (attempt < maxAttempts)
                {
                    await Task.Delay(1000, cts.Token);
                    
                    using var pollRequest = new HttpRequestMessage(HttpMethod.Get, predictionUrl);
                    pollRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", ReplicateApiKey);

                    using var pollResponse = await _httpClient.SendAsync(pollRequest, cts.Token);
                    var pollText = await pollResponse.Content.ReadAsStringAsync(cts.Token);

                    using var pollDoc = JsonDocument.Parse(pollText);
                    var pollRoot = pollDoc.RootElement;
                    var status = pollRoot.GetProperty("status").GetString();

                    if (status == "succeeded")
                    {
                        var output = pollRoot.GetProperty("output");
                        if (output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                        {
                            var imageUrl = output[0].GetString();
                            return (true, $"Generated image using Replicate Stable Diffusion 3", imageUrl);
                        }
                    }
                    else if (status == "failed")
                    {
                        var error = pollRoot.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "Unknown error";
                        return (false, $"Replicate generation failed: {error}", null);
                    }

                    attempt++;
                }

                return (false, "Replicate API timeout: prediction took too long to complete.", null);
            }
            catch (Exception ex)
            {
                return (false, $"Replicate API Error: {ex.Message}", null);
            }
        }

        private static List<string> GetHuggingFaceImageCandidates(string requestedModelId)
        {
            var candidates = new List<string>();

            void Add(string value)
            {
                if (!candidates.Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(value);
                }
            }

            if (!string.IsNullOrWhiteSpace(requestedModelId))
            {
                Add(requestedModelId);
            }

            // Known-supported community image models that work better with the shared hf-inference provider.
            Add("stabilityai/stable-diffusion-xl-base-1.0");
            Add("Lykon/dreamshaper-8");
            Add("stabilityai/stable-diffusion-2-1");

            return candidates;
        }

        private static string? TryExtractImageUrl(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var first = data[0];
                if (first.ValueKind == JsonValueKind.Object)
                {
                    if (first.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        return urlProp.GetString();
                    }

                    if (first.TryGetProperty("b64_json", out var b64Prop) && b64Prop.ValueKind == JsonValueKind.String)
                    {
                        var b64 = b64Prop.GetString();
                        return string.IsNullOrWhiteSpace(b64) ? null : $"data:image/png;base64,{b64}";
                    }
                }
            }

            if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array && artifacts.GetArrayLength() > 0)
            {
                var first = artifacts[0];
                if (first.TryGetProperty("base64", out var base64Prop) && base64Prop.ValueKind == JsonValueKind.String)
                {
                    var b64 = base64Prop.GetString();
                    return string.IsNullOrWhiteSpace(b64) ? null : $"data:image/png;base64,{b64}";
                }
            }

            if (root.TryGetProperty("image", out var imageProp) && imageProp.ValueKind == JsonValueKind.String)
            {
                var value = imageProp.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? value
                        : $"data:image/png;base64,{value}";
                }
            }

            return null;
        }

        public async Task<(string response, List<ToolCall>? toolCalls)> SendMessageWithToolsAsync(
            string modelName, 
            List<ApiChatMessage> messages, 
            List<ToolDefinition> tools = null,
            string toolChoice = "auto")
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(modelName))
                    return ("Error: Model name cannot be empty.", null);

                if (messages == null || messages.Count == 0)
                    return ("Error: Messages cannot be empty.", null);

                if (string.IsNullOrEmpty(NvidiaApiKey))
                    return ("Error: NVIDIA API key not configured. Set NVIDIA_API_KEY environment variable.", null);

                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = modelName,
                    ["messages"] = messages.Select(m => 
                    {
                        var msg = new Dictionary<string, object>
                        {
                            ["role"] = m.Role,
                            ["content"] = m.Content ?? string.Empty
                        };

                        if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                        {
                            msg["tool_calls"] = m.ToolCalls;
                        }

                        if (!string.IsNullOrEmpty(m.ToolCallId))
                        {
                            msg["tool_call_id"] = m.ToolCallId;
                        }

                        return msg;

                    }).ToList(),
                    ["max_tokens"] = 1024,
                    ["temperature"] = 0.7,
                    ["top_p"] = 0.9
                };

                if (tools != null && tools.Count > 0)
                {
                    requestBody["tools"] = tools;
                    requestBody["tool_choice"] = toolChoice;
                }

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}chat/completions");
                requestMessage.Content = content;
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NvidiaApiKey);

                var response = await _httpClient.SendAsync(requestMessage);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return ($"API Error ({response.StatusCode}): {errorContent}", null);
                }

                var responseString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseString);

                var messageElement = jsonDoc.RootElement
                    .GetProperty("choices")
                    .EnumerateArray()
                    .First()
                    .GetProperty("message");

                var responseContent = messageElement.TryGetProperty("content", out var contentProp) 
                    ? contentProp.GetString() ?? string.Empty
                    : string.Empty;

                List<ToolCall>? toolCalls = null;
                if (messageElement.TryGetProperty("tool_calls", out var toolCallsProp))
                {
                    toolCalls = new List<ToolCall>();
                    foreach (var toolCallElement in toolCallsProp.EnumerateArray())
                    {
                        var toolCall = new ToolCall
                        {
                            Id = toolCallElement.GetProperty("id").GetString() ?? string.Empty,
                            Type = toolCallElement.GetProperty("type").GetString() ?? string.Empty,
                            Function = new FunctionCall
                            {
                                Name = toolCallElement.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                                Arguments = toolCallElement.GetProperty("function").GetProperty("arguments").GetString() ?? string.Empty
                            }
                        };
                        toolCalls.Add(toolCall);
                    }
                }

                return (responseContent, toolCalls);
            }
            catch (HttpRequestException ex)
            {
                return ($"Network Error: {ex.Message}", null);
            }
            catch (TaskCanceledException)
            {
                return ($"Request Timeout: The API took longer than 60 seconds to respond.", null);
            }
            catch (JsonException ex)
            {
                return ($"Response Parse Error: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                return ($"Error: {ex.Message}", null);
            }
        }

        public async Task<(string response, string? thinking, List<ToolCall>? toolCalls)> SendMessageWithToolsStreamingAsync(
            string modelName,
            List<ApiChatMessage> messages,
            List<ToolDefinition> tools = null,
            string toolChoice = "auto",
            Action<string, string?>? onContentUpdate = null,
            CancellationToken? cancellationToken = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelName))
                    return ("Error: Model name cannot be empty.", null, null);

                if (messages == null || messages.Count == 0)
                    return ("Error: Messages cannot be empty.", null, null);

                if (string.IsNullOrEmpty(NvidiaApiKey))
                    return ("Error: NVIDIA API key not configured. Set NVIDIA_API_KEY environment variable.", null, null);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? CancellationToken.None);
                cts.CancelAfter(TimeSpan.FromSeconds(120));

                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = modelName,
                    ["messages"] = messages.Select(m =>
                    {
                        var msg = new Dictionary<string, object>
                        {
                            ["role"] = m.Role,
                            ["content"] = m.Content ?? string.Empty
                        };

                        if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                        {
                            msg["tool_calls"] = m.ToolCalls;
                        }

                        if (!string.IsNullOrEmpty(m.ToolCallId))
                        {
                            msg["tool_call_id"] = m.ToolCallId;
                        }

                        return msg;
                    }).ToList(),
                    ["max_tokens"] = 1024,
                    ["temperature"] = 0.7,
                    ["top_p"] = 0.9,
                    ["stream"] = true
                };

                if (tools != null && tools.Count > 0)
                {
                    requestBody["tools"] = tools;
                    requestBody["tool_choice"] = toolChoice;
                }

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}chat/completions")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NvidiaApiKey);

                using var response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return ($"API Error ({response.StatusCode}): {errorContent}", null, null);
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    return ParseNonStreamingChatResponse(responseString, onContentUpdate);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var reader = new StreamReader(stream);

                var contentBuilder = new StringBuilder();
                var thinkingBuilder = new StringBuilder();
                var toolCallParts = new Dictionary<int, ToolCallStreamPart>();

                while (!reader.EndOfStream)
                {
                    if (cts.Token.IsCancellationRequested)
                    {
                        return ("Request cancelled.", null, null);
                    }

                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var payload = line[5..].Trim();
                    if (payload.Length == 0)
                    {
                        continue;
                    }

                    if (payload.Equals("[DONE]", StringComparison.Ordinal))
                    {
                        break;
                    }

                    try
                    {
                        using var chunkDoc = JsonDocument.Parse(payload);
                        if (!chunkDoc.RootElement.TryGetProperty("choices", out var choices)
                            || choices.ValueKind != JsonValueKind.Array
                            || choices.GetArrayLength() == 0)
                        {
                            continue;
                        }

                        var choice = choices[0];
                        if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var updated = false;
                        if (delta.TryGetProperty("content", out var contentDelta) && contentDelta.ValueKind == JsonValueKind.String)
                        {
                            contentBuilder.Append(contentDelta.GetString());
                            updated = true;
                        }

                        if (delta.TryGetProperty("reasoning_content", out var thinkingDelta) && thinkingDelta.ValueKind == JsonValueKind.String)
                        {
                            thinkingBuilder.Append(thinkingDelta.GetString());
                            updated = true;
                        }

                        if (delta.TryGetProperty("tool_calls", out var toolCallsDelta) && toolCallsDelta.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var callDelta in toolCallsDelta.EnumerateArray())
                            {
                                var index = 0;
                                if (callDelta.TryGetProperty("index", out var indexProp) && indexProp.TryGetInt32(out var parsedIndex))
                                {
                                    index = parsedIndex;
                                }

                                if (!toolCallParts.TryGetValue(index, out var part))
                                {
                                    part = new ToolCallStreamPart();
                                    toolCallParts[index] = part;
                                }

                                if (callDelta.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                                {
                                    part.Id = idProp.GetString() ?? part.Id;
                                }

                                if (callDelta.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                                {
                                    part.Type = typeProp.GetString() ?? part.Type;
                                }

                                if (callDelta.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                                {
                                    if (fn.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                                    {
                                        part.FunctionName = nameProp.GetString() ?? part.FunctionName;
                                    }

                                    if (fn.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                                    {
                                        part.FunctionArguments.Append(argsProp.GetString());
                                    }
                                }
                            }
                        }

                        if (updated && onContentUpdate != null)
                        {
                            var currentThinking = thinkingBuilder.Length > 0 ? thinkingBuilder.ToString() : null;
                            onContentUpdate(contentBuilder.ToString(), currentThinking);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Debug.WriteLine($"Streaming chunk parse skipped: {ex.Message}");
                    }
                }

                var finalContent = contentBuilder.ToString();
                var finalThinking = thinkingBuilder.Length > 0 ? thinkingBuilder.ToString() : null;
                var toolCalls = BuildToolCallsFromStreamParts(toolCallParts);

                if (onContentUpdate != null && (!string.IsNullOrEmpty(finalContent) || !string.IsNullOrEmpty(finalThinking)))
                {
                    onContentUpdate(finalContent, finalThinking);
                }

                return (finalContent, finalThinking, toolCalls);
            }
            catch (HttpRequestException ex)
            {
                return ($"Network Error: {ex.Message}", null, null);
            }
            catch (TaskCanceledException)
            {
                return ("Request Timeout: The API took longer than 120 seconds to respond.", null, null);
            }
            catch (JsonException ex)
            {
                return ($"Response Parse Error: {ex.Message}", null, null);
            }
            catch (Exception ex)
            {
                return ($"Error: {ex.Message}", null, null);
            }
        }

        private static (string response, string? thinking, List<ToolCall>? toolCalls) ParseNonStreamingChatResponse(
            string responseString,
            Action<string, string?>? onContentUpdate)
        {
            using var jsonDoc = JsonDocument.Parse(responseString);

            var messageElement = jsonDoc.RootElement
                .GetProperty("choices")
                .EnumerateArray()
                .First()
                .GetProperty("message");

            var responseContent = messageElement.TryGetProperty("content", out var contentProp)
                ? contentProp.GetString() ?? string.Empty
                : string.Empty;

            var thinking = messageElement.TryGetProperty("reasoning_content", out var thinkingProp)
                && thinkingProp.ValueKind == JsonValueKind.String
                ? thinkingProp.GetString()
                : null;

            List<ToolCall>? toolCalls = null;
            if (messageElement.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
            {
                toolCalls = new List<ToolCall>();
                foreach (var toolCallElement in toolCallsProp.EnumerateArray())
                {
                    toolCalls.Add(new ToolCall
                    {
                        Id = toolCallElement.GetProperty("id").GetString() ?? string.Empty,
                        Type = toolCallElement.GetProperty("type").GetString() ?? string.Empty,
                        Function = new FunctionCall
                        {
                            Name = toolCallElement.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                            Arguments = toolCallElement.GetProperty("function").GetProperty("arguments").GetString() ?? string.Empty
                        }
                    });
                }
            }

            onContentUpdate?.Invoke(responseContent, thinking);
            return (responseContent, thinking, toolCalls);
        }

        private static List<ToolCall>? BuildToolCallsFromStreamParts(Dictionary<int, ToolCallStreamPart> toolCallParts)
        {
            if (toolCallParts.Count == 0)
            {
                return null;
            }

            return toolCallParts
                .OrderBy(kv => kv.Key)
                .Select(kv => new ToolCall
                {
                    Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? $"tool_call_{kv.Key}" : kv.Value.Id,
                    Type = string.IsNullOrWhiteSpace(kv.Value.Type) ? "function" : kv.Value.Type,
                    Function = new FunctionCall
                    {
                        Name = kv.Value.FunctionName,
                        Arguments = kv.Value.FunctionArguments.ToString()
                    }
                })
                .ToList();
        }

        private sealed class ToolCallStreamPart
        {
            public string Id { get; set; } = string.Empty;
            public string Type { get; set; } = "function";
            public string FunctionName { get; set; } = string.Empty;
            public StringBuilder FunctionArguments { get; } = new StringBuilder();
        }

        public async Task<System.Collections.Generic.List<string>> GetAvailableModelsAsync()
        {
            var models = new System.Collections.Generic.List<string>();
            try
            {
                // NIMs expose their active models here
                var response = await _httpClient.GetAsync($"{BaseUrl}models");
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseString);

                // Parse the standard OpenAI /v1/models JSON array
                var dataArray = jsonDoc.RootElement.GetProperty("data");
                foreach (var element in dataArray.EnumerateArray())
                {
                    var modelId = element.GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(modelId))
                    {
                        models.Add(modelId);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch models: {ex.Message}");
            }
            return models;
        }

        public async Task<List<ModelInfo>> GetNvidiaRemoteCatalogAsync()
        {
            var models = new List<ModelInfo>();
            try
            {
                if (string.IsNullOrEmpty(NvidiaApiKey))
                {
                    System.Diagnostics.Debug.WriteLine("No API key configured");
                    return models;
                }

                System.Diagnostics.Debug.WriteLine("Fetching model catalog from NVIDIA API...");

                var request = new HttpRequestMessage(HttpMethod.Get, "https://integrate.api.nvidia.com/v1/models");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NvidiaApiKey);

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"API returned {response.StatusCode}: {errBody}");
                    return models;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("data", out var modelsArray))
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        var info = new ModelInfo();
                        
                        if (model.TryGetProperty("id", out var idProp))
                            info.Id = idProp.GetString() ?? "";
                        
                        if (model.TryGetProperty("name", out var nameProp))
                            info.Name = nameProp.GetString() ?? "";
                        
                        if (model.TryGetProperty("owner", out var ownerProp))
                            info.Owner = ownerProp.GetString() ?? "";
                        
                        if (model.TryGetProperty("version", out var verProp))
                            info.Version = verProp.GetString() ?? "";
                        
                        if (model.TryGetProperty("description", out var descProp))
                            info.Description = descProp.GetString() ?? "";
                        
                        if (model.TryGetProperty("license", out var licProp))
                            info.License = licProp.GetString() ?? "";

                        if (model.TryGetProperty("parameters", out var paramsProp))
                            info.Parameters = paramsProp.GetInt64();
                        
                        if (model.TryGetProperty("architecture", out var archProp))
                            info.Architecture = archProp.GetString() ?? "";
                        
                        if (model.TryGetProperty("featured", out var featProp) && featProp.TryGetProperty("created_at", out var createdProp))
                        {
                            if (DateTime.TryParse(createdProp.GetString(), out var dt))
                                info.Featured = dt;
                        }
                        
                        if (model.TryGetProperty("last_updated", out var updatedProp))
                        {
                            if (DateTime.TryParse(updatedProp.GetString(), out var dt))
                                info.LastUpdated = dt;
                        }

                        if (model.TryGetProperty("model_type", out var typeProp))
                        {
                            var typeStr = typeProp.GetString() ?? "";
                            info.SupportsThinking = typeStr.Contains("reasoning", StringComparison.OrdinalIgnoreCase) 
                                || typeStr.Contains("thinking", StringComparison.OrdinalIgnoreCase);
                        }

                        if (model.TryGetProperty("tool_models", out var toolsProp) && toolsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            info.SupportsToolCalling = toolsProp.GetArrayLength() > 0;
                        }
                        
                        var idLower = info.Id.ToLower();
                        
                        // Inference for tool calling support
                        if (info.Id.Contains("nemotron") || info.Id.Contains("llama-3") || 
                            info.Id.Contains("mistral") || info.Id.Contains("mixtral") ||
                            info.Id.Contains("tool") || info.Id.Contains("function") ||
                            info.Id.Contains("agent") || info.Id.Contains("qwen") ||
                            info.Id.Contains("command") || info.Id.Contains("instruct"))
                        {
                            info.SupportsToolCalling = true;
                        }
                        
                        // Inference for thinking/reasoning support
                        if (info.Id.Contains("r1") || info.Id.Contains("reasoning") || 
                            info.Id.Contains("thought") || info.Id.Contains("reasoning") ||
                            info.Id.Contains("o1") || info.Id.Contains("o3") ||
                            info.Id.Contains("deepseek") || info.Id.Contains("cognitive"))
                        {
                            info.SupportsThinking = true;
                        }
                        
                        // Inference for recommended models
                        if (info.Id.Contains("nemotron") || info.Id.Contains("chat") || 
                            info.Owner == "NVIDIA" || info.Owner.Contains("Meta") ||
                            info.Owner.Contains("Mistral"))
                        {
                            info.IsRecommended = true;
                        }
                        
                        // Also check description for capability hints
                        if (!string.IsNullOrEmpty(info.Description))
                        {
                            var descLower = info.Description.ToLower();
                            if (descLower.Contains("tool") || descLower.Contains("function") ||
                                descLower.Contains("agent") || descLower.Contains("api"))
                            {
                                info.SupportsToolCalling = true;
                            }
                            if (descLower.Contains("reasoning") || descLower.Contains("thinking") ||
                                descLower.Contains("chain") || descLower.Contains("reason"))
                            {
                                info.SupportsThinking = true;
                            }
                        }

                        if (model.TryGetProperty("recommended", out var recProp))
                            info.IsRecommended = recProp.GetBoolean();

                        if (!string.IsNullOrEmpty(info.Id))
                            models.Add(info);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Found {models.Count} models");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetNvidiaRemoteCatalogAsync: {ex.Message}");
                return models;
            }
            return models;
        }
    }
}