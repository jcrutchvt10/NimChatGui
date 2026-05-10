using System;
using System.Collections.Generic;
using System.Linq;

namespace NimChatGui
{
    /// <summary>
    /// Generates tool definitions for NVIDIA API based on installed MCP servers.
    /// Maps MCP server types to OpenAPI-style tool definitions.
    /// </summary>
    public class McpToolGenerator
    {
        private readonly McpCatalogClient _catalog;

        public McpToolGenerator(McpCatalogClient catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>
        /// Generate all available tools based on installed MCP servers.
        /// Exposes tools for all installed servers with full admin access.
        /// </summary>
        public List<ToolDefinition> GenerateToolsForInstalledServers()
        {
            var tools = new List<ToolDefinition>();
            var installedServers = _catalog.GetInstalledServers();

            // Expose all installed server tools with full admin access
            foreach (var server in installedServers)
            {
                var serverTools = GenerateToolsForServer(server);
                tools.AddRange(serverTools);
            }

            return tools;
        }

        /// <summary>
        /// Generate tools for a specific MCP server based on its type.
        /// </summary>
        private List<ToolDefinition> GenerateToolsForServer(McpServerDefinition server)
        {
            return server.Name switch
            {
                "filesystem" => GenerateFilesystemTools(server),
                "memory" => GenerateMemoryTools(server),
                "todo" => GenerateTodoTools(server),
                "fetch" => GenerateFetchTools(server),
                "brave-search" => GenerateBraveSearchTools(server),
                "puppeteer" => GeneratePuppeteerTools(server),
                "git" => GenerateGitTools(server),
                "github" => GenerateGithubTools(server),
                "sqlite" => GenerateSqliteTools(server),
                "postgres" => GeneratePostgresTools(server),
                "slack" => GenerateSlackTools(server),
                "linear" => GenerateLinearTools(server),
                _ => GenerateGenericTools(server)
            };
        }

        private List<ToolDefinition> GenerateFilesystemTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "fs_list_files",
                        Description = "List files and directories in a path",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                path = new { type = "string", description = "Path to list" },
                                recursive = new { type = "boolean", description = "Recurse into subdirectories" },
                                max_results = new { type = "integer", description = "Maximum results" }
                            }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "fs_read_file",
                        Description = "Read text file content",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                path = new { type = "string", description = "File path" },
                                max_chars = new { type = "integer", description = "Max characters to read" }
                            },
                            required = new[] { "path" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "fs_search_files",
                        Description = "Search for files by name",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                pattern = new { type = "string", description = "Filename pattern" },
                                path = new { type = "string", description = "Search in path" },
                                max_results = new { type = "integer", description = "Max results" }
                            },
                            required = new[] { "pattern" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "fs_write_file",
                        Description = "Write text file",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                path = new { type = "string", description = "File path" },
                                content = new { type = "string", description = "File content" }
                            },
                            required = new[] { "path", "content" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateMemoryTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "memory_execute",
                        Description = "Execute an action on the memory MCP server (for example: create_entities, create_relations, search_nodes, read_graph)",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                action = new { type = "string", description = "Memory server tool/action name" },
                                args = new { type = "object", description = "Arguments to pass to the selected action" }
                            },
                            required = new[] { "action" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateTodoTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "todo_add",
                        Description = "Add a todo item",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                title = new { type = "string", description = "Todo title" },
                                description = new { type = "string", description = "Todo description" }
                            },
                            required = new[] { "title" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "todo_list",
                        Description = "List all todos",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                status = new { type = "string", description = "Filter by status (all/pending/done)" }
                            }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "todo_complete",
                        Description = "Mark todo as complete",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                id = new { type = "string", description = "Todo ID" }
                            },
                            required = new[] { "id" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateFetchTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "web_fetch",
                        Description = "Fetch and parse web page content",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                url = new { type = "string", description = "URL to fetch" },
                                format = new { type = "string", description = "Output format (html/markdown/text)" }
                            },
                            required = new[] { "url" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateBraveSearchTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "web_search",
                        Description = "Search the web using Brave Search",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                query = new { type = "string", description = "Search query" },
                                count = new { type = "integer", description = "Number of results" }
                            },
                            required = new[] { "query" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GeneratePuppeteerTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "browser_navigate",
                        Description = "Navigate browser to URL and get screenshot/content",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                url = new { type = "string", description = "URL to navigate to" }
                            },
                            required = new[] { "url" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateGitTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "git_log",
                        Description = "Get git commit history",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                path = new { type = "string", description = "Repository path" },
                                limit = new { type = "integer", description = "Number of commits" }
                            }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "git_diff",
                        Description = "Get git diff for files",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                path = new { type = "string", description = "Repository path" }
                            }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateGithubTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "github_search",
                        Description = "Search GitHub repositories",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                query = new { type = "string", description = "Search query" }
                            },
                            required = new[] { "query" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateSqliteTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "db_query",
                        Description = "Execute SQLite query",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                sql = new { type = "string", description = "SQL query" }
                            },
                            required = new[] { "sql" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GeneratePostgresTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "pg_query",
                        Description = "Execute PostgreSQL query",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                sql = new { type = "string", description = "SQL query" }
                            },
                            required = new[] { "sql" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateSlackTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "slack_send_message",
                        Description = "Send message to Slack channel",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                channel = new { type = "string", description = "Channel name or ID" },
                                message = new { type = "string", description = "Message text" }
                            },
                            required = new[] { "channel", "message" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateLinearTools(McpServerDefinition server)
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "linear_create_issue",
                        Description = "Create a Linear issue",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                title = new { type = "string", description = "Issue title" },
                                description = new { type = "string", description = "Issue description" }
                            },
                            required = new[] { "title" }
                        }
                    }
                }
            };
        }

        private List<ToolDefinition> GenerateGenericTools(McpServerDefinition server)
        {
            // For unknown servers, provide a generic tool that passes through
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = $"{server.Name}_execute",
                        Description = $"Execute a command on {server.Name} server",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                resource = new { type = "string", description = "Resource name" },
                                action = new { type = "string", description = "Action to perform" },
                                args = new { type = "object", description = "Action arguments" }
                            },
                            required = new[] { "resource", "action" }
                        }
                    }
                }
            };
        }
    }
}
