# MCP Server Catalog Integration

## Overview

The NIM Chat GUI now includes a built-in **MCP (Model Context Protocol) Server Catalog** browser that allows you to discover, browse, and one-click install MCP servers to extend your AI's capabilities.

## What is MCP?

Model Context Protocol (MCP) is an open standard that enables AI assistants to securely connect to external data sources and tools. MCP servers provide:

- **File System Access** - Read and manipulate local files
- **Database Queries** - Connect to PostgreSQL, MySQL, etc.
- **Web Integration** - Search engines, web scraping, APIs
- **Developer Tools** - GitHub, Slack, Git operations
- **And More** - Memory, location services, custom integrations

## Features

### ?? Browse Online Catalog
- Fetches the latest MCP server catalog from GitHub
- Falls back to built-in sample catalog if offline
- Categorized servers (Files, Development, Database, Web, etc.)

### ?? Search and Filter
- **Search** - Find servers by name, description, or category
- **Category Filter** - Browse by type (Development, Database, Web, etc.)
- **Real-time filtering** - Instant results as you type

### ? One-Click Installation
- Simple "Add" button to install servers
- Servers are saved to your local configuration
- "Remove" button to uninstall servers
- View installed servers anytime

### ?? Persistent Configuration
- Installed servers saved to: `%AppData%\NimChatGui\mcp\installed-servers.json`
- Survives app restarts
- Easy to backup and restore

## How to Use

### Opening the Catalog

1. Launch NIM Chat GUI
2. Look in the left sidebar
3. Click the **"Browse MCP Servers"** button (blue accent button)
4. The catalog dialog will open and automatically load available servers

### Installing a Server

1. Browse or search for the server you want
2. Click the **"Add"** button on the server card
3. The server is now installed and saved
4. Button changes to "Remove" to indicate installed status

### Viewing Installed Servers

1. Open the MCP Catalog
2. Look at the bottom of the dialog
3. See installed count: "X server(s) installed"
4. Click **"View Installed"** to see your installed servers

### Uninstalling a Server

1. Find the server in the catalog
2. Click the **"Remove"** button
3. Server is uninstalled and removed from configuration

### Refreshing the Catalog

- Click **"Refresh Catalog"** to reload from the web
- Gets latest server definitions
- Updates installed status

## Available Server Categories

The catalog includes servers organized by category:

- **Files** - File system access and manipulation
- **Development** - GitHub, Git, code repositories
- **Database** - PostgreSQL, MySQL, SQLite
- **Web** - Web scraping, browsers, HTTP
- **Search** - Brave Search, Google, web search
- **Communication** - Slack, Discord, email
- **Location** - Google Maps, geocoding
- **Utilities** - Memory, calculations, time
- **General** - Miscellaneous tools

## Sample MCP Servers

Here are some popular MCP servers included:

### ?? Filesystem
- **Description**: Access and manipulate local files and directories
- **Command**: `npx -y @modelcontextprotocol/server-filesystem /path/to/files`
- **Use Case**: Read, write, search files on your computer

### ?? GitHub
- **Description**: Interact with GitHub repositories, issues, and pull requests
- **Command**: `npx -y @modelcontextprotocol/server-github`
- **Requires**: `GITHUB_TOKEN` environment variable
- **Use Case**: Query repos, create issues, manage PRs

### ?? Memory
- **Description**: Persistent memory and context storage for conversations
- **Command**: `npx -y @modelcontextprotocol/server-memory`
- **Use Case**: AI remembers context across sessions

### ?? PostgreSQL
- **Description**: Query and manage PostgreSQL databases
- **Command**: `npx -y @modelcontextprotocol/server-postgres postgresql://localhost/mydb`
- **Use Case**: Database queries, schema inspection

### ?? Puppeteer
- **Description**: Browser automation and web scraping
- **Command**: `npx -y @modelcontextprotocol/server-puppeteer`
- **Use Case**: Automate browsers, scrape websites

### ?? Brave Search
- **Description**: Search the web using Brave Search API
- **Command**: `npx -y @modelcontextprotocol/server-brave-search`
- **Requires**: `BRAVE_API_KEY` environment variable
- **Use Case**: Web search, current information

### ?? Slack
- **Description**: Interact with Slack workspaces, channels, and messages
- **Command**: `npx -y @modelcontextprotocol/server-slack`
- **Requires**: `SLACK_BOT_TOKEN`, `SLACK_TEAM_ID`
- **Use Case**: Read/send messages, manage channels

### ??? Google Maps
- **Description**: Search and get information about locations
- **Command**: `npx -y @modelcontextprotocol/server-google-maps`
- **Requires**: `GOOGLE_MAPS_API_KEY`
- **Use Case**: Location search, directions, places

## Server Configuration

Each server in the catalog includes:

- **Name** - Unique identifier
- **Description** - What the server does
- **Command** - How to run it (usually npx)
- **Args** - Command line arguments
- **Environment Variables** - Required API keys/config
- **Category** - Type of functionality
- **Author** - Creator/maintainer
- **Source URL** - GitHub repository link

## Technical Details

### Files Created

1. **`McpServerDefinition.cs`** - Data models for servers and catalog
2. **`McpCatalogClient.cs`** - Fetches and manages the catalog
3. **`McpCatalogDialog.cs`** - UI for browsing and installing

### Catalog Source

- **Default URL**: `https://raw.githubusercontent.com/modelcontextprotocol/servers/main/servers.json`
- **Fallback**: Built-in sample catalog with 8 popular servers
- **Custom**: Can be extended to support custom catalog URLs

### Storage Location

Installed servers are stored in:
```
%AppData%\NimChatGui\mcp\installed-servers.json
```

Example format:
```json
[
  {
    "Name": "filesystem",
    "Description": "Access and manipulate local files and directories",
    "Command": "npx",
    "Args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/files"],
    "Category": "Files",
    "Author": "MCP Team",
    "SourceUrl": "https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem",
    "IsInstalled": true
  }
]
```

## Next Steps

### Using Installed Servers

Once you've installed MCP servers, you can:

1. **Configure Environment Variables** - Set required API keys
2. **Start Servers** - Launch them alongside your NIM containers
3. **Integrate with Tools** - Use with the tool calling feature
4. **Create Workflows** - Combine multiple servers for complex tasks

### Integration with Tool Calling

MCP servers expose tools that can be called by the AI using the tool calling feature:

```csharp
// Example: Using an installed MCP server's tools
var tools = new List<ToolDefinition>
{
    new ToolDefinition
    {
        Function = new FunctionDefinition
        {
            Name = "read_file",
            Description = "Read contents of a file",
            Parameters = new { /* ... */ }
        }
    }
};
```

## Troubleshooting

### Catalog won't load
- Check internet connection
- App falls back to sample catalog automatically
- Click "Refresh Catalog" to retry

### Server won't install
- Check if already installed
- Try removing and reinstalling
- Check write permissions for AppData folder

### Can't find a server
- Use the search box
- Try filtering by category
- Check if you're looking at the right category

### Installed servers not showing
- Click "View Installed" button
- Check the installed count at bottom
- Verify the config file exists in AppData

## Future Enhancements

Planned features:
- ? Browse and install servers (DONE)
- ? Automatic server startup
- ? Environment variable configuration UI
- ? Test server connectivity
- ? Custom catalog URLs
- ? Server dependencies management
- ? Import/export configurations

## Resources

- **MCP Documentation**: https://modelcontextprotocol.io
- **Official Servers**: https://github.com/modelcontextprotocol/servers
- **MCP Specification**: https://spec.modelcontextprotocol.io
- **Community Servers**: https://github.com/topics/mcp-server

## Contributing

Want to add your own MCP server to the catalog?

1. Create your MCP server following the spec
2. Publish to npm as `@yourname/mcp-server-name`
3. Submit to the official catalog repository
4. Your server will appear in the catalog after merge

---

**Note**: Installing a server only saves the configuration. You still need to start the server process separately before the AI can use it. Future updates will include automatic server management.
