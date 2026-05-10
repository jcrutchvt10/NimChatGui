# Tool Calling in NimApiClient

## Overview

The NimApiClient now supports **tool calling** (also known as function calling), allowing the AI model to intelligently use external tools and functions to provide more accurate and dynamic responses.

## Key Components

### 1. Tool Definition Classes

- **`ToolDefinition`**: Defines a tool/function that can be called by the AI
- **`FunctionDefinition`**: Specifies the function name, description, and parameters
- **`ToolCall`**: Represents a tool call request from the AI
- **`FunctionCall`**: Contains the function name and JSON arguments
- **`ApiChatMessage`**: Extended message format supporting tool calls and responses

### 2. New Method

```csharp
public async Task<(string response, List<ToolCall> toolCalls)> SendMessageWithToolsAsync(
    string modelName, 
    List<ApiChatMessage> messages, 
    List<ToolDefinition> tools = null,
    string toolChoice = "auto")
```

**Parameters:**
- `modelName`: The model to use (e.g., "meta/llama3-8b-instruct")
- `messages`: List of conversation messages including user, assistant, and tool messages
- `tools`: List of available tools (optional)
- `toolChoice`: "auto" (default), "none", or specific function name

**Returns:**
- `response`: The AI's text response
- `toolCalls`: List of tool calls the AI wants to execute (null if none)

## Usage Pattern

### Basic Flow

1. **Define Tools**: Create tool definitions with schemas
2. **Send Initial Request**: Call `SendMessageWithToolsAsync` with user message and tools
3. **Check for Tool Calls**: If the AI requests tool calls, execute them
4. **Add Tool Results**: Add tool responses to the conversation
5. **Get Final Response**: Call `SendMessageWithToolsAsync` again with tool results

### Example: Weather Tool

```csharp
// 1. Define the tool
var tools = new List<ToolDefinition>
{
    new ToolDefinition
    {
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
                        enumValues = new[] { "celsius", "fahrenheit" }
                    }
                },
                required = new[] { "location" }
            }
        }
    }
};

// 2. Initial conversation
var messages = new List<ApiChatMessage>
{
    new ApiChatMessage { Role = "user", Content = "What's the weather in New York?" }
};

// 3. First API call
var (response, toolCalls) = await _apiClient.SendMessageWithToolsAsync(
    "meta/llama3-8b-instruct",
    messages,
    tools
);

// 4. Process tool calls if any
if (toolCalls != null && toolCalls.Count > 0)
{
    // Add assistant's message
    messages.Add(new ApiChatMessage
    {
        Role = "assistant",
        Content = response,
        ToolCalls = toolCalls
    });

    // Execute each tool
    foreach (var toolCall in toolCalls)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(
            toolCall.Function.Arguments
        );
        var weatherData = GetCurrentWeather(args["location"], args.GetValueOrDefault("unit"));

        // Add tool result
        messages.Add(new ApiChatMessage
        {
            Role = "tool",
            Content = JsonSerializer.Serialize(weatherData),
            ToolCallId = toolCall.Id
        });
    }

    // 5. Get final response
    var (finalResponse, _) = await _apiClient.SendMessageWithToolsAsync(
        "meta/llama3-8b-instruct",
        messages,
        tools
    );

    return finalResponse;
}
```

## Tool Definition Schema

Tools follow the OpenAI function calling schema:

```csharp
new ToolDefinition
{
    Type = "function",  // Always "function"
    Function = new FunctionDefinition
    {
        Name = "function_name",
        Description = "Clear description of what the function does",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                param1 = new
                {
                    type = "string",
                    description = "Description of param1"
                },
                param2 = new
                {
                    type = "number",
                    description = "Description of param2"
                }
            },
            required = new[] { "param1" }
        }
    }
}
```

## Message Types

### User Message
```csharp
new ApiChatMessage { Role = "user", Content = "Your question here" }
```

### Assistant Message (with tool calls)
```csharp
new ApiChatMessage
{
    Role = "assistant",
    Content = "Optional text response",
    ToolCalls = toolCallsList
}
```

### Tool Response Message
```csharp
new ApiChatMessage
{
    Role = "tool",
    Content = JsonSerializer.Serialize(toolResult),
    ToolCallId = toolCall.Id
}
```

## Common Use Cases

### 1. Database Queries
Create tools to search, retrieve, or modify database records.

### 2. External APIs
Integrate weather, stock prices, news, or other external data sources.

### 3. Calculations
Perform complex calculations or data processing.

### 4. File Operations
Read, write, or analyze files and documents.

### 5. System Actions
Execute system commands or interact with external services.

## Tool Choice Options

- **`"auto"`** (default): Model decides whether to use tools
- **`"none"`**: Model will not use any tools
- **`"required"`**: Model must use at least one tool
- **`{"type": "function", "function": {"name": "specific_function"}}`**: Force specific tool

## Best Practices

1. **Clear Descriptions**: Provide detailed function and parameter descriptions
2. **Type Safety**: Use proper JSON schema types (string, number, boolean, object, array)
3. **Error Handling**: Always wrap tool execution in try-catch blocks
4. **Validation**: Validate tool arguments before execution
5. **Security**: Sanitize and validate all inputs from tool calls
6. **Logging**: Log tool calls and results for debugging
7. **Timeout**: Implement timeouts for long-running tool operations

## Example Tools

See `ToolCallingExample.cs` for complete examples including:
- Weather information retrieval
- Database search
- Mathematical calculations
- Time zone conversions
- Multi-tool scenarios

## Integration with UI

To integrate tool calling in the main UI:

1. Define your tools collection
2. Maintain conversation history with `ApiChatMessage` objects
3. Call `SendMessageWithToolsAsync` instead of `SendMessageAsync`
4. Handle tool calls in a loop until no more are requested
5. Display both AI responses and tool execution results in the chat

## Troubleshooting

**Issue**: Model doesn't call tools
- Ensure tool descriptions are clear and relevant to the query
- Try using `toolChoice = "required"` to force tool usage
- Check that your model supports function calling

**Issue**: JSON parsing errors
- Validate tool argument schemas match your expectations
- Add error handling for malformed JSON responses
- Log the raw arguments string for debugging

**Issue**: Infinite tool calling loop
- Implement a maximum iteration counter
- Validate tool results before adding to conversation
- Ensure tool results contain the information the model needs

## Additional Resources

- OpenAI Function Calling Documentation
- NVIDIA NIM API Documentation
- JSON Schema Specification
