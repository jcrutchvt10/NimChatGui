using System.Collections.Generic;

namespace NimChatGui
{
    /// <summary>
    /// Represents an MCP (Model Context Protocol) server definition
    /// </summary>
    public class McpServerDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Command { get; set; }
        public List<string> Args { get; set; }
        public Dictionary<string, string> Env { get; set; }
        public string Category { get; set; }
        public string Author { get; set; }
        public string SourceUrl { get; set; }
        public string IconUrl { get; set; }
        public bool IsInstalled { get; set; }
    }

    /// <summary>
    /// Represents the MCP catalog response
    /// </summary>
    public class McpCatalog
    {
        public List<McpServerDefinition> Servers { get; set; }
        public string Version { get; set; }
        public string LastUpdated { get; set; }
    }
}
