# Alco.LLM 工具调用可靠性 - 设计规格

## 需求
实现 Alco.LLM Agent 基础能力的阶段 1：让单个开发用 Agent 的工具调用链路更可靠。重点包括 await 异步工具返回、让 `AutoInvokeTools` 生效、增加 timeout/cancellation、规范化工具失败结果，并补充单元测试。

## 设计意图
当前 Agent 已经能调用工具，但工具执行链路仍偏原型：反射调用不会处理 `Task` / `Task<T>` 返回值，`AutoInvokeTools` 配置没有实际控制行为，工具调用没有 timeout，错误返回也不够结构化。阶段 1 不让 Agent 更聪明，也不改 UI，而是先把“模型调用工具”这条基础链路做成可预测、可测试、失败可恢复。

核心原则：

- 工具调用失败不能直接破坏 session。
- 模型应收到可理解的工具结果，无论成功还是失败。
- 主线程工具继续走主线程队列。
- 可离开主线程执行的工具继续直接执行。
- 本阶段保持 `IsAsync` 属性名和所有 call site 语义不变。

## 当前状态

### `ToolRegistry`

文件：`Alco/Src/Alco.LLM/Tools/ToolRegistry.cs`

当前行为：

- `InvokeToolAsync(string name, JsonElement jsonArgs)` 根据工具名查找 `ToolDescriptor`。
- `DeserializeArguments` 将 JSON 参数反序列化为方法参数。
- `descriptor.IsAsync == true` 时调用 `InvokeDirect`。
- `descriptor.IsAsync == false` 时调用 `InvokeOnMainThread`，等待 `DrainMainThreadQueue()` 执行。
- `InvokeDirect` 当前直接返回 `descriptor.Method.Invoke(...)` 的结果。

问题：

- 如果工具方法返回 `Task` 或 `Task<T>`，当前结果是 task 对象本身，而不是完成后的真实结果。
- `InvokeOnMainThread` 也复用 `InvokeDirect`，因此主线程异步工具同样不会被 await。
- `TaskCompletionSource` 没有 `RunContinuationsAsynchronously`，后续 continuations 可能在 drain 主线程队列时同步继续执行。
- `ToolDescriptor.IsAsync` 的 XML 注释与当前实际语义不一致。

### `LLMSession`

文件：`Alco/Src/Alco.LLM/Session/LLMSession.cs`

当前行为：

- `ChatAsync` 和 `ChatStreamingAsync` 都会进入最多 128 轮自动工具调用循环。
- `LLMSessionConfig.AutoInvokeTools` 已存在，但没有用于控制是否执行工具。
- `InvokeToolCallsAsync` 捕获工具异常，并用 `FunctionResultContent(fc.CallId, error.Message)` 写入 `_chatHistory`。

问题：

- `AutoInvokeTools = false` 不生效。
- 工具失败结果只有字符串 message，缺少 `success` / `errorType` 等结构。
- 没有工具 timeout。
- 外部 cancellation 和工具 timeout 没有区分。

### 测试

现有测试项目：`Alco/Test/Alco.LLM.Test/Alco.LLM.Test.csproj`

已有测试：

- `ToolCallLoopTests`：普通文本、单工具、多工具、未知工具、工具 throw、最大迭代、streaming tool call。
- `LLMSessionTests`：system prompt、多轮历史。
- `ToolRegistryAdapterTests`：`ToAITools()` 元数据和 schema。

现有测试缺口：

- 没有 `Task` / `Task<T>` 工具。
- 没有主线程队列工具测试。
- 没有 `AutoInvokeTools = false` 测试。
- 没有 timeout/cancellation 测试。
- 没有结构化失败结果断言。

## 用户流程

本阶段没有新的玩家或开发者 UI 流程。

开发者使用现有 Dev Chat：

1. 发送消息。
2. 模型返回文本或工具调用。
3. 如果 `AutoInvokeTools = true`，session 执行工具并把结果发回模型。
4. 如果工具成功，模型获得结构化成功结果。
5. 如果工具失败、未知、超时，模型获得结构化失败结果，并可继续解释或重试。
6. 如果外部取消整次聊天请求，聊天请求终止并向调用方抛 `OperationCanceledException`。

## UI 形态

本阶段不改 UI。

保留：

- `AgentChatWindow` 继续调用 `ChatStreamingAsync` 并拼接字符串 chunk。
- 工具调用通知仍保持当前 streaming 字符串格式，例如 `ToolName]` 和参数 JSON。
- `DebugWindow_LLM` 不新增控件。

可选人工验证仍通过现有 Dev Chat 完成。

## 配置/数据模型

### `LLMSessionConfig`

新增配置：

```csharp
public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(30);
```

规则：

- `ToolTimeout` 用于单次工具调用。
- `ToolTimeout <= TimeSpan.Zero` 表示不启用 timeout。
- `AutoInvokeTools` 控制是否自动执行工具。

### 工具调用失败结果

工具失败写入 `FunctionResultContent` 时使用结构化对象：

```csharp
new
{
    success = false,
    error = "...",
    errorType = "..."
}
```

工具成功可以继续直接返回原始 result。阶段 1 不强制把成功结果包装成 `{ success = true, data = ... }`，避免改变现有模型可见成功数据形态。

### 外部 API

本阶段不改变 HTTP API endpoint 形状。

`ToolRegistry.Http.cs` 仍调用 `registry.InvokeToolAsync(toolName, jsonArgs)`。如果后续新增 overload，本阶段不要求 HTTP API 传入 timeout 或 cancellation 之外的额外选项。

## 系统规则与边界情况

### 异步工具返回

- `ToolRegistry.InvokeToolAsync` 必须正确处理同步返回、`Task` 返回、`Task<T>` 返回。
- `Task` 返回完成后，工具结果为 `null`。
- `Task<T>` 返回完成后，工具结果为 `T`。
- 如果异步工具 fault，异常应从 `InvokeToolAsync` 抛出，由 `LLMSession` 转成 tool failure result。

### 主线程工具

- `IsAsync = false` 的工具继续通过 `_mainThreadQueue` 排队。
- 调用方在 queue drain 前等待。
- `DrainMainThreadQueue()` 执行后，等待中的 `InvokeToolAsync` 完成。
- 主线程工具也必须支持返回 `Task` / `Task<T>`。

### `AutoInvokeTools`

- `AutoInvokeTools = true`：维持当前自动工具调用循环。
- `AutoInvokeTools = false`：
  - 模型返回工具调用时，不执行工具。
  - 不追加 tool result message。
  - `ChatAsync` 返回 assistant message text；如果没有 text，则返回空字符串。
  - `ChatStreamingAsync` 仍 yield 已收到的文本和工具调用通知，但不进行第二次模型请求。

### Timeout

- 单个工具调用使用 `LLMSessionConfig.ToolTimeout`。
- timeout 只作用于 session 自动工具调用，不改变 `ToolRegistry` 的基础 API 语义。
- 工具 timeout 应写入 tool failure result，errorType 建议为 `TimeoutException`。
- timeout 发生后 session 继续下一轮模型请求，让模型看到工具失败结果。
- 对主线程工具，如果 timeout 后主线程队列里的 action 之后才执行，不能再让已完成的 task 抛异常或覆盖结果。

### Cancellation

- 外部 `CancellationToken` 被取消时，整个 `ChatAsync` / `ChatStreamingAsync` 应抛出 `OperationCanceledException`。
- 外部 cancellation 不应被包装成 tool failure result。
- 工具 timeout 和外部 cancellation 必须区分。

### 错误处理

- 未知工具、参数反序列化失败、同步 throw、异步 throw 都应转成 tool failure result。
- tool failure result 至少包含：
  - `success = false`
  - `error`
  - `errorType`
- 如果反射调用抛 `TargetInvocationException`，应优先暴露 inner exception 的类型和 message。

### 注释修正

- 修正 `ToolDescriptor.IsAsync` XML 注释，使其与当前实际语义一致：
  - `true`：async-safe/thread-safe，可在调用线程直接执行。
  - `false`：需要主线程队列。
- 本阶段不改 `IsAsync` 属性名，不批量改 call site。

## 验收标准

| 类型 | 内容 | 验证方式 |
|---|---|---|
| 单元测试 | `ToolRegistry.InvokeToolAsync` 会 await `Task<T>` 工具并返回真实结果。 | 新增 fake async tool 测试。 |
| 单元测试 | `ToolRegistry.InvokeToolAsync` 会 await `Task` 工具并返回 `null`。 | 新增 fake async tool 测试。 |
| 单元测试 | `IsAsync = false` 工具在 `DrainMainThreadQueue()` 前不完成，drain 后完成。 | 新增主线程队列工具测试。 |
| 单元测试 | 主线程工具返回 `Task<T>` 时也会被 await。 | 新增主线程异步工具测试。 |
| 单元测试 | `AutoInvokeTools = false` 时不执行工具、不追加 tool result、不进行工具后的第二次模型请求。 | 新增 `LLMSession` 测试。 |
| 单元测试 | 未知工具、同步 throw、异步 throw 都写入结构化 tool failure result，session 继续请求模型。 | 更新/新增 `ToolCallLoopTests`。 |
| 单元测试 | 工具 timeout 写入结构化 failure result，session 继续。 | 新增 timeout 测试。 |
| 单元测试 | 外部 cancellation 会取消整个聊天请求。 | 新增 cancellation 测试。 |
| 单元测试 | 现有 `ChatAsync` 和 `ChatStreamingAsync` 行为仍通过。 | 运行现有测试。 |
| 构建 | `Alco.LLM` 编译通过。 | `dotnet build Alco/Src/Alco.LLM/Alco.LLM.csproj` |
| 测试 | `Alco.LLM.Test` 全部通过。 | `dotnet test Alco/Test/Alco.LLM.Test/Alco.LLM.Test.csproj` |

## 范围外

- 不实现结构化事件流。
- 不改 `AgentChatWindow`。
- 不新增停止生成按钮。
- 不新增 session 历史导出/导入。
- 不实现 context budget 或上下文压缩。
- 不改 HTTP API endpoint 形状。
- 不实现工具权限/标签。
- 不改 `IsAsync` 属性名。
- 不批量迁移 `[AgentFunction]` call site。
- 不设计 NPC Agent 行为。

## 相关技能与文档

- `developer-lite`：本实现必须先经本 spec 确认，再执行代码修改。
- `Docs/Feature/Spec/2026-05-26-alco-llm-agent-foundation-design.md`：父路线图。
- `Docs/Feature/Spec/2026-05-22-remove-semantic-kernel-dependency-design.md`：现有 LLM 测试策略和 fake client 背景。
- `Docs/Feature/Spec/2026-05-22-isasync-semantic-fix-design.md`：当前 `IsAsync` 语义背景。
- `Alco/CLAUDE.md`：Alco 代码标准；修改 C# 后需要 build/test。
