# Tool Calling - Quick Start Guide

## 5-Minute Integration

### Step 1: Define a Simple Tool (30 seconds)

```csharp
var tools = new List<ToolDefinition>
{
    new ToolDefinition
    {
        Function = new FunctionDefinition
        {
            Name = "get_current_time",
            Description = "Get the current time",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    timezone = new
                    {
                        type = "string",
                        description = "Timezone like 'America/New_York'"
                    }
                },
                required = new string[] { }
            }
        }
    }
};
```

### Step 2: Prepare Your Message (15 seconds)

```csharp
var messages = new List<ApiChatMessage>
{
    new ApiChatMessage 
    { 
        Role = "user", 
        Content = "What time is it?" 
    }
};
```

### Step 3: Call the API (30 seconds)

```csharp
var (response, toolCalls) = await _apiClient.SendMessageWithToolsAsync(
    "meta/llama3-8b-instruct",
    messages,
    tools
);
```

### Step 4: Handle Tool Calls (2 minutes)

```csharp
if (toolCalls != null && toolCalls.Count > 0)
{
    // Add assistant's message to history
    messages.Add(new ApiChatMessage
    {
        Role = "assistant",
        Content = response,
        ToolCalls = toolCalls
    });

    // Execute the tool
    foreach (var toolCall in toolCalls)
    {
        // Your tool execution logic here
        var currentTime = DateTime.Now.ToString("h:mm tt");

        // Add result to conversation
        messages.Add(new ApiChatMessage
        {
            Role = "tool",
            Content = JsonSerializer.Serialize(new { time = currentTime }),
            ToolCallId = toolCall.Id
        });
    }

    // Get final response with tool results
    var (finalResponse, _) = await _apiClient.SendMessageWithToolsAsync(
        "meta/llama3-8b-instruct",
        messages,
        tools
    );

    Console.WriteLine(finalResponse);
}
else
{
    Console.WriteLine(response);
}
```

### Step 5: Run and Test! (1 minute)

That's it! You now have a working tool calling implementation.

---

## Real-World Example: Weather Tool

```csharp
public async Task<string> AskAboutWeather(string city)
{
    var tools = new List<ToolDefinition>
    {
        new ToolDefinition
        {
            Function = new FunctionDefinition
            {
                Name = "get_weather",
                Description = "Get current weather for a city",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        city = new { type = "string", description = "City name" }
                    },
                    required = new[] { "city" }
                }
            }
        }
    };

    var messages = new List<ApiChatMessage>
    {
        new ApiChatMessage { Role = "user", Content = $"What's the weather in {city}?" }
    };

    var (response, toolCalls) = await _apiClient.SendMessageWithToolsAsync(
        "meta/llama3-8b-instruct", messages, tools);

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
            // Call your weather API here
            var weatherData = new { temp = 72, condition = "Sunny" };

            messages.Add(new ApiChatMessage
            {
                Role = "tool",
                Content = JsonSerializer.Serialize(weatherData),
                ToolCallId = toolCall.Id
            });
        }

        var (final, _) = await _apiClient.SendMessageWithToolsAsync(
            "meta/llama3-8b-instruct", messages, tools);
        return final;
    }

    return response;
}
```

---

## Common Patterns

### Pattern 1: Single Tool
```csharp
// One tool, one call
var result = await CallWithSingleTool(userMessage, myTool);
```

### Pattern 2: Multiple Tools
```csharp
// AI chooses which tool(s) to use
var tools = new List<ToolDefinition> { tool1, tool2, tool3 };
var result = await CallWithMultipleTools(userMessage, tools);
```

### Pattern 3: Optional Tools
```csharp
// Toggle tools on/off
var result = enableTools 
    ? await CallWithTools(message, tools)
    : await _apiClient.SendMessageAsync(model, message);
```

### Pattern 4: Multi-Turn Conversation
```csharp
// Keep conversation history
var messages = new List<ApiChatMessage>();
messages.Add(new ApiChatMessage { Role = "user", Content = msg1 });

// Call 1
var (resp1, calls1) = await _apiClient.SendMessageWithToolsAsync(...);
ProcessToolCalls(messages, calls1);

// Add another user message
messages.Add(new ApiChatMessage { Role = "user", Content = msg2 });

// Call 2 with full history
var (resp2, calls2) = await _apiClient.SendMessageWithToolsAsync(...);
```

---

## Testing Your Implementation

### Test 1: Tool Gets Called
```
User: "What time is it?"
Expected: AI calls get_current_time tool
```

### Test 2: No Tool Needed
```
User: "Hello, how are you?"
Expected: AI responds directly without tools
```

### Test 3: Multiple Tools
```
User: "What's the weather and time in NYC?"
Expected: AI calls both weather and time tools
```

### Test 4: Invalid Tool Args
```
Test your error handling by providing malformed tool responses
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| AI never calls tools | Make tool descriptions more specific and relevant |
| Wrong tool called | Improve tool descriptions to be more distinct |
| JSON parse errors | Validate your Parameters schema structure |
| Null tool calls | Check if your model supports function calling |
| Infinite loop | Add max iteration counter (e.g., max 5 tool calls) |

---

## Next Steps

1. ? Try the examples above
2. ?? Read `TOOL_CALLING_README.md` for details
3. ?? Check `ToolCallingExample.cs` for more patterns
4. ?? Review `MainWindowToolIntegration.cs` for UI integration
5. ?? See `TOOL_CALLING_FLOW.md` for visual diagrams

---

## Need Help?

- **Concept unclear?** ? See `TOOL_CALLING_README.md`
- **How to integrate?** ? See `MainWindowToolIntegration.cs`
- **Visual learner?** ? See `TOOL_CALLING_FLOW.md`
- **Want examples?** ? See `ToolCallingExample.cs`

---

## Pro Tips

?? **Start Simple**: Begin with one tool that doesn't take parameters  
?? **Log Everything**: Print tool calls and results during development  
?? **Test Edge Cases**: What if tool fails? Returns null? Takes too long?  
?? **Iterate**: Start with auto, then try forced tool usage  
?? **Validate**: Always validate tool arguments before execution  

Happy coding! ??
