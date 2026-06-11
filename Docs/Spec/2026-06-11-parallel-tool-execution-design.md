# Parallel Tool Execution - Design Spec

## Request

支持 LLM 单次响应中多个 tool call 的并行执行。`IsOnAgentThread = true` 的 tool 用 `Task.Run` 丢到线程池并行执行。

## Design Intent

当前 `LLMSession.ChatEventsAsync` 对 LLM 返回的多个 tool call 严格串行（逐个 `await`）。本设计让 agent-thread tool 通过 `Task.Run` + `Task.WhenAll` 在线程池上真正并行执行。

**并行机制**：`Task.Run` 把同步 tool 方法丢到线程池，多个 tool 跑在不同线程上，`Task.WhenAll` 等待全部完成。Tool 作者无需关心并行——框架负责。

**判断依据**：不需要新属性，直接用 `IsOnAgentThread`——`true` 意味着线程安全，可以并行。

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
| `true` | `Task.Run` → 线程池并行 | 线程安全，无副作用 |
| `false` | 串行 await | 需主线程，引擎单线程 |

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
    /// </summary>
    public int MaxConcurrentTools { get; set; } = 10;
}
```

### Tool 调用流程（改造后）

```csharp
// 伪代码
var batches = PartitionByIsOnAgentThread(functionCalls);

foreach (var batch in batches)
{
    // Yield Started events for all calls in batch (original order)
    foreach (var call in batch.Calls)
        yield return new ToolCallStartedEvent(...);

    if (batch.IsAgentThreadBatch && batch.Calls.Count > 1)
    {
        // 并行执行：Task.Run 丢线程池，SemaphoreSlim 限制并发数
        // invocations[] 按索引写入是线程安全的（每个线程写不同 slot）
        // Task.WhenAll 提供 happens-before 保证，后续读取安全
        using var semaphore = new SemaphoreSlim(_maxConcurrentTools);
        var invocations = new ToolInvocationEventResult[batch.Calls.Count];
        var tasks = batch.Calls.Select((call, index) =>
            Task.Run(async () =>
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
            })
        ).ToArray();
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
        // 串行执行：主线程 tool 或单个 agent-thread tool（无需 Task.Run 开销）
        var invocation = await InvokeToolCallAsync(batch.Calls[0], cancellationToken);
        results.Add(invocation.ResultContent);
        yield return invocation.Event;
    }
}
```

### Event 排序

- `ToolCallStartedEvent`：batch 开始时按原始顺序一次性 yield
- `ToolCallCompletedEvent` / `ToolCallFailedEvent`：并行 batch 按原始顺序 yield（`Task.WhenAll` 全部完成后按 index 遍历）
- 最终 `results` 列表按原始 tool call 顺序排列（用于发回 LLM 的消息）

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

新增 `MaxConcurrentTools` 配置。

### 不需要改的

- `AgentFunctionAttribute` — 无新增属性
- `ToolDescriptor` — 无新增字段
- `ToolRegistry` — 无变化（`InvokeToolAsync` 在 registry 层面是线程安全的：agent-thread 路径无共享可变状态。Tool 方法自身的线程安全由 `IsOnAgentThread` 契约保证）
- Game 侧 Tool 标注 — 无变化

## System Rules & Edge Cases

1. **并行 batch 中某个 tool 失败**：`Task.WhenAll` 等待全部完成后，失败的 tool 返回错误结果，其他正常返回。各自独立处理
2. **并行 batch 中某个 tool 超时**：该 tool 的 `InvokeToolWithTimeoutAsync` 内部 `CancellationToken` 触发超时，不影响其他 tool
3. **外部取消**（`CancellationToken`）：所有正在执行的 `Task.Run` 通过 `CancellationToken` 传播取消
4. **单 tool call**：无 batch，行为与当前完全一致
5. **所有 tool 都需要主线程**：全部串行，行为与当前完全一致
6. **MaxConcurrentTools = 1**：退化为全串行，可用作 debug 开关
7. **同一 tool name 出现多次**：每个 tool call 独立解析和执行，天然支持（如 LLM 调用两次 `SearchItemById`）

## Acceptance Criteria

| # | 行为 | 验证方式 |
|---|------|---------|
| 1 | 连续 agent-thread tool 在线程池上并行执行 | Unit test: 2 个 agent-thread tool，验证 Task.Run 并行 |
| 2 | 主线程 tool 独占 batch，串行执行 | Unit test: 主线程 tool 前后的 agent-thread tool 不与其并行 |
| 3 | 并行 batch 中一个 tool 失败不影响其他 | Unit test: batch 中一个 throw，另一个正常返回 |
| 4 | 并行 batch 中一个 tool 超时不影响其他 | Unit test: 超时 tool 返回错误，其他正常 |
| 5 | 最终 results 按原始 tool call 顺序排列 | Unit test: 验证 FunctionResultContent 顺序与 LLM 返回顺序一致 |
| 6 | MaxConcurrentTools = 1 时全串行 | Unit test: 行为与当前一致 |
| 7 | 外部 CancellationToken 取消所有 tool | Unit test: 验证 OperationCanceledException |
| 8 | Event 顺序正确：Started 按原始顺序，Completed 按原始顺序 | Unit test: 验证 event 序列 |
| 9 | 现有 29 个测试全部通过 | Unit test: 无回归 |

## Out of Scope

- **Streaming tool execution**（LLM 还在流式输出时就启动 tool）——后续可加
- **Tool 间依赖/数据传递**——一个 tool 的输出作为下一个的输入
- **动态调整并行度**——运行时根据负载自动调节
- **Tool 取消其他并行 tool**——CC 的 sibling error cancellation，暂不需要
- **主线程 tool 并行**——引擎单线程本质，无法真正并行

## Related Skills & Docs

- `/csharp-standards` — 编辑 .cs 文件时遵循的编码标准
- `Alco/Docs/Spec/2026-06-09-alco-llm-agent-loop-refactor-design.md` — 上一次 session loop 重构的设计文档
- `Alco/Docs/Reference/claude-code-agent-architecture-analysis.md` — CC 架构分析（如存在）
