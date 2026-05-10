using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace NimChatGui
{
    /// <summary>
    /// Example demonstrating how to use tool calling with NimApiClient
    /// </summary>
    public class ToolCallingExample
    {
        private readonly NimApiClient _apiClient;

        public ToolCallingExample()
        {
            _apiClient = new NimApiClient();
        }

        /// <summary>
        /// Example: Using tool calling to get weather information
        /// </summary>
        public async Task<string> GetWeatherWithToolCallingAsync(string userMessage)
        {
            // Define available tools
            var tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Type = "function",
                    Function = new FunctionDefinition
                    {
                        Name = "get_current_weather",
                        Description = "Get the current weather in a given location",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                location = new
                                {
                                    type = "string",
                                    description = "The city and state, e.g. San Francisco, CA"
                                },
                                unit = new
                                {
                                    type = "string",
                                    enumValues = new[] { "celsius", "fahrenheit" },
                                    description = "The unit of temperature"
                                }
                            },
                            required = new[] { "location" }
                        }
                    }
                }
            };

            // Initial conversation with user message
            var messages = new List<ApiChatMessage>
            {
                new ApiChatMessage 
                { 
                    Role = "user", 
                    Content = userMessage 
                }
            };

            // First API call - model may request tool calls
            var (response, toolCalls) = await _apiClient.SendMessageWithToolsAsync(
                "meta/llama3-8b-instruct",
                messages,
                tools,
                "auto"
            );

            // Check if the model wants to use tools
            if (toolCalls != null && toolCalls.Count > 0)
            {
                // Add assistant's message with tool calls to conversation
                messages.Add(new ApiChatMessage
                {
                    Role = "assistant",
                    Content = response,
                    ToolCalls = toolCalls
                });

                // Process each tool call
                foreach (var toolCall in toolCalls)
                {
                    if (toolCall.Function.Name == "get_current_weather")
                    {
                        // Parse the arguments
                        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(toolCall.Function.Arguments);
                        var location = args.ContainsKey("location") ? args["location"] : "Unknown";
                        var unit = args.ContainsKey("unit") ? args["unit"] : "fahrenheit";

                        // Execute the actual function (simulated here)
                        var weatherResult = GetCurrentWeather(location, unit);

                        // Add tool response to conversation
                        messages.Add(new ApiChatMessage
                        {
                            Role = "tool",
                            Content = JsonSerializer.Serialize(weatherResult),
                            ToolCallId = toolCall.Id
                        });
                    }
                }

                // Second API call - model will use tool results to generate final response
                var (finalResponse, _) = await _apiClient.SendMessageWithToolsAsync(
                    "meta/llama3-8b-instruct",
                    messages,
                    tools,
                    "auto"
                );

                return finalResponse;
            }

            // No tool calls needed, return direct response
            return response;
        }

        /// <summary>
        /// Simulated weather function - replace with actual weather API
        /// </summary>
        private object GetCurrentWeather(string location, string unit)
        {
            // This is a mock implementation - replace with real weather API
            return new
            {
                location = location,
                temperature = unit == "celsius" ? "22" : "72",
                unit = unit,
                condition = "Sunny",
                humidity = "45%"
            };
        }

        /// <summary>
        /// Example: Using multiple tools
        /// </summary>
        public async Task<string> UseMultipleToolsAsync(string userMessage)
        {
            var tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "search_database",
                        Description = "Search the database for information",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                query = new { type = "string", description = "The search query" }
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
                        Description = "Perform mathematical calculations",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                expression = new { type = "string", description = "The mathematical expression to evaluate" }
                            },
                            required = new[] { "expression" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "get_current_time",
                        Description = "Get the current time in a specific timezone",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                timezone = new { type = "string", description = "IANA timezone identifier, e.g. America/New_York" }
                            },
                            required = new[] { "timezone" }
                        }
                    }
                }
            };

            var messages = new List<ApiChatMessage>
            {
                new ApiChatMessage { Role = "user", Content = userMessage }
            };

            var (response, toolCalls) = await _apiClient.SendMessageWithToolsAsync(
                "meta/llama3-8b-instruct",
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
                    var result = ExecuteTool(toolCall);
                    messages.Add(new ApiChatMessage
                    {
                        Role = "tool",
                        Content = result,
                        ToolCallId = toolCall.Id
                    });
                }

                var (finalResponse, _) = await _apiClient.SendMessageWithToolsAsync(
                    "meta/llama3-8b-instruct",
                    messages,
                    tools
                );

                return finalResponse;
            }

            return response;
        }

        private string ExecuteTool(ToolCall toolCall)
        {
            // Implement your tool execution logic here
            return $"Tool {toolCall.Function.Name} executed successfully";
        }
    }
}
