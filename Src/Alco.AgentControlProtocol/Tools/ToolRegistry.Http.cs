using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Alco.AgentControlProtocol;

/// <summary>
/// HTTP API adapter for <see cref="ToolRegistry"/>.
/// Maps ASP.NET Minimal API endpoints for tool discovery and invocation.
/// </summary>
public static class ToolRegistryHttpAdapter
{
    /// <summary>
    /// Maps tool API endpoints to the given endpoint route builder.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map endpoints to.</param>
    /// <param name="registry">The tool registry serving tool metadata and invocation.</param>
    public static void MapToolApi(this IEndpointRouteBuilder endpoints, ToolRegistry registry)
    {
        endpoints.MapGet("/", () =>
        {
            return Results.Ok(new
            {
                name = "Alco Game API",
                version = "1.0.0",
            });
        });

        endpoints.MapGet("/status", () =>
        {
            return Results.Ok(new
            {
                running = true,
                toolCount = registry.Tools.Count,
            });
        });

        endpoints.MapGet("/tools", () =>
        {
            var tools = new List<object>();
            foreach (var (name, descriptor) in registry.Tools)
            {
                tools.Add(new
                {
                    name = descriptor.Name,
                    description = descriptor.Description,
                    parameters = descriptor.ParameterSchema,
                    isOnAgentThread = descriptor.IsOnAgentThread,
                });
            }
            return Results.Ok(tools);
        });

        endpoints.MapGet("/tools/{toolName}/schema", (string toolName) =>
        {
            var descriptor = registry.GetTool(toolName);
            if (descriptor == null)
            {
                return Results.NotFound(new
                {
                    success = false,
                    error = $"Tool '{toolName}' not found.",
                    errorType = "ToolNotFoundException",
                });
            }

            return Results.Ok(new
            {
                name = descriptor.Name,
                description = descriptor.Description,
                parameters = descriptor.ParameterSchema,
            });
        });

        endpoints.MapGet("/tools/{toolName}", (string toolName, HttpContext context) =>
            InvokeToolAsync(registry, toolName, default, context));

        endpoints.MapPost("/tools/{toolName}", async (string toolName, HttpContext context) =>
        {
            JsonElement jsonArgs;
            try
            {
                jsonArgs = await context.Request.ReadFromJsonAsync<JsonElement>();
            }
            catch
            {
                jsonArgs = default;
            }

            return await InvokeToolAsync(registry, toolName, jsonArgs, context);
        });
    }

    private static async Task<IResult> InvokeToolAsync(
        ToolRegistry registry, string toolName, JsonElement jsonArgs, HttpContext context)
    {
        var descriptor = registry.GetTool(toolName);
        if (descriptor == null)
        {
            return Results.NotFound(new
            {
                success = false,
                error = $"Tool '{toolName}' not found.",
                errorType = "ToolNotFoundException",
            });
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await registry.InvokeToolAsync(toolName, jsonArgs);
            var elapsed = Stopwatch.GetElapsedTime(start);
            if (result is BinaryToolResult binaryResult)
            {
                return CreateBinaryResponse(context, binaryResult, elapsed);
            }

            return Results.Ok(new
            {
                success = true,
                data = result,
                executionTimeMs = elapsed.TotalMilliseconds,
            });
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            return Results.Ok(new
            {
                success = false,
                error = ex.Message,
                errorType = ex.GetType().Name,
                executionTimeMs = elapsed.TotalMilliseconds,
            });
        }
    }

    private static IResult CreateBinaryResponse(HttpContext context, BinaryToolResult result, TimeSpan elapsed)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Game-Api-Execution-Time-Ms"] = elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture);

        foreach (var (name, value) in result.Headers)
        {
            context.Response.Headers[name] = value;
        }

        return Results.File(result.Data, result.ContentType);
    }
}
