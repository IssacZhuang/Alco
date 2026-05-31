# Alco.LLM 结构化 Session Event - 设计规格

## Request

实现 Alco.LLM Agent 基础能力的阶段 2：为 `LLMSession` 增加一个结构化、实时、非持久的事件输出口，让调用方可以观察模型文本、模型请求、工具调用开始、工具调用完成和工具调用失败。现有字符串 streaming API 必须保持兼容。

## Design Intent

阶段 1 已经让工具调用链路更可靠，但 `LLMSession.ChatStreamingAsync` 仍然只输出 `string`。这意味着 session 内部发生的事在离开 `LLMSession` 时已经被压扁成文本：模型文本、工具名、工具参数、工具成功/失败都混在同一个字符串流里。短期 demo 能用，但后续 UI、debug log、测试、context budget、NPC wrapper 都需要重新从字符串里猜语义。

阶段 2 的目标不是实现完整 Claude Code runtime，也不是做 UI 大改，而是给 `LLMSession` 留出一个干净的结构化输出口：

```csharp
await foreach (var ev in session.ChatEventsAsync(message, cancellationToken))
{
    // UI/debug/test/NPC wrapper can consume typed events here.
}
```

这个事件流是实时输出，不是持久 transcript。`LLMSession` 继续维护现有 `_chatHistory` 作为发给 provider 的模型历史，但不在本阶段新增长期 event log。需要记录事件的调用方可以自行收集 `ChatEventsAsync` 的输出。

## Current State

### `LLMSession`

文件：`Alco/Src/Alco.LLM/Session/LLMSession.cs`

当前行为：

- `ChatAsync` 使用非 streaming `GetResponseAsync`，维护 `_chatHistory`，并自动调用工具。
- `ChatStreamingAsync` 使用 `GetStreamingResponseAsync`，直接 yield 文本 chunk。
- streaming 中遇到 `FunctionCallContent` 时，会把工具名和参数序列化成字符串 chunk，例如 `[Add]` 和 `{"a":2,"b":3}`。
- 工具调用成功或失败后，`InvokeToolCallsAsync` 会把 `FunctionResultContent` 写入 `_chatHistory`，但调用方不会实时收到结构化工具结果事件。
- 阶段 1 已完成：工具返回 `Task`/`Task<T>` 会被 await，工具 timeout/失败会被结构化写回模型历史，`AutoInvokeTools = false` 已生效。

### 测试

文件：`Alco/Test/Alco.LLM.Test/ToolCallLoopTests.cs`

当前测试：

- `ChatStreamingAsync_TextOnly_YieldsTextChunks` 断言字符串 chunk。
- `ChatStreamingAsync_ToolCallYieldsNotificationThenText` 断言工具通知和最终文本都会出现在字符串流中。
- `ChatStreamingAsync_NotificationFormatMatchesExpected` 断言字符串里包含 `Echo]`。
- `ChatStreamingAsync_AutoInvokeToolsFalse_YieldsNotificationWithoutContinuingLoop` 断言关闭 auto invoke 时不会执行工具，也不会继续第二轮模型请求。

阶段 2 需要保留这些旧行为，同时新增对事件序列的断言。

### UI / Demo 消费点

当前仓库中没有找到父路线图里提到的 `Src/Core/AgentChatWindow.cs`。实际可见的字符串 streaming 消费点至少包括：

- `Alco/Sandbox/33-LLM/Game.cs`

该 demo 当前直接消费 `ChatStreamingAsync` 并把 chunk 拼接到 `_chatHistory`。本阶段不要求改造该 UI；旧 API 保持兼容后，Sandbox 应继续工作。

### Claude Code 逆向参考

只读逆向 `D:/Zhuang/claude-code-rev-main` 后看到，Claude Code 没有把 agent runtime 简化成一个字符串流。它使用更复杂的 message/update 体系，例如 assistant message、user message、progress message、attachment message、tool result message、tool use summary、compact boundary message 等；工具执行也通过 async generator 逐步 yield 结构化 update。

这些能力服务于 Claude Code 的复杂场景：shell 权限、文件读写、并发工具、hook、subagent、todo、context compact、telemetry、resume transcript 和 streaming fallback 修复。

`Alco.LLM` 当前不复制这套完整系统。阶段 2 只吸收一个核心分层思想：

> session runtime 先输出结构化事件，UI 和字符串输出只是这些事件的一种展示方式。

因此，本阶段只定义最小 6 个事件类型，而不是 Claude Code 级别的大型 message/update union。

## Player/User Flow

本阶段没有新的玩家 UI 流程。

开发者或上层系统可以选择两种消费方式：

1. 继续调用 `ChatStreamingAsync`
   - 行为与当前版本兼容。
   - 仍得到字符串 chunk。
   - Sandbox 33 不需要改动也能继续运行。

2. 调用新的 `ChatEventsAsync`
   - 发送用户消息。
   - 先收到 `RequestStartedEvent`。
   - 模型文本以 `TextDeltaEvent` 输出。
   - 如果模型请求工具，收到 `ToolCallStartedEvent`。
   - 工具成功时收到 `ToolCallCompletedEvent`。
   - 工具失败、超时或参数错误时收到 `ToolCallFailedEvent`。
   - 每次模型请求结束时收到 `RequestCompletedEvent`。
   - 如果工具调用后需要第二轮模型请求，会再次看到新的 request started/completed 事件。

示例事件序列：

```text
RequestStarted(requestIndex: 0)
ToolCallStarted(callId: "call1", toolName: "Add", arguments: { a: 2, b: 3 })
ToolCallCompleted(callId: "call1", toolName: "Add", result: 5, duration: 3ms)
RequestCompleted(requestIndex: 0)
RequestStarted(requestIndex: 1)
TextDelta("结果是 5")
RequestCompleted(requestIndex: 1)
```

## UI Shape

本阶段不新增 UI，也不改 Sandbox 33 的聊天窗口展示。

原因：

- Phase 2 的核心是稳定底层事件 API。
- UI 如何展示工具卡片、折叠参数、显示耗时，应该在事件模型稳定后单独设计。
- 保留 `ChatStreamingAsync` 可以让现有字符串 UI 继续工作。

未来 UI 可以基于事件流做这些展示，但不属于本阶段：

- 模型文本作为普通聊天内容。
- 工具调用作为独立工具卡片。
- 工具成功/失败显示耗时和结果摘要。
- 请求开始/结束用于 loading 状态或 debug timing。

## Config/Data Model

### 新增事件模型

新增文件建议：

- `Alco/Src/Alco.LLM/Session/LLMSessionEvent.cs`

事件模型使用 `abstract record + sealed record 子类型`，而不是一个塞满 nullable 字段的大类。

推荐形状：

```csharp
public abstract record LLMSessionEvent(DateTimeOffset Timestamp);

public sealed record RequestStartedEvent(
    DateTimeOffset Timestamp,
    int RequestIndex
) : LLMSessionEvent(Timestamp);

public sealed record TextDeltaEvent(
    DateTimeOffset Timestamp,
    string Text
) : LLMSessionEvent(Timestamp);

public sealed record ToolCallStartedEvent(
    DateTimeOffset Timestamp,
    string CallId,
    string ToolName,
    IReadOnlyDictionary<string, object?>? Arguments
) : LLMSessionEvent(Timestamp);

public sealed record ToolCallCompletedEvent(
    DateTimeOffset Timestamp,
    string CallId,
    string ToolName,
    object? Result,
    TimeSpan Duration
) : LLMSessionEvent(Timestamp);

public sealed record ToolCallFailedEvent(
    DateTimeOffset Timestamp,
    string CallId,
    string ToolName,
    string Error,
    string ErrorType,
    TimeSpan Duration
) : LLMSessionEvent(Timestamp);

public sealed record RequestCompletedEvent(
    DateTimeOffset Timestamp,
    int RequestIndex
) : LLMSessionEvent(Timestamp);
```

字段可以在实现时按 Microsoft.Extensions.AI 实际类型做小幅调整，但语义必须保持：

- `Timestamp`：事件产生时间。
- `RequestIndex`：本次用户消息触发的模型请求序号，从 0 开始。工具调用后第二轮请求为 1。
- `CallId`：provider/tool call ID；如果 provider 缺失，允许使用空字符串，但应尽量保留原始 call ID。
- `ToolName`：模型请求的工具名；缺失时使用空字符串或 `"<missing>"`，但失败事件必须说明原因。
- `Arguments`：模型给出的工具参数。第一版只需要暴露可读参数，不要求 deep clone 或持久化安全。
- `Result`：工具成功返回值。第一版直接暴露 runtime result，不做持久化截断。
- `Error` / `ErrorType`：工具失败信息，与阶段 1 写回模型的 failure result 保持一致。
- `Duration`：工具调用耗时，不包含后续模型请求耗时。

### 新增 API

在 `LLMSession` 中新增：

```csharp
public async IAsyncEnumerable<LLMSessionEvent> ChatEventsAsync(
    string message,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
```

规则：

- `ChatEventsAsync` 是 streaming agent loop 的新底层 API。
- 它实时 yield 事件，不保存长期 event log。
- 它继续维护 `_chatHistory`，确保 provider 后续请求能看到用户、assistant tool call 和 tool result。
- 它复用阶段 1 的工具调用可靠性逻辑。
- `ChatStreamingAsync` 应改为消费 `ChatEventsAsync` 的兼容 wrapper。

### 保留 API

`ChatStreamingAsync` 保留：

```csharp
public async IAsyncEnumerable<string> ChatStreamingAsync(...)
```

实现约束：

- `ChatEventsAsync` 必须成为 streaming agent loop 的唯一真实实现。
- `ChatStreamingAsync` 不得直接调用 provider，也不得直接调用 `ToolRegistry`。
- `ChatStreamingAsync` 只能消费 `ChatEventsAsync`，并把事件投影为旧字符串 chunk。
- 这意味着两个 public API 可以同时存在，但不能维护两套 agent loop。

兼容规则：

- `TextDeltaEvent` 转成原始文本 chunk。
- `ToolCallStartedEvent` 转成当前兼容格式：工具名通知和参数 JSON。
- `ToolCallCompletedEvent`、`ToolCallFailedEvent`、`RequestStartedEvent`、`RequestCompletedEvent` 默认不转成字符串，除非为了保持现有行为需要。
- 现有 streaming 测试必须继续通过。

`ChatAsync` 本阶段不强制重构。它可以继续走现有非 streaming 路径，避免 Phase 2 同时翻动 streaming 和 non-streaming 两条 loop。未来如果需要统一所有 agent loop，可另写 spec。

## System Rules & Edge Cases

### 事件范围

- 第一版只定义 6 个事件：
  - `RequestStartedEvent`
  - `TextDeltaEvent`
  - `ToolCallStartedEvent`
  - `ToolCallCompletedEvent`
  - `ToolCallFailedEvent`
  - `RequestCompletedEvent`
- 不定义 `ToolCallProgressEvent`、`ReasoningDeltaEvent`、`ContextChangedEvent`、`PermissionRequiredEvent`。
- 不实现 Claude Code 的完整 message/update union。

### Request 事件

- 每次调用 provider streaming API 前 yield `RequestStartedEvent`。
- 每次 provider streaming API 正常结束后 yield `RequestCompletedEvent`。
- 如果 provider streaming API 抛异常，事件流应抛出该异常；是否 yield failed/completed 事件留给后续 error event spec，本阶段不新增 request failed event。
- 工具调用后继续请求模型时，应产生新的 request index。
- 达到 `MaxAutoInvokeIterations` 后的 final request without tools 也应产生 request started/completed 事件。

### Text delta

- 每个非空 `ChatResponseUpdate.Text` 产生一个 `TextDeltaEvent`。
- 空文本不产生 `TextDeltaEvent`。
- 本阶段不解析 reasoning content。

### Tool call started

- streaming 收到 `FunctionCallContent` 后，应产生 `ToolCallStartedEvent`。
- 如果 `AutoInvokeTools = false`，仍然产生 `ToolCallStartedEvent`，但不执行工具、不产生 completed/failed 事件、不发起第二轮模型请求。
- 事件中应包含 call ID、工具名和参数。
- 兼容 wrapper 必须继续把该事件转换成旧字符串通知。

### Tool call completed / failed

- 每个自动执行的 tool call 都应产生且只产生一个 terminal tool event：
  - 成功：`ToolCallCompletedEvent`
  - 失败：`ToolCallFailedEvent`
- terminal tool event 应在工具执行结束后、下一轮模型请求开始前 yield。
- 工具失败事件覆盖：
  - unknown tool
  - 参数反序列化失败
  - 同步 throw
  - async throw
  - timeout
- 外部 cancellation 不应包装成 `ToolCallFailedEvent`；事件流应抛出 `OperationCanceledException`。
- 多个工具调用仍按当前行为串行执行，事件顺序与执行顺序一致。

### 非持久事件流

- `ChatEventsAsync` 不新增 `_eventHistory`。
- `LLMSession` 不负责将事件写文件、导出 JSON 或做敏感信息过滤。
- 测试和调用方可以自行收集事件列表。

### 兼容性

- `ChatStreamingAsync` 的 public signature 不变。
- 现有 string streaming 行为保持测试兼容。
- `ChatAsync` 的 public signature 和行为不变。
- HTTP API 不变。
- ToolRegistry 不新增职责。

## Acceptance Criteria

| Type | What | How to verify |
|---|---|---|
| 单元测试 | 文本-only streaming 通过 `ChatEventsAsync` 产生 `RequestStartedEvent`、一个或多个 `TextDeltaEvent`、`RequestCompletedEvent`。 | 新增 `LLMSessionEventTests` 或扩展 `ToolCallLoopTests`。 |
| 单元测试 | 单个工具调用通过 `ChatEventsAsync` 产生 `ToolCallStartedEvent` 和 `ToolCallCompletedEvent`，且字段包含 tool name、call ID、arguments、result、duration。 | fake tool + fake streaming response。 |
| 单元测试 | 工具失败通过 `ChatEventsAsync` 产生 `ToolCallStartedEvent` 和 `ToolCallFailedEvent`，且包含 error 和 errorType。 | unknown tool 或 throwing fake tool。 |
| 单元测试 | 工具 timeout 产生 `ToolCallFailedEvent`，errorType 为 `TimeoutException`。 | 使用现有 slow fake tool。 |
| 单元测试 | `AutoInvokeTools = false` 时仍产生 `ToolCallStartedEvent`，但不执行工具、不产生 completed/failed、不进行第二轮模型请求。 | fake async tool call count + streaming call count。 |
| 单元测试 | `ChatStreamingAsync` 继续输出现有字符串格式并通过既有测试。 | 运行现有 streaming tests。 |
| 单元测试 | 外部 cancellation 让 `ChatEventsAsync` 抛出 `OperationCanceledException`，不包装成 tool failed event。 | 新增 cancellation test。 |
| 构建 | `Alco.LLM` 编译通过。 | `dotnet build Src/Alco.LLM/Alco.LLM.csproj` |
| 测试 | `Alco.LLM.Test` 全部通过。 | `dotnet test Test/Alco.LLM.Test/Alco.LLM.Test.csproj` |

## Out of Scope

- 不改 Sandbox 33 UI。
- 不实现工具卡片 UI。
- 不新增持久 event log。
- 不新增 session transcript 导出/导入。
- 不实现 `ReasoningDeltaEvent`。
- 不实现 `ToolCallProgressEvent`。
- 不实现 `ContextChangedEvent`。
- 不实现 request failed event。
- 不实现权限/approval event。
- 不实现 tool result pairing validator。
- 不实现并发工具执行。
- 不重构 `ChatAsync`。
- 不改变 HTTP API endpoint。
- 不设计 NPC 行为。

## Related Skills & Docs

- `developer-lite`：本 spec 确认后才能进入代码实现。
- `Docs/Feature/Spec/2026-05-26-alco-llm-agent-foundation-design.md`：父路线图，Phase 2 定义为结构化 session event。
- `Docs/Feature/Spec/2026-05-26-alco-llm-tool-invocation-reliability-design.md`：阶段 1 已完成的工具调用可靠性基础。
- `D:/Zhuang/claude-code-rev-main`：只读逆向参考。该仓库是恢复版源码，本 spec 只吸收其结构化 message/update 分层思想，不复制完整实现。
