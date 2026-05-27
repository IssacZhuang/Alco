# Alco.LLM Agent 基础能力 - 设计规格

## 需求
为一个面向开发用途的单 Agent 制定下一阶段实现路线。当前目标不是直接扩展到 NPC，而是先把现有的 toy chat/tool 原型推进成一个可靠、可观察、可逐步扩展的 Agent 核心。

## 设计意图
当前 `Alco.LLM` 已经具备关键部件：OpenAI 兼容聊天、基于 attribute 的工具发现、自动工具调用、主线程转发、游戏内聊天窗口，以及本地 HTTP API。接下来的工作不应过早跳到 NPC 行为设计，而应先稳定 Agent runtime，让后续能力都建立在清晰的 session、tool、context 和 event 模型上。

补充参考：对同级目录 `claude-code-rev-main` 的只读逆向显示，一个成熟 code agent 的核心并不只是“模型能调函数”，而是围绕工具生命周期建立了一组清晰边界：工具声明、输入/输出 schema、权限/安全判断、并发安全判断、执行、进度事件、结果映射、tool_use/tool_result 配对修复、上下文预算和压缩。该仓库是 source-map 恢复版，不能作为逐字实现依据，但它提供的架构方向对 `Alco.LLM` 有参考价值：我们应当吸收小而稳定的子集，而不是一次性复制完整 code agent。

本设计刻意拆成小而可验证的阶段：

1. 已完成：让工具调用正确、可预测。
2. 用结构化 session event 让 Agent 行为可观察。
3. 把工具从“反射方法”提升为可描述生命周期的 runtime 对象。
4. 让会话可控制，并且有边界。
5. 在模型摘要压缩之前，先加入确定性的 context budget 处理。
6. 在 deterministic budget 后，再加入基于模型的摘要压缩。
7. 在支持恢复/导入/复杂 streaming 后，再加入 tool result pairing 校验。
8. 在核心链路稳定后，再改善配置、权限、HTTP API 和可观察性。

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

### 阶段 1 后的状态

阶段 1 `Alco.LLM 工具调用可靠性` 已实现并提交。当前已具备：

- `ToolRegistry.InvokeToolAsync` 会 await `Task` / `Task<T>` 工具返回。
- 主线程工具和 direct 工具共享同一套 async result unwrap 逻辑。
- `LLMSessionConfig.AutoInvokeTools` 可以真正关闭自动工具执行。
- `LLMSessionConfig.ToolTimeout` 提供单次工具调用 timeout。
- 工具失败会写回结构化 failure result，包含 `success = false`、`error`、`errorType`。
- 外部 cancellation 和工具 timeout 已区分。

### 仍然存在的缺口

- `AgentFunctionAttribute.IsAsync` 容易造成语义误解。当前文档含义是 async-safe/direct execution，但名字读起来像“这个方法是异步方法”，而不是“这个方法可以离开主线程执行”。
- streaming 当前只 yield 原始字符串。工具调用通知被混在 assistant 文本里，导致 UI、日志、回放和调试都不够清晰。
- 工具目前仍主要是反射方法描述，缺少一等公民的工具生命周期元数据，例如只读/会修改状态、是否可并发、是否 destructive、是否需要权限确认。
- 聊天历史没有边界，基本只依赖 provider 拒绝超长 context。大型 tool result 很容易污染上下文。
- 目前没有一等公民的 transcript/event 模型来记录请求耗时、工具参数、工具结果、失败和 reasoning chunk。
- 没有 tool_use/tool_result pairing 校验。当前自动调用路径通常能保持配对，但未来支持 resume、导入历史、复杂 streaming 或手动注入消息后，需要防御缺失 result、重复 result、孤儿 result。
- 配置和 API key 体验能用，但仍是开发原型级别。

## 逆向参考带来的设计调整

从 `claude-code-rev-main` 中抽取的可借鉴模式如下。它们不要求一次性实现，但会影响后续阶段排序：

| 参考模式 | 对 `Alco.LLM` 的启发 | 落地优先级 |
|---|---|---|
| 工具生命周期拆分 | 工具不应长期停留在 `MethodInfo + JsonSchema`。后续应引入更高层工具定义，承载 schema、权限、线程路由、只读/写状态、结果映射。 | 中 |
| 结构化事件流 | 文本、工具开始、工具进度、工具结果、工具失败、请求生命周期应作为 typed event 暴露，UI 只是 consumer。 | 高 |
| 并发安全工具批处理 | 只读且声明为 concurrency-safe 的工具可以并发，写状态或主线程工具串行。当前先保持串行。 | 低 |
| tool result pairing 校验 | 在历史恢复、导入、streaming fallback 或中途取消后，校验 tool call/result 是否成对，必要时修复或拒绝继续。 | 中低 |
| context budget 和 compaction 分层 | 先做确定性 budget，再做模型摘要压缩；压缩失败必须能回退。 | 中 |
| 权限/危险等级 | 即使是 toy agent，也应逐步区分 read-only、state-mutating、destructive、debug 工具。 | 中 |
| 近期活动观测 | 记录请求耗时、工具耗时、参数摘要、结果大小、错误类型，支撑 debug UI。 | 中 |

因此，下一阶段不应优先做 NPC 或完整上下文压缩，而应优先做 `LLMSessionEvent`。这会把后续 UI、日志、context budget、tool metadata 和 pairing 校验都接到一个稳定输出面上。

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
| `LLMSessionEvent` | Session 的结构化输出：文本、reasoning、工具开始、工具进度、工具结果、工具失败、请求生命周期、上下文变化。 |
| `AgentToolDefinition` 或 `LLMToolDefinition` | `ToolDescriptor` 之上的 runtime 工具模型，后续用于承载 schema、权限、只读/写状态、并发安全、结果映射。 |
| `ToolInvocationOptions` | 单次或全局工具调用 timeout、cancellation 和线程路由策略。 |
| `ToolInvocationResult` | 工具调用的规范化成功/错误结果。 |
| `SessionHistorySnapshot` | 适合导出/导入的 session 表示。 |
| `IContextManager` | 根据完整历史、budget 规则和摘要构建本次发送给模型的 prompt window。 |
| `IConversationCompressor` | 可选的模型摘要器，由 context manager 使用。 |
| `ToolResultPairingValidator` | 后续用于检查 assistant tool call 和 tool result 是否成对，避免恢复/导入/中断造成坏历史。 |

### 事件类别

最小可用事件集合：

| 事件 | 含义 |
|---|---|
| `TextDelta` | Assistant 文本片段。 |
| `ReasoningDelta` | 当 provider 暴露 reasoning content 时的 reasoning 片段。 |
| `ToolCallStarted` | 模型请求工具调用，且参数已可用。 |
| `ToolCallProgress` | 工具执行过程中的可选进度。第一版可以不产生，但事件模型应允许扩展。 |
| `ToolCallCompleted` | 工具成功返回。 |
| `ToolCallFailed` | 工具失败、超时、被取消，或参数校验失败。 |
| `RequestStarted` | 一次模型请求开始。 |
| `RequestCompleted` | 一次模型请求结束。 |
| `ContextChanged` | 历史被裁剪、摘要或以其他方式转换。 |

## 系统规则与边界情况

### 阶段 1：工具调用可靠性

阶段 1 已实现，后续只作为回归约束保留：

- 返回 `Task` 或 `Task<T>` 的工具方法必须被 await。
- 工具异常必须被捕获，并转换为模型可见的工具结果，不能让 session 崩掉。
- 工具调用必须支持 cancellation。
- 工具调用应支持 timeout。timeout 值可以先作为 session option。
- `AutoInvokeTools = false` 必须能关闭自动工具执行，并原样返回模型响应。
- 主线程工具调用必须继续通过 `LLMSystem.OnTick` 执行。
- 可离开主线程执行的工具不应阻塞 engine 主线程。

### 阶段 2：结构化 session event

- Session streaming 应暴露结构化事件，而不是只暴露字符串。
- `AgentChatWindow` 应改成这些事件的 consumer。
- 非 streaming 的 `ChatAsync` 和 streaming chat 应共享同一套工具调用逻辑。
- 工具事件必须包含工具名、可用时的 call ID、可用时的参数、耗时，以及结果或错误文本。
- event model 应允许后续加入工具进度、request usage、context budget 变化，而不破坏现有 consumer。
- 现有简单文本聊天行为应继续可用。

### 阶段 3：工具生命周期元数据

- 当前 `IsAsync` 属性可以为了兼容性保留，但文档和 UI 文案必须避免歧义。
- 如果后续重命名，迁移必须保持所有 call site 的行为不变。
- 会修改游戏状态的工具应默认走主线程执行。
- 工具定义应逐步承载 `IsReadOnly`、`IsStateMutating`、`IsDestructive`、`IsConcurrencySafe` 等元数据。
- 第一版不实现复杂权限系统，但 metadata 形状应能支撑 debug UI 展示和后续权限确认。
- 工具成功结果和失败结果应有统一映射点，避免每个 provider/session 分支各自拼装。

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

### 阶段 7：tool result pairing 校验

- 支持恢复、导入、复杂 streaming fallback 或中途取消之前，应加入 pairing 校验。
- assistant message 中的每个 tool call 都应有对应 tool result。
- tool result 不应引用不存在的 tool call。
- 重复 tool result 应被识别并拒绝或去重。
- 自动修复只能用于开发态恢复；如果未来用于训练/回放，应允许 strict mode 直接失败。

### 阶段 8：权限和可观察性

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
| 规划 | 第一阶段已完成，下一阶段可以独立启动。 | 阶段 2 聚焦结构化 session event，不要求同时实现 context 压缩、权限系统或 NPC 行为。 |

## 实现顺序

### 1. 工具调用可靠性（已完成）

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
- 新增 `Alco/Src/Alco.LLM/Session/LLMSessionEvent.cs`
- `Src/Core/AgentChatWindow.cs`

交付内容：

- 增加 event model。
- 增加 yield 事件的 streaming 方法。
- 改造聊天窗口以渲染 event stream。
- 如有必要，保留简单文本聊天 API 作为 convenience wrapper。

### 3. 工具生命周期元数据

主要文件：

- `Alco/Src/Alco.LLM/Tools/ToolDescriptor.cs`
- `Alco/Src/Alco.LLM/Tools/ToolRegistry.cs`
- 新增 `Alco/Src/Alco.LLM/Tools` 下的高层工具定义文件。

交付内容：

- 在不破坏 `[AgentFunction]` 现有用法的前提下，引入可扩展的工具元数据。
- 明确 direct/main-thread 线程路由语义。
- 预留 read-only、state-mutating、destructive、concurrency-safe 分类。
- 让 debug UI 和后续权限系统可以读取这些元数据。

### 4. 会话控制和 transcript 模型

主要文件：

- `Alco/Src/Alco.LLM/Session`
- `Src/Core/Engine.LLM.cs`
- `Src/Core/Debugging/DebugWindow_LLM.cs`

交付内容：

- Clear/new session 行为。
- 只读历史检查。
- 可选 JSON 导出/导入。
- Debug UI 中检查工具列表。

### 5. 轻量 context budget

主要文件：

- `Alco/Src/Alco.LLM/Session`
- 新增 `Alco/Src/Alco.LLM/Context` 下的 context 管理文件。

交付内容：

- 确定性消息裁剪。
- Tool result 长度限制。
- Context budget options。
- 用 `ContextChanged` 事件提供可见性。

### 6. 基于模型的上下文压缩

主要文件：

- `Alco/Src/Alco.LLM/Context`

交付内容：

- `IConversationCompressor`。
- 插入 summary message。
- 失败时回退到确定性裁剪。
- 使用 fake chat client/compressor 的测试。

### 7. Tool result pairing 校验

主要文件：

- `Alco/Src/Alco.LLM/Session`
- `Alco/Src/Alco.LLM/Context`
- `Alco/Test/Alco.LLM.Test`

交付内容：

- 检查 tool call 和 tool result 是否成对。
- 对缺失、重复、孤儿 result 提供明确错误或开发态修复策略。
- 为历史导入、resume 和复杂 streaming fallback 做防御。

### 8. 配置、HTTP API 和可观察性打磨

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
- `D:/Zhuang/claude-code-rev-main`：只读逆向参考。该仓库是恢复版 Claude Code source tree，仅用于架构启发，不作为直接实现来源。
