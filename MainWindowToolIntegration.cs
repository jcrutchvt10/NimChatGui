using System.Windows.Controls;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace NimChatGui
{
    /// <summary>
    /// Example of how to integrate tool calling into MainWindow.xaml.cs
    /// This shows how to extend the existing ProcessWorkflowAsync method to support tools
    /// </summary>
    public class MainWindowToolIntegration
    {
        // Example: Modified ProcessWorkflowAsync that supports tool calling
        public async Task ProcessWorkflowWithToolsAsync(
            NimApiClient apiClient,
            string userText,
            string modelId,
            Action<ChatMessage> addMessage,
            Action scrollToEnd)
        {
            // Define available tools
            var tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "search_web",
                        Description = "Search the web for current information",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                query = new
                                {
                                    type = "string",
                                    description = "The search query"
                                }
                            },
                            required = new[] { "query" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "calculate",
                        Description = "Perform a mathematical calculation",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                expression = new
                                {
                                    type = "string",
                                    description = "Mathematical expression to evaluate"
                                }
                            },
                            required = new[] { "expression" }
                        }
                    }
                }
            };

            // Build conversation history
            var messages = new List<ApiChatMessage>
            {
                new ApiChatMessage { Role = "user", Content = userText }
            };

            // First API call
            var (response, toolCalls) = await apiClient.SendMessageWithToolsAsync(
                modelId,
                messages,
                tools,
                "auto"
            );

            // If model wants to use tools
            if (toolCalls != null && toolCalls.Count > 0)
            {
                // Show that the assistant is using tools
                addMessage(new ChatMessage
                {
                    Role = "Assistant",
                    Content = "Using tools to help answer your question..."
                });
                scrollToEnd();

                // Add assistant's message with tool calls to conversation
                messages.Add(new ApiChatMessage
                {
                    Role = "assistant",
                    Content = response,
                    ToolCalls = toolCalls
                });

                // Execute each tool
                foreach (var toolCall in toolCalls)
                {
                    var toolName = toolCall.Function.Name;
                    var toolArgs = toolCall.Function.Arguments;

                    // Show tool execution in UI
                    addMessage(new ChatMessage
                    {
                        Role = "System",
                        Content = $"Executing tool: {toolName}"
                    });
                    scrollToEnd();

                    // Execute the tool and get result
                    var toolResult = await ExecuteToolAsync(toolCall);

                    // Add tool result to conversation
                    messages.Add(new ApiChatMessage
                    {
                        Role = "tool",
                        Content = toolResult,
                        ToolCallId = toolCall.Id
                    });

                    // Show tool result in UI
                    addMessage(new ChatMessage
                    {
                        Role = "Tool Result",
                        Content = toolResult
                    });
                    scrollToEnd();
                }

                // Get final response with tool results
                var (finalResponse, _) = await apiClient.SendMessageWithToolsAsync(
                    modelId,
                    messages,
                    tools,
                    "auto"
                );

                // Show final response
                addMessage(new ChatMessage
                {
                    Role = "NIM Assistant",
                    Content = finalResponse
                });
            }
            else
            {
                // No tools needed, show direct response
                addMessage(new ChatMessage
                {
                    Role = "NIM Assistant",
                    Content = response
                });
            }

            scrollToEnd();
        }

        /// <summary>
        /// Execute a tool call and return the result as JSON string
        /// </summary>
        private async Task<string> ExecuteToolAsync(ToolCall toolCall)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    toolCall.Function.Arguments
                );

                switch (toolCall.Function.Name)
                {
                    case "search_web":
                        return await SearchWebAsync(args["query"]);

                    case "calculate":
                        return CalculateExpression(args["expression"]);

                    default:
                        return JsonSerializer.Serialize(new
                        {
                            error = $"Tool '{toolCall.Function.Name}' is not wired in this example integration. Use the main MCP executor path instead."
                        });
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    error = ex.Message
                });
            }
        }

        private async Task<string> SearchWebAsync(string query)
        {
            // Simulate web search - replace with actual search API
            await Task.Delay(500); // Simulate API delay
            return JsonSerializer.Serialize(new
            {
                query = query,
                results = new[]
                {
                    new { title = "Result 1", snippet = "Information about " + query },
                    new { title = "Result 2", snippet = "More details on " + query }
                }
            });
        }

        private string CalculateExpression(string expression)
        {
            try
            {
                // Simple calculator - replace with proper expression evaluator
                var result = new System.Data.DataTable().Compute(expression, null);
                return JsonSerializer.Serialize(new
                {
                    expression = expression,
                    result = result.ToString()
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Invalid expression: " + ex.Message
                });
            }
        }

        /// <summary>
        /// Alternative: Simpler integration that enables/disables tools via checkbox
        /// </summary>
        public async Task<string> ProcessWithOptionalToolsAsync(
            NimApiClient apiClient,
            string userText,
            string modelId,
            bool enableTools)
        {
            if (!enableTools)
            {
                // Use existing simple API call
                var (simpleResponse, _) = await apiClient.SendMessageAsync(modelId, userText);
                return simpleResponse;
            }

            // Use tool-enabled API call
            var tools = GetDefaultTools();
            var messages = new List<ApiChatMessage>
            {
                new ApiChatMessage { Role = "user", Content = userText }
            };

            var (response, toolCalls) = await apiClient.SendMessageWithToolsAsync(
                modelId,
                messages,
                tools
            );

            if (toolCalls != null && toolCalls.Count > 0)
            {
                messages.Add(new ApiChatMessage
                {
                    Role = "assistant",
                    Content = response,
                    ToolCalls = toolCalls
                });

                foreach (var toolCall in toolCalls)
                {
                    var result = await ExecuteToolAsync(toolCall);
                    messages.Add(new ApiChatMessage
                    {
                        Role = "tool",
                        Content = result,
                        ToolCallId = toolCall.Id
                    });
                }

                var (finalResponse, _) = await apiClient.SendMessageWithToolsAsync(
                    modelId,
                    messages,
                    tools
                );

                return finalResponse;
            }

            return response;
        }

        private List<ToolDefinition> GetDefaultTools()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "get_time",
                        Description = "Get the current time",
                        Parameters = new
                        {
                            type = "object",
                            properties = new { }
                        }
                    }
                }
            };
        }
    }
}
