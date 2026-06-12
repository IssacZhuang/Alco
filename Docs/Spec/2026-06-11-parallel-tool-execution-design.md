# Parallel Tool Execution - Design Spec

## Request

支持 LLM 单次响应中多个 tool call 的并行执行。`IsOnAgentThread = true` 的 tool 用 `Task.Run` 丢到线程池并行执行。

## Design Intent

当前 `LLMSession.ChatEventsAsync` 对 LLM 返回的多个 tool call 严格串行（逐个 `await`）。本设计让 agent-thread tool 通过 `Task.Run` + `Task.WhenAll` 在线程池上真正并行执行。

**并行机制**：`Task.Run` 下沉到 `ToolRegistry.InvokeToolAsync` 的 agent-thread 路径——同步 tool 方法在线程池上执行，`InvokeToolAsync` 立即返回未完成的 task。这同时修复一个现存 bug：当前 agent-thread 路径同步执行（`descriptor.Method.Invoke` 直接调用），`InvokeToolWithTimeoutAsync` 拿到 task 时工具已跑完，超时机制对 agent-thread tool 永远不生效。改为 `Task.Run` 后，超时 `WaitAsync` 才能真正与执行 race。Session 层用 `Task.WhenAll` 等待整个 batch 完成。Tool 作者无需关心并行——框架负责。

**判断依据**：不需要新属性，直接用 `IsOnAgentThread`——`true` 意味着可并发执行（见下文契约强化说明）。

## Current State

### 现有线程模型

```
Agent Thread (background)                    Main Thread (engine tick)
─────────────────────────                    ─────────────────────────
IsOnAgentThread = true                       IsOnAgentThread = false (默认)
→ 直接调用                                    → ConcurrentQueue + TCS
→ 线程安全，无游戏状态访问                      → DrainMainThreadQueue() 执行
```

### 现有 tool 调用流程（串行）

```csharp
// LLMSession.ChatEventsAsync
foreach (var functionCall in functionCalls)
{
    var invocation = await InvokeToolCallAsync(functionCall, cancellationToken);
    results.Add(invocation.ResultContent);
    yield return invocation.Event;
}
```

## Proposed Design

### 核心规则

| IsOnAgentThread | 执行方式 | 原因 |
|-----------------|---------|------|
| `true` | registry 内 `Task.Run` → 线程池并行 | 可并发执行，无游戏状态访问 |
| `false` | 串行 await | 需主线程，引擎单线程 |

**契约强化**：`IsOnAgentThread = true` 的语义从"可在 agent 线程（单一后台线程）执行"强化为"可与其他 tool（包括自身）并发执行"。需同步更新 `AgentFunctionAttribute.IsOnAgentThread` 的 XML 文档注释声明此契约。现有标 `true` 的 tool 已审计：`AgentTool_MapConfig`、`AgentTool_ConfigDatabase`、`AgentTool_EntityPlayground` 中的方法均为静态只读查询，无共享可变状态，满足新契约。

### 分批策略

LLM 返回多个 tool call 后，按顺序分组：

```
[AgentThread, AgentThread] → Batch 1: Task.Run + Task.WhenAll 并行
[MainThread]               → Batch 2: 串行执行
[AgentThread]              → Batch 3: Task.Run + Task.WhenAll 并行
```

规则：
1. **连续 `IsOnAgentThread = true` 的 tool call** 合并到一个 batch，`Task.Run` 丢线程池 + `Task.WhenAll` 等待
2. **任何 `IsOnAgentThread = false` 的 tool call** 独占一个 batch，串行执行
3. Batch 之间严格串行（一个 batch 完成后再执行下一个）

### 并行度上限

```csharp
public class LLMSessionConfig
{
    /// <summary>
    /// Maximum number of tool calls that can execute concurrently within an agent-thread batch.
    /// Values less than or equal to 1 disable parallelism (serial path, debug switch).
    /// </summary>
    public int MaxConcurrentTools { get; set; } = 10;
}
```

`MaxConcurrentTools <= 1`（含 0 和负值）时完全绕过并行路径，走与当前一致的逐个串行逻辑——这是真正的 debug 开关。注意若用 `SemaphoreSlim(1)` 限流而不绕过，执行顺序由信号量竞争决定、不保证原始顺序，且事件要等整批完成才 yield，行为与当前并不一致，因此必须绕过。

### Tool 调用流程（改造后）

#### ToolRegistry.InvokeToolAsync（agent-thread 路径改造）

```csharp
// 改造前：同步执行，返回的 task 已完成，超时永远不生效
if (descriptor.IsOnAgentThread)
{
    return descriptor.Method.Invoke(descriptor.Target, args);
}

// 改造后：Task.Run 丢线程池，立即返回未完成的 task
// 超时 WaitAsync 可与执行 race；session 层天然可并发多个调用
if (descriptor.IsOnAgentThread)
{
    return await Task.Run(() =>
    {
        try
        {
            return descriptor.Method.Invoke(descriptor.Target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    });
}
```

#### LLMSession.ChatEventsAsync（batch 调度）

注意：现有"invoke 前一次性 yield 全部 `ToolCallStartedEvent`"的代码块（含 `AutoInvokeTools = false` 路径之前的那段）移入 batch 循环。`AutoInvokeTools = false` 时仍需先 yield 全部 Started 再 yield RequestCompleted，行为保持不变。

```csharp
// 伪代码
var batches = PartitionByIsOnAgentThread(functionCalls);

foreach (var batch in batches)
{
    // Yield Started events for all calls in batch (original order)
    foreach (var call in batch.Calls)
        yield return new ToolCallStartedEvent(...);

    if (batch.IsAgentThreadBatch && batch.Calls.Count > 1 && _maxConcurrentTools > 1)
    {
        // 并行执行：registry 内部 Task.Run 丢线程池，SemaphoreSlim 限制并发数
        // invocations[] 按索引写入是线程安全的（每个 task 写不同 slot）
        // Task.WhenAll 提供 happens-before 保证，后续读取安全
        using var semaphore = new SemaphoreSlim(_maxConcurrentTools);
        var invocations = new ToolInvocationEventResult[batch.Calls.Count];
        var tasks = batch.Calls.Select(async (call, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                invocations[index] = await InvokeToolCallAsync(call, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks);

        // 按原始顺序收集 results 和 yield events
        for (int i = 0; i < invocations.Length; i++)
        {
            results.Add(invocations[i].ResultContent);
            yield return invocations[i].Event;
        }
    }
    else
    {
        // 串行执行：主线程 tool、单个 agent-thread tool、或 MaxConcurrentTools <= 1
        foreach (var call in batch.Calls)
        {
            var invocation = await InvokeToolCallAsync(call, cancellationToken);
            results.Add(invocation.ResultContent);
            yield return invocation.Event;
        }
    }
}
```

### Event 排序

- `ToolCallStartedEvent`：batch 开始时按原始顺序一次性 yield
- `ToolCallCompletedEvent` / `ToolCallFailedEvent`：并行 batch 按原始顺序 yield（`Task.WhenAll` 全部完成后按 index 遍历）
- 最终 `results` 列表按原始 tool call 顺序排列（用于发回 LLM 的消息）

**对消费者可观察的行为变更**：

1. 事件流从"全部 Started → 逐个 Completed"变为按 batch 交错（后一 batch 的 Started 出现在前一 batch 的 Completed 之后）
2. 并行 batch 内所有 Completed/Failed 事件在整批完成后才 yield——快的 tool 的事件会被批内最慢的 tool 延迟
3. `AutoInvokeTools = false` 路径不变：全部 Started → RequestCompleted

## Data Flow

```
LLM Response
    │
    ▼
[FC1: ListTerrainConfigs (agent)]
[FC2: SearchItemById (agent)]
[FC3: PlaceEntity (main thread)]
[FC4: ListFloorConfigs (agent)]
    │
    ▼ partitionByIsOnAgentThread()
    │
    ├── Batch 1: [FC1, FC2] ──Task.Run──→ Thread1: FC1
    │                        ──Task.Run──→ Thread2: FC2
    │                        ──Task.WhenAll──→ [Result1, Result2]
    │
    ├── Batch 2: [FC3] ──await──→ Main Thread Queue → [Result3]
    │
    └── Batch 3: [FC4] ──Task.Run──→ Thread3 → [Result4]
    │
    ▼ 按原始顺序组装 results
    │
    [Result1, Result2, Result3, Result4] → ChatMessage → 发回 LLM
```

## Config/Data Model

### LLMSessionConfig 变更

新增 `MaxConcurrentTools` 配置（`<= 1` 时退化为串行路径）。

### ToolRegistry 变更

`InvokeToolAsync` 的 agent-thread 路径改为 `Task.Run` 执行（见上文伪代码），使返回的 task 真正 pending，超时机制对 agent-thread tool 生效。Registry 层面线程安全：`_tools` 字典构造后只读，反射 `Invoke` 与 `JsonSerializer.Deserialize` 均线程安全。Tool 方法自身的并发安全由 `IsOnAgentThread` 契约保证。

### AgentFunctionAttribute 变更

无新增属性，但需更新 `IsOnAgentThread` 的 XML 文档注释：从"可在 agent 线程执行"强化为"可与其他 tool 并发执行（线程池），方法必须无共享可变状态"。

### 不需要改的

- `ToolDescriptor` — 无新增字段
- Game 侧 Tool 标注 — 无变化（现有 `IsOnAgentThread = true` 的 tool 均已审计满足并发契约）

## System Rules & Edge Cases

1. **并行 batch 中某个 tool 失败**：`Task.WhenAll` 等待全部完成后，失败的 tool 返回错误结果，其他正常返回。各自独立处理（`InvokeToolCallAsync` 捕获非取消异常转为错误结果，task 不会 fault）
2. **并行 batch 中某个 tool 超时**：`InvokeToolWithTimeoutAsync` 的 `WaitAsync` 超时后返回 `TimeoutException` 错误结果，不影响其他 tool。注意超时是"放弃等待"语义——线程池上的同步执行无法中断，会跑完但结果被丢弃（与主线程 tool 超时语义一致）。依赖本设计的 ToolRegistry `Task.Run` 改造，否则 agent-thread tool 超时永远不生效
3. **外部取消**（`CancellationToken`）：尚未获得信号量的 tool 不再启动；正在等待的 `WaitAsync` 被放弃并抛出 `OperationCanceledException`。正在线程池上执行的同步 tool 方法无法被中断，会跑完但结果被丢弃。取消抛出时 `_chatHistory` 已含 function call 消息但无对应 results 消息（与当前串行行为一致）
4. **单 tool call**：无 batch，走串行路径。唯一行为差异：agent-thread tool 的超时从"永不生效"变为生效（即本设计修复的 bug）
5. **所有 tool 都需要主线程**：全部串行，行为与当前完全一致
6. **MaxConcurrentTools <= 1**（含 0 和负值）：绕过并行路径，走串行逻辑，行为与当前一致，可用作 debug 开关
7. **同一 tool name 出现多次**：每个 tool call 独立解析和执行，天然支持（如 LLM 调用两次 `SearchItemById`）

## Acceptance Criteria

| # | 行为 | 验证方式 |
|---|------|---------|
| 1 | 连续 agent-thread tool 在线程池上并行执行 | Unit test: 2 个 agent-thread tool，验证 Task.Run 并行 |
| 2 | 主线程 tool 独占 batch，串行执行 | Unit test: 主线程 tool 前后的 agent-thread tool 不与其并行 |
| 3 | 并行 batch 中一个 tool 失败不影响其他 | Unit test: batch 中一个 throw，另一个正常返回 |
| 4 | 并行 batch 中一个 tool 超时不影响其他 | Unit test: 超时 tool 返回 TimeoutException 错误，其他正常 |
| 5 | 最终 results 按原始 tool call 顺序排列 | Unit test: 验证 FunctionResultContent 顺序与 LLM 返回顺序一致 |
| 6 | MaxConcurrentTools <= 1 时绕过并行路径，全串行 | Unit test: 行为与当前一致 |
| 7 | 外部 CancellationToken 取消所有 tool | Unit test: 验证 OperationCanceledException |
| 8 | Event 顺序正确：Started 按原始顺序，Completed 按原始顺序 | Unit test: 验证 event 序列 |
| 9 | 串行路径上单个 agent-thread tool 超时也生效（修复现存 bug） | Unit test: 单个慢 tool 超时返回错误 |
| 10 | 现有 29 个测试全部通过 | Unit test: 无回归 |

## Out of Scope

- **Streaming tool execution**（LLM 还在流式输出时就启动 tool）——后续可加
- **Tool 间依赖/数据传递**——一个 tool 的输出作为下一个的输入
- **动态调整并行度**——运行时根据负载自动调节
- **Tool 取消其他并行 tool**——CC 的 sibling error cancellation，暂不需要
- **主线程 tool 并行**——引擎单线程本质，无法真正并行

## Related Skills & Docs

- `/csharp-standards` — 编辑 .cs 文件时遵循的编码标准
- `Alco/Docs/Spec/2026-06-09-alco-llm-agent-loop-refactor-design.md` — 上一次 session loop 重构的设计文档
- `Docs/Reference/claude-code-agent-architecture-analysis.md`（game 仓库）— CC 架构分析
