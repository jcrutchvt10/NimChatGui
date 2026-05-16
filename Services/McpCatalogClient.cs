using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NimChatGui
{
    public class McpCatalogClient
    {
        private readonly HttpClient _httpClient;
        private List<McpServerDefinition> _installedServers;
        private readonly string _configPath;
        private List<McpServerDefinition>? _cachedCatalog;

        public McpCatalogClient()
        {
            _httpClient = new HttpClient();
            _installedServers = new List<McpServerDefinition>();

            var appData = FileSystem.AppDataDirectory;
            var configDir = Path.Combine(appData, "mcp");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "installed-servers.json");

            LoadInstalledServers();
        }

        public List<McpServerDefinition> GetBuiltInCatalog()
        {
            var userDir = FileSystem.AppDataDirectory;
            return new List<McpServerDefinition>
            {
                new McpServerDefinition { Name = "filesystem", Description = "Access and manipulate local files and directories", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-filesystem", userDir }, Category = "Files", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem" },
                new McpServerDefinition { Name = "memory", Description = "Persistent memory and context storage for conversations", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-memory" }, Category = "Storage", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/memory" },
                new McpServerDefinition { Name = "sqlite", Description = "Local SQLite database for queries and data storage", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-sqlite", $"{userDir}/data.db" }, Category = "Database", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/sqlite" },
                new McpServerDefinition { Name = "git", Description = "Git operations: read repos, commits, branches, diffs", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-git", userDir }, Category = "Development", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/git" },
                new McpServerDefinition { Name = "github", Description = "GitHub: issues, PRs, repos, code search", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-github" }, Env = new Dictionary<string, string> { { "GITHUB_TOKEN", "" } }, Category = "Development", Author = "GitHub", SourceUrl = "https://github.com/github/github-mcp-server" },
                new McpServerDefinition { Name = "gitlab", Description = "GitLab: projects, issues, merge requests", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-gitlab" }, Env = new Dictionary<string, string> { { "GITLAB_TOKEN", "" }, { "GITLAB_URL", "https://gitlab.com" } }, Category = "Development", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "sequentialthinking", Description = "Step-by-step reasoning and problem solving", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-sequentialthinking" }, Category = "Development", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/sequentialthinking" },
                new McpServerDefinition { Name = "fetch", Description = "Web content fetching, scraping, HTML to markdown", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-fetch" }, Category = "Web", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/fetch" },
                new McpServerDefinition { Name = "puppeteer", Description = "Browser automation, web scraping, testing", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-puppeteer" }, Category = "Web", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/puppeteer" },
                new McpServerDefinition { Name = "playwright", Description = "Browser automation using Microsoft Playwright", Command = "npx", Args = new List<string> { "-y", "@microsoft/playwright-mcp" }, Category = "Web", Author = "Microsoft", SourceUrl = "https://github.com/microsoft/playwright-mcp" },
                new McpServerDefinition { Name = "webdriverio", Description = "Web and mobile automation with WebDriverIO", Command = "npx", Args = new List<string> { "-y", "@webdriverio/mcp" }, Category = "Web", Author = "WebDriverIO", SourceUrl = "https://github.com/webdriverio/mcp" },
                new McpServerDefinition { Name = "firecrawl", Description = "AI-powered web scraping and extraction", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-firecrawl" }, Env = new Dictionary<string, string> { { "FIRECRAWL_API_KEY", "" } }, Category = "Web", Author = "Firecrawl", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "brave-search", Description = "Web search using Brave Search API", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-brave-search" }, Env = new Dictionary<string, string> { { "BRAVE_API_KEY", "" } }, Category = "Search", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/brave-search" },
                new McpServerDefinition { Name = "slack", Description = "Slack: channels, messages, posts", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-slack" }, Env = new Dictionary<string, string> { { "SLACK_BOT_TOKEN", "" } }, Category = "Communication", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/slack" },
                new McpServerDefinition { Name = "discord", Description = "Discord: messages, channels, server management", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-discord" }, Env = new Dictionary<string, string> { { "DISCORD_BOT_TOKEN", "" } }, Category = "Communication", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "linear", Description = "Linear: issues, projects, comments", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-linear" }, Env = new Dictionary<string, string> { { "LINEAR_API_KEY", "" } }, Category = "Communication", Author = "Linear", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "postgres", Description = "PostgreSQL database queries and management", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-postgres", "postgresql://localhost/mydb" }, Category = "Database", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/postgres" },
                new McpServerDefinition { Name = "mysql", Description = "MySQL database queries and management", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-mysql" }, Category = "Database", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "mongodb", Description = "MongoDB NoSQL database operations", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-mongodb" }, Category = "Database", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "redis", Description = "Redis in-memory data store", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-redis" }, Category = "Database", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "teradata", Description = "Teradata database queries and analysis", Command = "npx", Args = new List<string> { "-y", "@teradata/teradata-mcp-server" }, Category = "Database", Author = "Teradata", SourceUrl = "https://github.com/Teradata/teradata-mcp-server" },
                new McpServerDefinition { Name = "aws-sdk-core", Description = "AWS services via AWS SDK", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-aws-sdk" }, Env = new Dictionary<string, string> { { "AWS_ACCESS_KEY_ID", "" }, { "AWS_SECRET_ACCESS_KEY", "" } }, Category = "Cloud", Author = "AWS", SourceUrl = "https://github.com/aws-samples/mcp-servers" },
                new McpServerDefinition { Name = "google-maps", Description = "Google Maps: geocoding, directions, places", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-google-maps" }, Env = new Dictionary<string, string> { { "GOOGLE_MAPS_API_KEY", "" } }, Category = "Location", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src/google-maps" },
                new McpServerDefinition { Name = "google-workspace", Description = "Gmail, Drive, Calendar integration", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-google-workspace" }, Env = new Dictionary<string, string> { { "GOOGLE_SERVICE_ACCOUNT_KEY", "" } }, Category = "Cloud", Author = "Google", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "azure-devops", Description = "Azure DevOps: repos, work items, builds", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-azure-devops" }, Env = new Dictionary<string, string> { { "AZURE_DEVOPS_PAT", "" } }, Category = "Cloud", Author = "Microsoft", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "cryptocurrency", Description = "Real-time cryptocurrency data and market info", Command = "npx", Args = new List<string> { "-y", "@desk3/cryptocurrency-mcp-server" }, Category = "Blockchain", Author = "Desk3", SourceUrl = "https://github.com/desk3/cryptocurrency-mcp-server" },
                new McpServerDefinition { Name = "pentagonal", Description = "AI-powered smart contract audit and generation", Command = "npx", Args = new List<string> { "-y", "@pentagonal-ai/pentagonal" }, Category = "Blockchain", Author = "Pentagonal", SourceUrl = "https://github.com/Pentagonal-ai/pentagonal" },
                new McpServerDefinition { Name = "todo", Description = "Manage tasks and todos with memory persistence", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-todo" }, Category = "Productivity", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "docfork", Description = "Access live documentation for 9000+ libraries", Command = "npx", Args = new List<string> { "-y", "@docfork/docfork" }, Category = "Documentation", Author = "Docfork", SourceUrl = "https://github.com/docfork/docfork" },
                new McpServerDefinition { Name = "everart", Description = "Image generation using AI models", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-everart" }, Env = new Dictionary<string, string> { { "EVERART_API_KEY", "" } }, Category = "AI", Author = "Everart", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "ip2location", Description = "IP geolocation lookup", Command = "npx", Args = new List<string> { "-y", "@ip2location/mcp-ip2location-io" }, Env = new Dictionary<string, string> { { "IP2LOCATION_API_KEY", "" } }, Category = "Location", Author = "IP2Location", SourceUrl = "https://github.com/ip2location/mcp-ip2location-io" },
                new McpServerDefinition { Name = "ip2whois", Description = "WHOIS domain registration lookup", Command = "npx", Args = new List<string> { "-y", "@ip2whois/mcp-ip2whois" }, Env = new Dictionary<string, string> { { "IP2WHOIS_API_KEY", "" } }, Category = "Location", Author = "IP2WHOIS", SourceUrl = "https://github.com/ip2whois/mcp-ip2whois" },
                new McpServerDefinition { Name = "pdf-export", Description = "Generate professional PDFs from prompts", Command = "npx", Args = new List<string> { "-y", "@pdfcrowd/pdfcrowd-mcp-pdf-export" }, Env = new Dictionary<string, string> { { "PDFCROWD_API_KEY", "" } }, Category = "Media", Author = "PDFCrowd", SourceUrl = "https://github.com/pdfcrowd/pdfcrowd-mcp-pdf-export" },
                new McpServerDefinition { Name = "sentry", Description = "Sentry error monitoring and debugging", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-sentry" }, Env = new Dictionary<string, string> { { "SENTRY_AUTH_TOKEN", "" } }, Category = "Monitoring", Author = "Sentry", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "time", Description = "Time, timezone, and date operations", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-time" }, Category = "Utilities", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers/tree/main/src" },
                new McpServerDefinition { Name = "everything", Description = "Local file search and system queries (Windows)", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-everything" }, Category = "Utilities", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "openapi", Description = "OpenAPI/REST API client and executor", Command = "npx", Args = new List<string> { "-y", "@modelcontextprotocol/server-openapi" }, Category = "Utilities", Author = "MCP Team", SourceUrl = "https://github.com/modelcontextprotocol/servers" },
                new McpServerDefinition { Name = "mockzilla", Description = "API mocking and OpenAPI spec testing", Command = "npx", Args = new List<string> { "-y", "@mockzilla/mockzilla-mcp" }, Category = "Testing", Author = "Mockzilla", SourceUrl = "https://github.com/mockzilla/mockzilla-mcp" },
                new McpServerDefinition { Name = "whodb", Description = "Database management and query execution", Command = "npx", Args = new List<string> { "-y", "@clidey/whodb" }, Category = "Database", Author = "WhoDB", SourceUrl = "https://github.com/clidey/whodb" },
                new McpServerDefinition { Name = "context7", Description = "Document loading and vector search integration", Command = "npx", Args = new List<string> { "-y", "@context7/mcp-server" }, Env = new Dictionary<string, string> { { "CONTEXT7_TOKEN", "" } }, Category = "AI", Author = "Context7", SourceUrl = "https://github.com/context7/mcp-server" },
                new McpServerDefinition { Name = "financial-datasets", Description = "Stock market data, financial statements, news", Command = "npx", Args = new List<string> { "-y", "@financial-datasets/mcp-server" }, Env = new Dictionary<string, string> { { "FINANCIAL_DATASETS_API_KEY", "" } }, Category = "Finance", Author = "Financial Datasets", SourceUrl = "https://github.com/financial-datasets/mcp-server" },
                new McpServerDefinition { Name = "decision-node", Description = "Record and search development decisions", Command = "npx", Args = new List<string> { "-y", "@decisionnode/DecisionNode" }, Category = "Documentation", Author = "DecisionNode", SourceUrl = "https://github.com/decisionnode/DecisionNode" }
            };
        }

        public async Task<List<McpServerDefinition>> FetchCatalogAsync(string catalogUrl = null)
        {
            if (_cachedCatalog != null)
                return _cachedCatalog;

            var catalog = GetBuiltInCatalog();

            foreach (var server in catalog)
            {
                server.IsInstalled = _installedServers.Any(s =>
                    s.Name.Equals(server.Name, StringComparison.OrdinalIgnoreCase));
            }

            _cachedCatalog = catalog;
            return catalog;
        }

        public void InstallServer(McpServerDefinition server)
        {
            if (!_installedServers.Any(s => s.Name.Equals(server.Name, StringComparison.OrdinalIgnoreCase)))
            {
                server.IsInstalled = true;
                _installedServers.Add(new McpServerDefinition
                {
                    Name = server.Name,
                    Description = server.Description,
                    Command = server.Command,
                    Args = server.Args,
                    Env = server.Env,
                    Category = server.Category,
                    Author = server.Author,
                    SourceUrl = server.SourceUrl,
                    IsInstalled = true
                });
                SaveInstalledServers();

                if (_cachedCatalog != null)
                {
                    var cached = _cachedCatalog.FirstOrDefault(s => s.Name == server.Name);
                    if (cached != null) cached.IsInstalled = true;
                }
            }
        }

        public void UninstallServer(McpServerDefinition server)
        {
            var existing = _installedServers.FirstOrDefault(s =>
                s.Name.Equals(server.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                _installedServers.Remove(existing);
                SaveInstalledServers();

                if (_cachedCatalog != null)
                {
                    var cached = _cachedCatalog.FirstOrDefault(s => s.Name == server.Name);
                    if (cached != null) cached.IsInstalled = false;
                }
            }
        }

        public List<McpServerDefinition> GetInstalledServers()
        {
            return _installedServers.ToList();
        }

        private void LoadInstalledServers()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var servers = JsonSerializer.Deserialize<List<McpServerDefinition>>(json);
                    if (servers != null)
                    {
                        _installedServers = servers;
                        foreach (var server in _installedServers)
                            server.IsInstalled = true;
                    }
                }
            }
            catch
            {
            }
        }

        private void SaveInstalledServers()
        {
            try
            {
                var json = JsonSerializer.Serialize(_installedServers, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_configPath, json);
            }
            catch
            {
            }
        }
    }
}
