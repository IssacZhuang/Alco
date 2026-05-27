using System;
using System.Collections.Generic;

namespace Alco.LLM;

/// <summary>
/// Base type for structured, real-time events emitted by <see cref="LLMSession"/>.
/// These events are not persisted by the session; callers can collect them if needed.
/// </summary>
public abstract record LLMSessionEvent(DateTimeOffset Timestamp);

/// <summary>
/// Emitted before a streaming model request starts.
/// </summary>
public sealed record RequestStartedEvent(
    DateTimeOffset Timestamp,
    int RequestIndex) : LLMSessionEvent(Timestamp);

/// <summary>
/// Emitted for assistant text streamed from the model.
/// </summary>
public sealed record TextDeltaEvent(
    DateTimeOffset Timestamp,
    string Text) : LLMSessionEvent(Timestamp);

/// <summary>
/// Emitted when the model requests a tool call.
/// </summary>
public sealed record ToolCallStartedEvent(
    DateTimeOffset Timestamp,
    string CallId,
    string ToolName,
    IReadOnlyDictionary<string, object?>? Arguments) : LLMSessionEvent(Timestamp);

/// <summary>
/// Emitted after a tool call completes successfully.
/// </summary>
public sealed record ToolCallCompletedEvent(
    DateTimeOffset Timestamp,
    string CallId,
    string ToolName,
    object? Result,
    TimeSpan Duration) : LLMSessionEvent(Timestamp);

/// <summary>
/// Emitted after a tool call fails, times out, or cannot be invoked.
/// </summary>
public sealed record ToolCallFailedEvent(
    DateTimeOffset Timestamp,
    string CallId,
    string ToolName,
    string Error,
    string ErrorType,
    TimeSpan Duration) : LLMSessionEvent(Timestamp);

/// <summary>
/// Emitted after a streaming model request and its immediate tool handling complete.
/// </summary>
public sealed record RequestCompletedEvent(
    DateTimeOffset Timestamp,
    int RequestIndex) : LLMSessionEvent(Timestamp);
