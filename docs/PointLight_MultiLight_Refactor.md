# 点光源多光源支持 — 现状分析与改造方案

> 生成时间：2026-08-05
> **实施状态：阶段 2（StructuredBuffer）已完成 — 2026-08-05**
> 对比对象：
> - **Alco 引擎**：`Src/Alco.Rendering/Deferred/PBRDeferredPipeline.cs` + `Src/Alco.Rendering/Deferred/VoxelGiRenderer.cs` + `Src/Alco.Engine/Assets/Shaders/Pipelines/Rendering/PBR/DeferredLighting.hlsl` + `VoxelCommon.hlsli` + `VoxelInject.hlsl`
> - **CRYENGINE 5.7.1**：`Code/CryEngine/CryCommon/CryRenderer/IShader.h` + `Code/CryEngine/Cry3DEngine/3DEngineLight.cpp` + `Code/CryEngine/RenderDll/XRenderD3D9/GraphicsPipeline/TiledShading.cpp` + `Code/CryEngine/RenderDll/XRenderD3D9/GraphicsPipeline/TiledLightVolumes.cpp` + `Shaders/HWScripts/CryFX/TiledShading.cfi`

---

## 目录

- [1. 当前引擎点光源现状](#1-当前引擎点光源现状)
  - [1.1 数据流总览](#11-数据流总览)
  - [1.2 硬编码分布（7 处）](#12-硬编码分布7-处)
  - [1.3 核心问题](#13-核心问题)
- [2. CRYENGINE 5.7.1 点光源架构](#2-cryengine-571-点光源架构)
  - [2.1 数据结构](#21-数据结构)
  - [2.2 光源收集与剔除](#22-光源收集与剔除)
  - [2.3 光源数据传 GPU 的方式](#23-光源数据传-gpu-的方式)
  - [2.4 Tiled Deferred Shading（Compute 主路径）](#24-tiled-deferred-shadingcompute-主路径)
  - [2.5 前向渲染多光源（Forward+）](#25-前向渲染多光源forward)
  - [2.6 光源上限](#26-光源上限)
- [3. CE5 设计可借鉴点](#3-ce5-设计可借鉴点)
- [4. 改造方案](#4-改造方案)
  - [阶段 1：固定数组 + 动态数量（最小改动）](#阶段-1固定数组--动态数量最小改动)
  - [阶段 2：StructuredBuffer 存光源（推荐）](#阶段-2structuredbuffer-存光源推荐)
  - [阶段 3：CPU 光源剔除 + 重要性排序](#阶段-3cpu-光源剔除--重要性排序)
  - [阶段 4：Tiled / Clustered 光照（后期）](#阶段-4tiled--clustered-光照后期)
- [5. 关键陷阱](#5-关键陷阱)
- [6. 总结](#6-总结)

---

## 1. 当前引擎点光源现状

### 1.1 数据流总览

```
Game.cs (手动构造 DeferredLightingData，从未填充 PointLight 字段)
   │
   ▼
PBRDeferredPipeline.RenderLighting(ref data)
   │  _lightingDataBuffer.UpdateBuffer(data)
   │  → 整个 struct 作为单个 Uniform Buffer 一次性上传
   ▼
GPU Uniform Buffer (set 0 的 data cbuffer)
   │
   ├──▶ DeferredLighting.hlsl
   │      for (i = 0; i < 4; i++) {
   │          attenuation = 1/(d² + 1) × EvaluatePBR(...)
   │      }
   │
   └──▶ VoxelGiRenderer → VoxelCommon.hlsli → VoxelInject.hlsl
          for (i = 0; i < 4; i++) {
              attenuation = 1/(d² + 1) × NdotL/PI
          }
```

**关键发现**：4 个光源槽位目前处于"定义了但从未被填充"的状态。Sandbox 中唯一的消费者 `34-PBRDeferred/Game.cs` 只设置了 Sun / Sky / Camera / Shadow / GI 参数，从未调用过 `SetPointLights`，也从未给任何 `PointLight*` 字段赋值。因此运行时 4 个点光源全部 intensity=0，shader 中 `continue` 跳过，等同关闭。

### 1.2 硬编码分布（7 处）

| # | 文件 | 行号 | 硬编码形式 |
|---|------|------|-----------|
| 1 | `PBRDeferredPipeline.cs` | 157–172 | `DeferredLightingData` 中 8 个独立 `Vector4` 字段（`PointLight0Position` … `PointLight3Color`） |
| 2 | `PBRDeferredPipeline.cs` | 192–215 | `SetPointLights` 中 `Math.Min(..., 4)` + 4 个 `if (count > N)` 分支 |
| 3 | `VoxelGiRenderer.cs` | 176–191 | `VoxelGiData` 中同样的 8 个独立字段 |
| 4 | `DeferredLighting.hlsl` | 38–45 | cbuffer 中 8 个 `float4` 声明 |
| 5 | `DeferredLighting.hlsl` | 354–380 | 局部 `float4 pointLightPositions[4]` + `for (i < 4)` |
| 6 | `VoxelCommon.hlsli` | 37–44 | cbuffer 中同样的 8 个 `float4` 声明 |
| 7 | `VoxelInject.hlsl` | 217–235 | `[4]` 数组 + `for (i < 4)` |

全仓搜索 `NUM_POINT_LIGHTS` 等宏**零命中**——引擎没有用宏定义光源数，而是直接写死 `4` 散落在上述多处。

### 1.3 核心问题

**问题 1：没有光源收集 / 剔除系统**

光源完全由应用层手动塞进 `DeferredLightingData` struct。不存在"场景光源列表 → 剔除 → 排序 → 上传"的管线。`SetPointLights` 只被定义，从未被任何代码调用。

**问题 2：光源和帧数据耦合在同一个 Uniform Buffer**

光源数据（8 个 `Vector4` = 128 字节）与相机矩阵、太阳参数、天空参数、阴影级联等挤在同一个 `DeferredLightingData` cbuffer 中。如果扩展到 64 个光源，仅光源就要 64 × 32 = 2048 字节，整个 cbuffer 将显著膨胀。Uniform Buffer 存在大小限制（引擎 `GraphicsBuffer` 的 `EntryReadonly` 注释明确提到 65536 字节上限）。

**问题 3：没有范围 / 衰减半径**

`PointLight` struct 只有 `Position` 和 `ColorAndIntensity`，没有半径/范围字段。衰减在 shader 中硬编码为 `1/(d²+1)`，光源永远有微量贡献，没有截止距离。在多光源场景下，每个像素都要遍历所有光源——即使光源距离极远、贡献微乎其微。

**问题 4：VoxelGI 路径重复定义**

直接光照 Pass 和体素注入 Pass 各自独立维护一份 4 组光源字段（`DeferredLightingData` vs `VoxelGiData`），C# struct 和 HLSL cbuffer 各有一套，共 4 份副本。修改光源数量时必须同步修改所有副本，极易遗漏。

---

## 2. CRYENGINE 5.7.1 点光源架构

### 2.1 数据结构

**文件**：`C:/Projects/CRYENGINE_Source-5.7.1/Code/CryEngine/CryCommon/CryRenderer/IShader.h:2082-2479`

CE5 使用单一 flat struct `SRenderLight` 承载所有光源类型，通过 `m_Flags` 位掩码区分类型：

```cpp
struct SRenderLight
{
    int16   m_Id;
    uint32  m_Flags;          // DLF_POINT, DLF_PROJECT, DLF_AREA, ...
    Vec3    m_Origin;         // 世界空间位置
    float   m_fRadius;        // 有效半径（截止距离）
    ColorF  m_Color;          // RGB 颜色
    float   m_SpecMult;       // 高光乘数
    float   m_fClipRadius;    // 用户设定的裁剪半径上限
    float   m_fAttenuationBulbSize; // 灯泡大小（物理衰减用）
    // ... 聚光灯投影、阴影、环境探针、面光源等字段 ...
};
```

光源类型标志（`IShader.h:1989-2028`）：

```cpp
enum eDynamicLightFlags : uint32 {
    DLF_DIRECTIONAL = BIT32(1),
    DLF_POINT       = BIT32(6),
    DLF_PROJECT     = BIT32(7),   // 聚光灯
    DLF_AREA        = BIT32(14),
    DLF_AMBIENT     = BIT32(21),
    // ...
};
```

**设计要点**：单一结构体 + flag 位掩码，避免 OOP 继承层次，GPU 友好。

### 2.2 光源收集与剔除

**三级剔除流水线**：

```
3DEngine CPU 剔除                    GPU Tile 剔除                   着色
┌────────────────────┐          ┌────────────────────┐          ┌──────────────┐
│ 1. 视锥剔除         │          │ 1. Tile 深度范围    │          │ 遍历 tile 的 │
│    (球 vs 视锥体)   │ ──────▶  │    计算 minZ/maxZ  │ ──────▶  │ 光源位掩码   │
│ 2. 距离剔除         │          │ 2. 球/锥/OBB 与    │          │ 计算 PBR 贡献│
│    (m_fWSMaxViewDist)│         │    Tile 视锥相交   │          │              │
│ 3. 数量截断         │          │ 3. 位排序输出      │          │              │
│    (MAX_LIGHTS_NUM) │          │    光源列表        │          │              │
└────────────────────┘          └────────────────────┘          └──────────────┘
```

**CPU 侧（3DEngineLight.cpp）**：

- `FindPotentialLightSources()` (line 89)：遍历所有 `CLightEntity`，做视锥剔除 `IsSphereVisible_F(Sphere(origin, radius))` + 距离剔除
- `AddDynamicLightSource()` (line 171)：加入光源列表，有数量上限
- `PrepareLightSourcesForRendering()` (line ~352)：最终数量限制，`MAX_LIGHTS_NUM = 32`

**光源分类存储**（`IRenderView.h:51-61`）：

```cpp
enum eDeferredLightType {
    eDLT_DeferredLight        = 0,  // 普通延迟光
    eDLT_DeferredCubemap      = 1,  // 环境探针
    eDLT_DeferredAmbientLight = 2,  // 环境光
    eDLT_DynamicLight         = 3,  // 前向渲染光
};
RenderLightsList m_lights[eDLT_NumLightTypes];
```

**光源排序**（`RenderView.cpp:372-476`）：按屏幕 scissor 矩形面积做二分插入排序，非阴影投射光优先于阴影投射光。

### 2.3 光源数据传 GPU 的方式

**StructuredBuffer（主要方式）**，不是 Constant Buffer。

**文件**：`TiledLightVolumes.h:146-175`、`TiledLightVolumes.cpp:132-142`

CE5 将光源数据分为两套独立结构——**剔除数据**和**着色数据**分离，剔除阶段只需要更紧凑的数据：

```cpp
struct STiledLightCullInfo {       // 80 bytes — 仅用于剔除
    uint32 volumeType;             // 球体/圆锥/OBB/太阳
    uint32 miscFlag;
    Vec2   depthBounds;            // 视空间深度范围 [min, max]
    Vec4   posRad;                 // xyz: 位置, w: 半径
    Vec4   volumeParams0/1/2;      // 体积参数
};

struct STiledLightShadeInfo {      // 192 bytes — 用于最终着色
    uint32   lightType;
    Vec4     posRad;
    Vec2     attenuationParams;    // x: bulbSize, y: falloff
    Vec4     color;                // rgb: 颜色 × intensityScale, a: specMult
    Matrix44 projectorMatrix;      // 聚光灯投影矩阵
    Matrix44 shadowMatrix;
    // ...
};
```

创建方式：

```cpp
m_lightCullInfoBuf.Create(MaxNumTileLights, sizeof(STiledLightCullInfo),
    DXGI_FORMAT_UNKNOWN, USAGE_CPU_WRITE | USAGE_STRUCTURED | BIND_SHADER_RESOURCE);
```

### 2.4 Tiled Deferred Shading（Compute 主路径）

**C++**：`TiledShading.cpp`
**Shader**：`TiledShading.cfi:437-1262`

Compute Shader `TiledDeferredShadingCS` 两个阶段：

**Phase 1 — 光源剔除** (line 533-629)：
- 每个 Tile（8×8 像素）为一个 Thread Group
- `InterlockedMin/Max` 计算 Tile 的 minZ/maxZ
- 对每个光源做球体/圆锥/OBB 与 Tile 视锥的相交测试
- 通过测试的光源 ID 写入 `groupshared sTileLightIndices[]`
- bitonic sort 排序

**Phase 2 — 着色** (line 711-1000+)：
- 重建世界坐标
- 解码 G-Buffer
- 按光源列表遍历，根据类型分别处理（Probe / Ambient / Regular Point）

**Tile 光源映射**——用 uint 位掩码 buffer 存储：

```cpp
// 每 tile 8 个 uint = 256 bit，支持最多 255 个光源
CGpuBuffer m_tileOpaqueLightMaskBuf;   // 不透明物体
CGpuBuffer m_tileTranspLightMaskBuf;   // 透明物体
```

Shader 中用 `firstbitlow` 硬件指令快速遍历位掩码，O(popcount) 而非 O(n)。

### 2.5 前向渲染多光源（Forward+）

**文件**：`TiledShading.cfi:138-378`

```hlsl
void TiledForwardShading(in ForwardShadingAttribs attribs, ...) {
    // 1. 计算像素属于哪个 tile
    uint2 tileIdx = uint2(tcProj.x * numTiles.x, tcProj.y * numTiles.y);
    uint bufferIdx = (tileIdx.y * numTiles.x + tileIdx.x) * 8;

    // 2. 遍历该 tile 的 8 个 uint（256 bit mask）
    for (;;) {
        while (maskIndex < 8) {
            lightIndex = GetNextTileLightIndex(curMask, lightIndex);
            if (lightIndex >= 0) break;
            else curMask = Fwd_TileLightMask[++maskIndex + bufferIdx];
        }

        // 3. 根据光源类型分别处理
        if (lightType == LIGHT_TYPE_REGULAR_POINT) {
            float attenuation = GetPhysicalLightAttenuation(
                length(lightVec), rcp(posRad.w), bulbSize);
            // ...
        }
    }
}
```

**位提取技巧**（`TiledShading.cfi:128-132`）：

```hlsl
int GetNextTileLightIndex(uint lightMask, int startIndex) {
    uint mask = startIndex < 31 ? ~((1 << (startIndex + 1)) - 1) : 0;
    return firstbitlow(lightMask & mask);
}
```

### 2.6 光源上限

| 常量 | 值 | 文件 | 用途 |
|------|---|------|------|
| `MAX_LIGHTS_NUM` | 32 | `IShader.h:2062` | 旧版前向渲染上限 |
| `MaxNumTileLights` | 255 | `TiledLightVolumes.h:10` | Tiled 路径上限 |
| `TILED_SHADING_MAX_NUM_LIGHTS` | 255 | `TiledShading.cfi:18` | Shader 侧上限 |
| `LV_MAX_LIGHTS` | 2048 | `LightVolumeManager.h:9` | 3DEngine 空间哈希上限 |

---

## 3. CE5 设计可借鉴点

| CE5 设计 | 对当前引擎的意义 | 借鉴程度 |
|----------|----------------|---------|
| **StructuredBuffer 传光源**（非 Constant Buffer） | 引擎已有 SSBO 能力（`GraphicsBuffer.EntryReadWrite` + `BindingType.StorageBuffer`），无需新基建；光源数量不受 cbuffer 大小限制 | ★★★ 直接借鉴 |
| **单一 flat struct + flag 类型位掩码** | GPU 友好，避免继承开销；当前引擎可保持简单值类型，加 flag 区分点光/聚光 | ★★☆ 可参考 |
| **带半径的物理衰减** | 当前 `1/(d²+1)` 无截止距离，光源永远有微量贡献；CE5 用 `GetPhysicalLightAttenuation(dist, rcpRadius, bulbSize)` 做物理衰减，有明确截止 | ★★★ 直接借鉴 |
| **剔除/着色数据分离**（CullInfo 80B vs ShadeInfo 192B） | 减少 GPU 剔除阶段带宽 | ★☆☆ 后期优化 |
| **Tile 位掩码**（uint[8] = 256 bit） | 极紧凑的光源-像素映射，`firstbitlow` 硬件指令遍历 | ☆☆☆ 后期（阶段 4） |
| **三级剔除流水线**（CPU 视锥/距离 → GPU Tile） | 分摊 CPU/GPU 负载 | ★☆☆ CPU 剔除可先做 |
| **光源分类存储**（4 个 list） | 不同 pass 只读需要的光源列表 | ★☆☆ 架构参考 |
| **光源按屏幕面积排序** | 大的/近的光源优先，配合数量截断提升质量 | ★☆☆ 后期优化 |

---

## 4. 改造方案

### 阶段 1：固定数组 + 动态数量（最小改动）

> 适用场景：光源数 ≤ 32，快速上线。

将 4 个独立字段改为**固定大小数组 + 实际数量字段**，仍然放在 cbuffer 里。

**改动清单：**

| 文件 | 改动 |
|------|------|
| `PBRDeferredPipeline.cs:104-122` | `PointLight` struct 加 `Range` 字段（打包进 `Position.w`） |
| `PBRDeferredPipeline.cs:157-172` | 8 个独立 `Vector4` → 两个 `Vector4` 数组（`PointLightPositions[MAX]` + `PointLightColors[MAX]`） |
| `PBRDeferredPipeline.cs:173-174` | `Params.y` 从 `pointLightEnabled`(bool) 改为 `numPointLights`(float) |
| `PBRDeferredPipeline.cs:192-215` | `SetPointLights` 改为循环拷贝，上限改为 `MAX_POINT_LIGHTS` |
| `VoxelGiRenderer.cs:176-199` | 同步改 `VoxelGiData` |
| `DeferredLighting.hlsl:38-45` | 8 个 `float4` → `float4 pointLightPositions[MAX]` + `float4 pointLightColors[MAX]` |
| `DeferredLighting.hlsl:46` | `pbrParams.y` 语义改为 `numPointLights` |
| `DeferredLighting.hlsl:354-380` | `for(i<4)` → `for(i < numPointLights)`，加半径衰减 |
| `VoxelCommon.hlsli:37-48` | 同步改 cbuffer 声明 |
| `VoxelInject.hlsl:217-235` | `for(i<4)` → `for(i < numPointLights)`，同步衰减 |

**优点**：改动量最小，不涉及新 buffer 基础设施。

**缺点**：cbuffer 数组存在 HLSL 对齐陷阱（见 [第 5 节](#5-关键陷阱)）；光源数上限受 cbuffer 大小制约。

---

### 阶段 2：StructuredBuffer 存光源（推荐）

> 适用场景：光源数 32~256，架构一步到位。

光源不再挤在 `DeferredLightingData` cbuffer 里，而是放到独立的 **StructuredBuffer**，数量只受 buffer 大小限制。

**目标架构：**

```
DeferredLightingData (cbuffer, set 0)        PointLightBuffer (SSBO)
├─ InvViewProjection                         ├─ PointLight[0]  {pos.xyz, range}
├─ SunViewProjection 0..3                    ├─ PointLight[1]  {color.rgb, intensity}
├─ CameraPosition                            ├─ ...
├─ SunDirection / SunColorAndIntensity       └─ ... up to MAX (256)
├─ SkyParams / SkyHorizonColor / ...
├─ Params (y = numPointLights)               ────────┐
├─ CascadeSplits / CascadeTexelSizes                  │ shader 通过 numPointLights
└─ ...                                                 │ 控制遍历范围
                                            VoxelGiData (cbuffer) 也引用同一个
                                            numPointLights，共享 PointLightBuffer
```

**引擎已有能力确认：**

- `GraphicsBuffer` 类同时支持 `EntryReadonly`（Uniform 绑定）和 `EntryReadWrite`（Storage 绑定）
- `BindingType.StorageBuffer = 2` 已定义
- `BufferUsage.Storage` 已在 buffer 创建 flags 中
- HLSL 端的 binding 系统（`DEFINE_UNIFORM` / `DEFINE_STORAGE` 宏）已支持 storage buffer 声明

**C# 端改动：**

```csharp
// PBRDeferredPipeline.cs — 新增字段
private readonly GraphicsBuffer _pointLightBuffer;
public const int MaxPointLights = 256;

// 构造时
_pointLightBuffer = new GraphicsBuffer(rendering,
    (uint)(MaxPointLights * 2 * sizeof(float) * 4), // pos+color, each float4
    "point_lights");
_pointLightMaterial.SetBuffer(ShaderResourceId.PointLights, _pointLightBuffer);

// 渲染时
public void UpdatePointLights(ReadOnlySpan<PointLight> lights)
{
    int count = Math.Min(lights.Length, MaxPointLights);
    // 打包成 float4[] 上传，或直接用 PointLight struct（需保证布局匹配）
    _pointLightBuffer.UpdateBuffer(lights.Slice(0, count));
    _lightingData.PointLightCount = count;  // 数量放进 cbuffer
}
```

**HLSL 端改动：**

```hlsl
// DeferredLighting.hlsl — StructuredBuffer 声明
struct PointLightData {
    float4 positionRange;   // xyz = pos, w = range
    float4 colorIntensity;  // rgb = color, a = intensity
};
DEFINE_STORAGE(ALCO_GROUP_PASS, 10, PointLightData, pointLights[]);

// cbuffer 中加一个数量字段
float pointLightCount;   // 替代原来的 pbrParams.y bool

// 光照循环
uint count = (uint)pointLightCount;
for (uint i = 0; i < count; i++)
{
    float4 posRange = pointLights[i].positionRange;
    float4 colInt   = pointLights[i].colorIntensity;
    if (colInt.w <= 0.0) continue;

    float3 toLight = posRange.xyz - worldPosition;
    float dist = length(toLight);
    if (dist > posRange.w) continue;          // 超出半径，跳过

    // CE5 风格物理衰减
    float attenuation = saturate(1.0 - (dist / posRange.w));
    attenuation *= attenuation;
    attenuation /= (dist * dist + 1.0);

    float3 L = toLight / dist;
    Lo += EvaluatePBR(N, V, L, albedo, metallic, roughness)
        * colInt.rgb * colInt.w * attenuation;
}
```

**优点：**

- `DeferredLightingData` 和 `VoxelGiData` 不膨胀，光源字段可从这两个 struct 中移除
- 光源数量灵活，16 / 32 / 64 / 256 随意调
- 与 CE5 的架构一致，后续可扩展 tile 剔除
- VoxelGI 路径可共享同一个 `PointLightBuffer`
- 引擎已有 SSBO 能力，无需新基建

---

### 阶段 3：CPU 光源剔除 + 重要性排序

> 适用场景：光源数 > 32，全屏全光源遍历成为瓶颈。

借鉴 CE5 的 CPU 侧剔除，纯 CPU 逻辑，不需要改 GPU 端：

```
场景中所有点光源
   │
   ├─ 视锥剔除：光源半径球 vs 相机视锥体 → 剔除不可见的
   ├─ 距离剔除：超过最大渲染距离的截断
   ├─ 重要性排序：按屏幕投影面积（近的、大的优先）
   ├─ 数量截断：限制为 MAX_VISIBLE_LIGHTS（如 64）
   │
   ▼
写入 PointLightBuffer + 设置 numPointLights
```

**CE5 参考实现**（`3DEngineLight.cpp:89-137`）：

```cpp
// 视锥剔除
bool bIsVisible = camera.IsSphereVisible_F(
    Sphere(light.m_Origin, light.m_fRadius));
```

---

### 阶段 4：Tiled / Clustered 光照（后期）

> 适用场景：光源数 100+，需要 GPU 侧 per-tile 剔除。

借鉴 CE5 的完整 Tiled Shading 方案：

1. **Compute Shader 做 per-tile 光源剔除**——每 tile 计算深度范围，光源球体与 tile 视锥做相交测试
2. **Tile 位掩码**——每 tile 用 8 个 uint（256 bit）表示影响该 tile 的光源列表
3. **Deferred / Forward 着色时遍历位掩码**——`firstbitlow` 硬件指令，O(popcount)

这是大工程，不建议现在做。当场景确实需要 100+ 动态光源时再考虑。

---

## 5. 关键陷阱

### 5.1 HLSL cbuffer 数组对齐

HLSL 中 cbuffer 里的数组元素**按 16 字节（float4）对齐**。如果声明：

```hlsl
// ❌ 危险：cbuffer 中的结构体数组会被自动 padding
cbuffer data {
    struct PointLightData {
        float3 pos;       // 12 bytes → 实际占 16 bytes (padded to float4)
        float3 color;     // 12 bytes → 实际占 16 bytes
        float  intensity; // 4 bytes  → 可能被并入下一个 float4
        float  range;     // 4 bytes
    };
    PointLightData pointLights[32]; // 每个 48-64 bytes，布局不确定
};
```

**正确做法**：用 `float4` 数组，显式控制布局：

```hlsl
// ✅ 安全：float4 数组，16 字节对齐，无 padding 歧义
cbuffer data {
    float4 pointLightPositions[MAX]; // xyz = pos, w = range
    float4 pointLightColors[MAX];    // rgb = color, w = intensity
};
```

对应 C# 端也需要用 `Vector4[]`（而非含 `Vector3` 的 struct 数组），否则 `sizeof` 和 marshal 布局会不匹配。

### 5.2 阶段 1 → 阶段 2 的迁移成本

如果先做阶段 1（cbuffer 数组），后续迁移到阶段 2（StructuredBuffer）时需要：
- 从 `DeferredLightingData` / `VoxelGiData` 中移除光源字段
- 新建 buffer + material binding
- HLSL 声明从 cbuffer 改为 StructuredBuffer
- 上传逻辑从 `UpdateBuffer(struct)` 改为 `UpdateBuffer(span)`

因此，**如果最终目标是阶段 2，建议直接做阶段 2**，跳过阶段 1 的过渡。

### 5.3 C# struct 与 HLSL cbuffer 逐字节对齐

`DeferredLightingData` 的注释明确要求"必须与 `DeferredLighting.hlsl` 的 `data` cbuffer 逐字节对齐"。修改光源字段时必须同步修改 C# struct 和 HLSL cbuffer，否则会导致数据错位。

---

## 6. 总结

| 维度 | 当前引擎 | CE5 做法 | 推荐改造方向 |
|------|---------|---------|-------------|
| 光源数量上限 | 硬编码 4 | StructuredBuffer 支持到 255 | → 256 |
| 光源存储方式 | 跟相机/太阳挤在一个 cbuffer | 独立 StructuredBuffer | → 独立 SSBO |
| 光源数据结构 | 独立命名字段 | flat struct + flag 类型 | → StructuredBuffer\<PointLight\> |
| 衰减模型 | `1/(d²+1)` 无截止 | 带半径的物理衰减 | → 加 Range 字段 + 物理衰减 |
| 光源剔除 | 无 | CPU 视锥 + GPU Tile | → 先做 CPU 视锥剔除 |
| VoxelGI 路径 | 独立重复 4 组字段 | — | → 共享同一个 PointLightBuffer |
| 光源收集系统 | 无（应用层手动填充） | 三级剔除流水线 | → 新增 LightCollector |

**推荐路线：直接做阶段 2（StructuredBuffer）**，理由：

1. 引擎已有 `GraphicsBuffer` + `BindingType.StorageBuffer`，不需要新基建
2. 一步到位，避免阶段 1 → 阶段 2 的迁移成本和 cbuffer 数组对齐陷阱
3. 与 CE5 架构对齐，未来扩展 tile 剔除时不需要推翻重做
4. VoxelGI 路径可直接复用同一个 buffer，消除 4 份重复副本
5. 光源数量上限灵活，不再受 cbuffer 大小制约
