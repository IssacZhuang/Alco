# Alco.LLM Agent 基础能力 - 设计规格

## 需求
为一个面向开发用途的单 Agent 制定下一阶段实现路线。当前目标不是直接扩展到 NPC，而是先把现有的 toy chat/tool 原型推进成一个可靠、可观察、可逐步扩展的 Agent 核心。

## 设计意图
当前 `Alco.LLM` 已经具备关键部件：OpenAI 兼容聊天、基于 attribute 的工具发现、自动工具调用、主线程转发、游戏内聊天窗口，以及本地 HTTP API。接下来的工作不应过早跳到 NPC 行为设计，而应先稳定 Agent runtime，让后续能力都建立在清晰的 session、tool、context 和 event 模型上。

本设计刻意拆成小而可验证的阶段：

1. 让工具调用正确、可预测。
2. 用结构化事件让 Agent 行为可观察。
3. 让会话可控制，并且有边界。
4. 在模型摘要压缩之前，先加入轻量 context budget 处理。
5. 在核心链路稳定后，再改善配置、工具元数据、HTTP API 和测试覆盖。

## 当前状态

### Alco.LLM 模块

| 区域 | 文件 | 当前职责 |
|---|---|---|
| Agent 工厂 | `Alco/Src/Alco.LLM/Agent/LLMAgent.cs` | 创建 OpenAI 兼容的 `IChatClient`，发现工具，并创建 `LLMSession`。 |
| Session 循环 | `Alco/Src/Alco.LLM/Session/LLMSession.cs` | 保存聊天历史，发送聊天请求，并自动调用工具。 |
| 工具注册表 | `Alco/Src/Alco.LLM/Tools/ToolRegistry.cs` | 发现 `[AgentFunction]` 方法，反序列化 JSON 参数，并执行工具。 |
| 主线程桥接 | `Alco/Src/Alco.LLM/Agent/LLMSystem.cs` | 在 engine tick 中消费 `ToolRegistry` 的主线程回调队列。 |
| HTTP API | `Alco/Src/Alco.LLM/Server/GameApiServer.cs`, `ToolRegistry.Http.cs` | 向本地 HTTP 调用方暴露工具元数据和工具调用入口。 |
| 游戏集成 | `Src/Core/Engine.LLM.cs` | 扫描 `[AgentTools]`，创建当前 agent/session，管理聊天窗口和 API server。 |
| Debug UI | `Src/Core/AgentChatWindow.cs`, `Src/Core/Debugging/DebugWindow_LLM.cs` | 展示聊天，允许开发者刷新 profile 并打开聊天窗口。 |

### 已知缺口

- 返回 `Task` 或 `Task<T>` 的工具方法会被反射调用，但 registry 当前不会 await 后再把结果返回给模型。
- `AgentFunctionAttribute.IsAsync` 容易造成语义误解。当前文档含义是 async-safe/direct execution，但名字读起来像“这个方法是异步方法”，而不是“这个方法可以离开主线程执行”。
- `LLMSessionConfig.AutoInvokeTools` 已存在，但目前没有真正用于关闭自动工具调用。
- 工具调用没有明确的 timeout 策略。
- streaming 当前只 yield 原始字符串。工具调用通知被混在 assistant 文本里，导致 UI、日志、回放和调试都不够清晰。
- 聊天历史没有边界，基本只依赖 provider 拒绝超长 context。大型 tool result 很容易污染上下文。
- 目前没有一等公民的 transcript/event 模型来记录请求耗时、工具参数、工具结果、失败和 reasoning chunk。
- 配置和 API key 体验能用，但仍是开发原型级别。

## 用户流程

这是开发者面对的功能。

1. 开发者在现有 LLM debug/preference UI 中配置 LLM profile。
2. 开发者刷新 agent。
3. Agent 扫描已注册的游戏工具并创建 session。
4. 开发者打开聊天窗口并发送消息。
5. Session 流式输出模型文本和工具事件。
6. 如果模型请求工具，runtime 校验并执行工具。工具可能直接执行，也可能通过 engine 主线程队列执行。
7. 聊天窗口以可读方式展示文本、工具进度、工具结果和错误。
8. 开发者可以开始新 session、检查可用工具，并在不重启游戏的情况下观察失败原因。

## UI 形态

第一批实现阶段应尽量少改 UI。

### 复用现有 UI

- `DebugWindow_LLM`：profile 控件、刷新按钮、API server 状态、聊天窗口控制。
- `AgentChatWindow`：聊天历史和输入框。
- `ImGUILogger`：当前 engine log sink。

### 分阶段预期 UI 增量

| 阶段 | UI 影响 |
|---|---|
| 工具可靠性 | 最小。错误信息应在聊天或日志中更清楚。 |
| 结构化事件流 | 聊天窗口应分别渲染文本 delta 和工具事件。 |
| 会话控制 | 增加新 session、清空历史、检查工具列表等控件。 |
| Context budget | 可选 debug 信息：历史大小、估算 context 大小、裁剪/压缩事件。 |
| 可观察性 | 增加近期请求、工具调用、耗时和错误的 debug 面板或表格。 |

## 配置/数据模型

### 保留现有配置

`Preference.LLM` 继续负责开发 Agent 的 profile 状态：

- Provider
- Endpoint/custom URI
- API key
- Model ID
- System prompt
- API port

### 建议的 runtime 数据类型

这些是实现目标，不要求第一批代码一次性全部加入。

| 类型 | 用途 |
|---|---|
| `AgentEvent` 或 `LLMEvent` | Session 的结构化输出：文本、reasoning、工具开始、工具结果、工具失败、请求生命周期。 |
| `ToolInvocationOptions` | 单次或全局工具调用 timeout、cancellation 和线程路由策略。 |
| `ToolInvocationResult` | 工具调用的规范化成功/错误结果。 |
| `SessionHistorySnapshot` | 适合导出/导入的 session 表示。 |
| `IContextManager` | 根据完整历史、budget 规则和摘要构建本次发送给模型的 prompt window。 |
| `IConversationCompressor` | 可选的模型摘要器，由 context manager 使用。 |

### 事件类别

最小可用事件集合：

| 事件 | 含义 |
|---|---|
| `TextDelta` | Assistant 文本片段。 |
| `ReasoningDelta` | 当 provider 暴露 reasoning content 时的 reasoning 片段。 |
| `ToolCallStarted` | 模型请求工具调用，且参数已可用。 |
| `ToolCallCompleted` | 工具成功返回。 |
| `ToolCallFailed` | 工具失败、超时、被取消，或参数校验失败。 |
| `RequestStarted` | 一次模型请求开始。 |
| `RequestCompleted` | 一次模型请求结束。 |
| `ContextChanged` | 历史被裁剪、摘要或以其他方式转换。 |

## 系统规则与边界情况

### 阶段 1：工具调用可靠性

- 返回 `Task` 或 `Task<T>` 的工具方法必须被 await。
- 工具异常必须被捕获，并转换为模型可见的工具结果，不能让 session 崩掉。
- 工具调用必须支持 cancellation。
- 工具调用应支持 timeout。timeout 值可以先作为 session option。
- `AutoInvokeTools = false` 必须能关闭自动工具执行，并原样返回模型响应。
- 主线程工具调用必须继续通过 `LLMSystem.OnTick` 执行。
- 可离开主线程执行的工具不应阻塞 engine 主线程。

### 阶段 2：更清晰的线程路由语义

- 当前 `IsAsync` 属性可以为了兼容性保留，但文档和 UI 文案必须避免歧义。
- 如果后续重命名，迁移必须保持所有 call site 的行为不变。
- 会修改游戏状态的工具应默认走主线程执行。

### 阶段 3：结构化事件流

- Session streaming 应暴露结构化事件，而不是只暴露字符串。
- `AgentChatWindow` 应改成这些事件的 consumer。
- 非 streaming 的 `ChatAsync` 和 streaming chat 应共享同一套工具调用逻辑。
- 工具事件必须包含工具名、可用时的 call ID、可用时的参数、耗时，以及结果或错误文本。
- 现有简单文本聊天行为应继续可用。

### 阶段 4：会话控制和历史

- Session 必须支持 clear/new session。
- Session 应提供只读历史，供 debug UI 查看。
- System prompt 必须保持在首位，或由 context manager 明确保护。
- 导出/导入可以使用普通 JSON，不应依赖 provider SDK 的具体类型。
- API key 等敏感数据不得包含在导出的 session 中。

### 阶段 5：轻量 context budget

- 在模型摘要压缩之前，先加入确定性的 budget 处理：
  - 保留 system prompt。
  - 保留最近的 user/assistant 轮次。
  - 优先丢弃或截断旧 tool result，而不是丢掉用户意图。
  - 限制单个 tool result 的长度。
  - 发生裁剪时记录 `ContextChanged` 事件。
- 如果暂时没有准确 tokenizer，可以先使用近似字符 budget。

### 阶段 6：基于模型的上下文压缩

- 只有在确定性 budget 处理存在后，才加入 `IConversationCompressor`。
- 压缩应把旧轮次摘要为持久 summary message。
- 最新对话窗口应保留原文。
- Tool result 摘要应优先保留事实和状态变化，而不是原始 JSON。
- 压缩失败时应回退到确定性裁剪。

### 阶段 7：工具元数据、权限和可观察性

- 工具元数据最终应区分只读工具、会修改状态的工具、debug 工具。
- 即使暂不考虑 NPC，debug UI 也应显示工具是否可能修改游戏状态。
- 近期 Agent 活动应可检查：请求耗时、工具耗时、参数、结果大小和错误。

## 验收标准

本文档定义阶段性路线。每个阶段在实现前，如果细节仍有歧义，应再写独立的实现 spec。

| 类型 | 内容 | 验证方式 |
|---|---|---|
| 文档 | 单 Agent 路线图已写入 `Docs/Feature/Spec`。 | 审阅本文档。 |
| 文档 | 第一阶段明确不包含 NPC 行为。 | 审阅 `范围外`。 |
| 文档 | 路线图列出了具体的第一批实现阶段。 | 审阅 `实现顺序`。 |
| 文档 | 当前已知缺口和相关代码归属已记录。 | 审阅 `当前状态` 和 `已知缺口`。 |
| 规划 | 第一阶段可以独立启动。 | 阶段 1 只涉及工具调用可靠性和聚焦测试。 |

## 实现顺序

### 1. 工具调用可靠性

主要文件：

- `Alco/Src/Alco.LLM/Tools/ToolRegistry.cs`
- `Alco/Src/Alco.LLM/Session/LLMSession.cs`
- `Alco/Test/Alco.LLM.Test`

交付内容：

- Await `Task` 和 `Task<T>` 工具返回值。
- 增加工具 cancellation 和 timeout 处理。
- 让 `AutoInvokeTools` 生效。
- 规范化工具失败结果。
- 增加测试：direct 工具、主线程工具、异步返回工具、失败、timeout/cancellation、关闭 auto-invoke。

### 2. 结构化 session event

主要文件：

- `Alco/Src/Alco.LLM/Session/LLMSession.cs`
- `Src/Core/AgentChatWindow.cs`

交付内容：

- 增加 event model。
- 增加 yield 事件的 streaming 方法。
- 改造聊天窗口以渲染 event stream。
- 如有必要，保留简单文本聊天 API 作为 convenience wrapper。

### 3. 会话控制和 transcript 模型

主要文件：

- `Alco/Src/Alco.LLM/Session`
- `Src/Core/Engine.LLM.cs`
- `Src/Core/Debugging/DebugWindow_LLM.cs`

交付内容：

- Clear/new session 行为。
- 只读历史检查。
- 可选 JSON 导出/导入。
- Debug UI 中检查工具列表。

### 4. 轻量 context budget

主要文件：

- `Alco/Src/Alco.LLM/Session`
- 新增 `Alco/Src/Alco.LLM/Context` 下的 context 管理文件。

交付内容：

- 确定性消息裁剪。
- Tool result 长度限制。
- Context budget options。
- 用 `ContextChanged` 事件提供可见性。

### 5. 基于模型的上下文压缩

主要文件：

- `Alco/Src/Alco.LLM/Context`

交付内容：

- `IConversationCompressor`。
- 插入 summary message。
- 失败时回退到确定性裁剪。
- 使用 fake chat client/compressor 的测试。

### 6. 配置、HTTP API 和可观察性打磨

主要文件：

- `Src/Core/Preference.cs`
- `Src/Core/Debugging/DebugWindow_LLM.cs`
- `Alco/Src/Alco.LLM/Server/GameApiServer.cs`
- `Alco/Src/Alco.LLM/Tools/ToolRegistry.Http.cs`

交付内容：

- 更好的 profile 校验和 API key masked display。
- `/health`、`/tools`、`/invoke` 行为有文档和测试。
- 近期活动/debug log 面板。
- 工具元数据支持只读/修改状态/debug 分类。

## 范围外

- NPC Agent 行为。
- NPC 长期记忆。
- Embedding 或向量数据库。
- Multi-agent 编排。
- 自主后台 Agent 循环。
- 超出单个开发 Agent 所需的 provider 特定高级功能。
- 完整工具安全沙箱。
- Prompt template 系统。

## 相关技能与文档

- `developer-lite`：将本文档作为父路线图；每个阶段在需要时再写聚焦的实现 spec。
- `Alco/CLAUDE.md`：Alco 编码标准、build/test 期望和文档要求。
- `Docs/Feature/Spec/2026-05-22-remove-semantic-kernel-dependency-design.md`：之前的 LLM 迁移和测试策略。
- `Docs/Feature/Spec/2026-05-22-isasync-semantic-fix-design.md`：之前的线程路由语义修正。
