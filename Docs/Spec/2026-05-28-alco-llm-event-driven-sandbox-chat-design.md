# Alco.LLM Event-Driven Sandbox Chat - 设计规格

## Request

清理 Phase 2 留下的旧字符串工具通知：让 Sandbox 33 的聊天窗口直接消费 `ChatEventsAsync`，把工具调用显示为独立的 Tool 行；同时保留 `ChatStreamingAsync` 作为只输出 assistant 文本的简单 convenience wrapper，不再通过字符串拼接工具调用。

## Design Intent

Phase 2 已经新增 `ChatEventsAsync`，让 `LLMSession` 能输出结构化 session event。但为了兼容旧 UI，`ChatStreamingAsync` 仍会把 `ToolCallStartedEvent` 投影成 `[ToolName]` 和参数 JSON。这解决了兼容问题，但展示语义仍不够干净：工具调用不是 assistant 文本，却仍被塞进 LLM 字符串输出里。

本 cleanup 的目标是把边界收干净：

- `ChatEventsAsync`：完整结构化事件流，推荐给 UI、debug、测试、未来 NPC wrapper 使用。
- `ChatStreamingAsync`：只看 assistant 文本的简化 API，只输出 `TextDeltaEvent`。
- Sandbox 33 UI：改为消费 `ChatEventsAsync`，把工具事件作为独立 Tool/System 行显示。

这延续 Claude Code 逆向得到的方向：runtime 事件应作为结构化消息进入 UI，而不是伪装成 assistant 字符串。

## Current State

### `LLMSession`

文件：`Alco/Src/Alco.LLM/Session/LLMSession.cs`

当前状态：

- `ChatEventsAsync` 是 streaming agent loop 的唯一真实实现。
- `ChatStreamingAsync` 已经是 wrapper，不直接调用 provider 或 `ToolRegistry`。
- 但 `ChatStreamingAsync` 当前仍把 `ToolCallStartedEvent` 转成旧字符串通知。

当前兼容投影：

```text
ToolCallStartedEvent(SetCubeColor, args)
-> "[SetCubeColor]"
-> "{\"cubeName\":\"cube 1\",\"color\":\"#FF0000FF\"}"
```

问题：

- 工具事件仍会混进 assistant text 展示。
- 字符串工具通知是隐式协议，后续 UI 不应依赖它。
- `ChatStreamingAsync` 的定位不够清楚：它看起来像完整 streaming API，但实际上应只是文本投影视图。

### Sandbox 33

文件：`Alco/Sandbox/33-LLM/Game.cs`

当前状态：

- `SendMessage()` 调用 `_llmSession!.ChatStreamingAsync(userMessage)`。
- UI 把所有 chunk 拼进一个 `("LLM", content)` 行。
- 工具调用因此显示在 LLM 文本中。

当前效果类似：

```text
[LLM]:
[SetCubeColor]{"cubeName":"cube 1","color":"#FF0000FF"}Done.
```

目标效果是：

```text
[Tool]:
SetCubeColor
Args: {"cubeName":"cube 1","color":"#FF0000FF"}
Status: completed in 4ms
Result: Cube cube 1 color set to #FF0000FF

[LLM]:
Done.
```

### 测试

文件：`Alco/Test/Alco.LLM.Test/ToolCallLoopTests.cs`

当前状态：

- 事件流测试已覆盖 `ChatEventsAsync`。
- 旧 `ChatStreamingAsync` 测试仍断言工具通知字符串存在。

本 cleanup 需要更新这些测试，使 `ChatStreamingAsync` 被定义为 text-only wrapper。

## Player/User Flow

开发者运行 Sandbox 33：

1. 配置 LLM profile 并连接 agent。
2. 在聊天窗口发送消息。
3. 模型输出普通文本时，UI 追加到 `[LLM]` 行。
4. 模型请求工具时，UI 增加独立 `[Tool]` 行，显示工具名和参数。
5. 工具成功时，UI 更新或追加该工具行的 completed 状态、耗时和结果。
6. 工具失败时，UI 更新或追加 failed 状态、错误类型和错误信息。
7. 后续模型文本继续显示在 `[LLM]` 行。

## UI Shape

本阶段只做最小 UI 改造，不追求工具卡片视觉设计。

### Chat history 数据

当前：

```csharp
List<(string Role, string Content)> _chatHistory
```

可以继续使用，不强制引入复杂 UI model。新增角色字符串即可：

- `User`
- `LLM`
- `Tool`
- `System`

### 渲染规则

- `User`：保持现有蓝色。
- `LLM`：保持现有绿色。
- `Tool`：使用区别于 LLM 的颜色，例如黄色或灰色。
- `System`：错误/连接信息使用已有系统行，或保持当前非 User 都走 LLM 色的逻辑后再最小调整。

### 事件显示格式

`ToolCallStartedEvent`：

```text
Tool: SetCubeColor
Args: {"cubeName":"cube 1","color":"#FF0000FF"}
```

`ToolCallCompletedEvent`：

```text
Tool: SetCubeColor
Args: ...
Status: completed in 4ms
Result: ...
```

`ToolCallFailedEvent`：

```text
Tool: SetCubeColor
Args: ...
Status: failed in 4ms
Error: TimeoutException: ...
```

可以选择更新同一条 Tool 行，也可以追加 completion/failure 行。推荐第一版更新同一条 Tool 行，让一次工具调用保持在一个 UI 条目里。

## Config/Data Model

### `ChatStreamingAsync`

更新语义：

```csharp
/// Streams assistant text only. Use ChatEventsAsync for tool calls and runtime events.
```

规则：

- 只消费 `TextDeltaEvent`。
- 不输出 `ToolCallStartedEvent`。
- 不输出 `ToolCallCompletedEvent`。
- 不输出 `ToolCallFailedEvent`。
- 仍然通过 `ChatEventsAsync` 执行完整 agent loop，所以工具仍会执行，结果仍会写回模型历史。

### Sandbox tool display state

Sandbox 可新增一个小的运行时 map：

```csharp
Dictionary<string, int> _toolMessageIndexByCallId
```

用途：

- `ToolCallStartedEvent` 时记录 call ID 对应的 `_chatHistory` index。
- `ToolCallCompletedEvent` 或 `ToolCallFailedEvent` 时更新同一条 Tool 行。

该 map 只用于当前 UI 展示，不持久化、不导出。

### JSON 序列化

工具参数和结果可以使用 `JsonSerializer.Serialize(...)` 做展示。若结果无法序列化，回退到 `ToString()`。

## System Rules & Edge Cases

### API 边界

- `ChatEventsAsync` 仍是 streaming agent loop 的唯一真实实现。
- `ChatStreamingAsync` 仍不得直接调用 provider 或 `ToolRegistry`。
- `ChatStreamingAsync` 保留 public signature，但语义改为 text-only。
- 新 UI/debug/NPC wrapper 应使用 `ChatEventsAsync`。

### Sandbox 事件消费

- `TextDeltaEvent` 应追加到当前 LLM 行。
- 如果当前没有 LLM 行，应创建新的 LLM 行。
- `ToolCallStartedEvent` 应创建 Tool 行。
- `ToolCallCompletedEvent` 应更新对应 Tool 行；如果找不到 started 行，则追加新的 Tool 行。
- `ToolCallFailedEvent` 同理。
- `RequestStartedEvent` 和 `RequestCompletedEvent` 不需要展示。

### AutoInvokeTools = false

- 如果未来 Sandbox 使用 `AutoInvokeTools = false`，`ToolCallStartedEvent` 仍应显示工具请求。
- 因为工具不会执行，所以不会出现 completed/failed。
- 本阶段 Sandbox 默认仍使用 agent 默认配置，不新增 UI 控件。

### 工具仍然执行

删除 `ChatStreamingAsync` 的工具字符串输出不应影响工具执行。工具执行由 `ChatEventsAsync` 负责，Sandbox 改用 `ChatEventsAsync` 后仍能看到工具 start/completed/failed。

### 错误处理

- `ChatEventsAsync` 抛异常时，Sandbox 继续添加 `System` 错误行。
- 外部 cancellation 本阶段不新增 UI 控件。

## Acceptance Criteria

| Type | What | How to verify |
|---|---|---|
| 单元测试 | `ChatStreamingAsync` text-only：工具调用时不再输出 `[ToolName]` 或参数 JSON，只输出后续 assistant text。 | 更新 `ToolCallLoopTests`。 |
| 单元测试 | `ChatStreamingAsync` 仍通过 `ChatEventsAsync` 执行完整 agent loop，工具结果仍能驱动第二轮模型文本。 | 更新 existing streaming tool test。 |
| 单元测试 | `ChatStreamingAsync` 在 `AutoInvokeTools = false` 时不输出工具字符串，也不执行工具、不发起第二轮请求。 | 更新 existing auto-invoke false test。 |
| 构建 | `Alco.LLM` 编译通过。 | `dotnet build Src/Alco.LLM/Alco.LLM.csproj` |
| 构建 | Sandbox 33 编译通过。 | `dotnet build Sandbox/33-LLM/33-LLM.csproj` |
| 测试 | `Alco.LLM.Test` 全部通过。 | `dotnet test Test/Alco.LLM.Test/Alco.LLM.Test.csproj` |
| 人工验证 | Sandbox 33 中工具调用显示为独立 Tool 行，而不是拼进 LLM 文本。 | 运行 Sandbox 33，发送会触发 `ListCube` 或 `SetCubeColor` 的 prompt。 |

## Out of Scope

- 不删除 `ChatStreamingAsync`。
- 不添加 `[Obsolete]`。
- 不实现复杂工具卡片 UI。
- 不新增折叠 JSON 控件。
- 不新增停止生成按钮。
- 不新增 persistent event log。
- 不新增 transcript export/import。
- 不改变 `ChatAsync`。
- 不改变 HTTP API。
- 不设计 NPC 行为。

## Related Skills & Docs

- `developer-lite`：本 spec 确认后才能执行代码修改。
- `Docs/Feature/Spec/2026-05-27-alco-llm-session-event-design.md`：Phase 2 结构化事件流 spec。
- `Docs/Feature/Spec/2026-05-26-alco-llm-agent-foundation-design.md`：父路线图。
- `D:/Zhuang/claude-code-rev-main`：只读逆向参考；本 cleanup 继续采用“UI 渲染结构化 message/event，而非解析字符串”的方向。
