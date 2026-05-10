using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Alco.LLM;

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
                    isAsyncSafe = descriptor.IsAsyncSafe,
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

        endpoints.MapPost("/tools/{toolName}", async (string toolName, HttpContext context) =>
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

            JsonElement jsonArgs;
            try
            {
                jsonArgs = await context.Request.ReadFromJsonAsync<JsonElement>();
            }
            catch
            {
                jsonArgs = default;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var result = await registry.InvokeToolAsync(toolName, jsonArgs);
                sw.Stop();
                return Results.Ok(new
                {
                    success = true,
                    data = result,
                    executionTimeMs = sw.ElapsedMilliseconds,
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Results.Ok(new
                {
                    success = false,
                    error = ex.Message,
                    errorType = ex.GetType().Name,
                    executionTimeMs = sw.ElapsedMilliseconds,
                });
            }
        });
    }
}
