using ModelContextProtocol;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace Alco.Project.Mcp;

internal sealed class Program
{
    static async Task Main(string[] args)
    {
        McpServerOptions options = new()
        {
            ServerInfo = new Implementation() { Name = "MyServer", Version = "1.0.0" },
            Capabilities = new ServerCapabilities()
            {
                Tools = new ToolsCapability()
                {
                    ListToolsHandler = (request, cancellationToken) =>
                        ValueTask.FromResult(new ListToolsResult()
                        {
                            Tools =
                            [
                                new Tool()
                                {
                                    Name = "echo",
                                    Description = "Echoes the input back to the client.",
                                    InputSchema = JsonSerializer.Deserialize<JsonElement>("""
                                        {
                                            "type": "object",
                                            "properties": {
                                            "message": {
                                                "type": "string",
                                                "description": "The input to echo back"
                                            }
                                            },
                                            "required": ["message"]
                                        }
                                        """),
                                },
                                new Tool()
                                {
                                    Name = "create_empty_txt",
                                    Description = "Creates an empty txt file at the specified path.",
                                    InputSchema = JsonSerializer.Deserialize<JsonElement>("""
                                        {
                                            "type": "object",
                                            "properties": {
                                            "filepath": {
                                                "type": "string",
                                                "description": "The path where to create the empty txt file"
                                            }
                                            },
                                            "required": ["filepath"]
                                        }
                                        """),
                                },
                            ]
                        }),

                    CallToolHandler = (request, cancellationToken) =>
                    {
                        if (request.Params?.Name == "echo")
                        {
                            if (request.Params.Arguments?.TryGetValue("message", out var message) is not true)
                            {
                                throw new McpException("Missing required argument 'message'");
                            }

                            return ValueTask.FromResult(new CallToolResponse()
                            {
                                Content = [new Content() { Text = $"Echo: {message}", Type = "text" }]
                            });
                        }
                        else if (request.Params?.Name == "create_empty_txt")
                        {
                            if (request.Params.Arguments?.TryGetValue("filepath", out var filepath) is not true)
                            {
                                throw new McpException("Missing required argument 'filepath'");
                            }

                            try
                            {
                                string path = filepath.ToString();
                                if (!path.EndsWith(".txt"))
                                {
                                    path += ".txt";
                                }

                                File.WriteAllText(path, string.Empty);
                                return ValueTask.FromResult(new CallToolResponse()
                                {
                                    Content = [new Content() { Text = $"Empty txt file created at: {path}", Type = "text" }]
                                });
                            }
                            catch (Exception ex)
                            {
                                throw new McpException($"Failed to create file: {ex.Message}");
                            }
                        }

                        throw new McpException($"Unknown tool: '{request.Params?.Name}'");
                    },
                }
            },
        };

        await using IMcpServer server = McpServerFactory.Create(new StdioServerTransport("MyServer"), options);
        await server.RunAsync();
    }
}