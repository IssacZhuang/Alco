# CE5 SVOTI 太阳光注入（RSM）研究报告 + Alco 实现方案

> 生成时间：2026-08-20
> 参考对象：CRYENGINE 5.7.1 本地源码 `C:\Projects\CRYENGINE_Source-5.7.1`
> - `Shaders/HWScripts/CryFX/CommonSVO.cfi`（3261 行）
> - `Shaders/HWScripts/CryFX/Total_Illumination.cfx`（2928 行）
> - `Code/CryEngine/RenderDll/XRenderD3D9/D3D_SVO.cpp`（2038 行）
> - `Code/CryEngine/RenderDll/XRenderD3D9/GraphicsPipeline/ShadowMap.cpp`
> - `Code/CryEngine/Cry3DEngine/SVO/SceneTreeCVars.inl`

---

## 0. 结论摘要

1. **"太阳光注入"在 CE5 SVOTI 里指 RSM（Reflective Shadow Map）太阳注入体系**，不是把太阳直射写进体素——后者 Alco 已经有（`VoxelInject.hlsl:216-220`，CSM 单采样 + cone march 兜底）。缺失的是 CE5 用 RSM 在**锥追踪阶段**逐点注入 shadow-map 分辨率的太阳一次反弹（`CommonSVO.cfi:1007-1043`）。
2. **效果**：近场太阳间接光从"体素分辨率 + mip 模糊"提升到"阴影图分辨率"，且带正确的阴影、正确的反弹色（albedo）、正确的方向（法线拒绝）。红色墙面在地面上的彩色反弹、门窗光斑的锐利边缘、树叶/栏杆等低于体素分辨率的几何反弹、屋檐下/室内深处的欠注入修复，全部由此而来。对比文档（`GI_Comparison_Alco_vs_CRYENGINE.md` §7 Phase 4）将其评为 ★★★★★"近场太阳反射品质飞跃"。
3. **成本**：CE5 的做法是给选定的太阳级联（默认 cascade 2）的阴影 pass 挂 2 张 MRT（albedo + world normal，RGBA8），**没有独立几何 pass**；消费端在锥追踪每步最多加 3 次纹理采样。
4. **Alco 适配**：推荐分两步——先用独立 RSM pass（结构最简、和现有 CSM 零耦合，约 1/4 个 shadow pass 的几何开销），再做 `VoxelTrace.hlsl` 的 march 内注入（核心特性）。CE5 的动态体素 RSM 注入（`e_svoTI_SunRSMInject`）默认关闭且标注 EXPERIMENTAL，列为可选第三步。

---

## 1. CE5 的 RSM 太阳注入体系

### 1.1 资源组成

| 资源 | 内容 | 来源 |
|------|------|------|
| `rsmSunShadowMap` | **复用**选定 GSM 级联的深度图（`e_svoTI_GsmCascadeLod`，默认 2） | `D3D_SVO.cpp:1796-1811`、`ShadowMap.cpp:651-663` |
| `rsmSunColorsMap` | 太阳视角的表面 albedo，RGBA8，尺寸 = 该级联 shadow map | `SVO_SUN_RSM_COLOR`，`D3D_SVO.cpp:1928-1938` |
| `rsmSunNormalMap` | 太阳视角的 world normal（`*0.5+0.5` 编码），RGBA8 | `SVO_SUN_RSM_NORMAL`，`D3D_SVO.cpp:1951-1961` |

生成方式（`ShadowMap.cpp:646-663, 757-769`）：阴影阶段发现某级联 `nShadowMapLod == e_svoTI_GsmCascadeLod` 且 GI 注入开启时，把该级联的 pass 从 `ePass_DirectionalLight` 换成 `ePass_DirectionalLightRSM`，同一几何绘制在写深度的同时写两张 color MRT。**没有独立的 RSM pass**，深度共享，几何成本几乎为零（只是 PS 从空变为采样 + 写 2 RT）。

### 1.2 消费点（按重要性排序）

#### ① 锥追踪 march 内逐点注入（核心，`_RT_LIGHT_TEX_PROJ`）

`CommonSVO.cfi:1007-1043`，位于 `ConeTraceBrick` 的行进循环里，**只在 final gather 的像素 pass 启用**（`D3D_SVO.cpp:1591`：`bPixelShader && !GetIntegratioMode() && e_svoTI_InjectionMultiplier`；传播 compute pass 不注入）。每一步：

```hlsl
// CommonSVO.cfi:1007-1043 摘要
float4 vShadTC = mul(SVO_RsmSunShadowProj, float4(r.startpoint.xyz, 1.f));
vShadTC.xy /= vShadTC.w;
if (vShadTC 在 [0,1] 内) {
    float fShadowDepth = tex2DlodPC(rsmSunShadowMap, vShadTC.xy);
    // 深度匹配因子：射线当前深度与 RSM 深度越接近越强
    float fRSM = saturate(1 - (8 / ocNodeSize) * 450 * abs(fShadowDepth - vShadTC.z));
    half4 vMatColor = tex2DlodPC(rsmSunColorsMap, vShadTC.xy);   // albedo
    half3 vMatNormal = tex2DlodPC(rsmSunNormalMap, vShadTC.xy);  // normal
    // MinReflectance：暗面 albedo 钳到下限，防止反弹被吞
    vMatColor.xyz += saturate(SvoParamsMisc2.z - dot(vMatColor.xyz, .333f));
    vRGBD.xyz = (fShadowDepth > vShadTC.z + .0007)          // 深度比较（被挡=不注入）
              * vMatColor.xyz * SVO_RsmSunCol.xyz * SVO_RsmSunCol.w;
    vRGBD.a   = fRSM * vMatColor.a;
    // 法线拒绝：命中面必须朝向接收者（dot < 0.7）
    if (dot(vMatNormal * 2 - 1, r.direction) < 0.7)
        vRgbOut.xyz += vRGBD.xyz * lerp(fRSM, vRGBD.a, vOPAC.a == 0) * saturate(1 - vRgbOut.a);
}
```

**关键机制——深度匹配本身就是遮挡测试**：RSM 的每个 texel 按定义就是太阳射线上第一个表面。当锥射线上的点投影到某 texel、且该点深度与 RSM 深度一致（`fRSM≈1`）时，这个点就在"太阳视角的第一表面"上——它必然被太阳照亮。屋顶下的地板点投影到的 texel 深度是屋顶的深度，深度差大 → `fRSM→0` → 不注入。**所以 RSM 注入的太阳反弹自带正确的阴影图阴影，不需要额外的 shadow test。**

#### ② 动态体素注入（`e_svoTI_SunRSMInject`，EXPERIMENTAL，默认 0）

`Total_Illumination.cfx:2264-2287`，`ComputeDirectDynamicLighting` 里：24m 内、足够细的节点，把体素位置投影进 RSM，深度比较 + RSM normal 做 NdotL，注入 `sunColor * shadow * NdotL`。CVar 描述（`SceneTreeCVars.inl:141`）：

> "Enable additional RSM sun injection. **Helps getting sun bounces in over-occluded areas where primary injection methods are not able to inject enough sun light**"

这就是"遮挡严重区域欠注入修复"的出处。默认关闭，说明 CE5 自己也把它当可选增强。

#### ③ Analytical occluder 重新着色

`Total_Illumination.cfx:697-741`（`ConeTraceTreeAndSkyEx`）：解析遮挡体（手工放的胶囊/OBB）命中点用 RSM 重新着色。Alco 没有 Analytical Occluders 系统，**跳过**。

#### ④ 静态体素 RSM 注入——已被 CE5 自己注释掉

`Total_Illumination.cfx:2088-2129` 是注释掉的实验代码（把每个静态体素投影进 RSM、3×3 gather albedo、命中则注入太阳），现行方案是 `ProcessLights` 向太阳方向 cone trace 体素树做阴影（`:2140`）。**与 Alco 现有的 inject 太阳同思路**——这条路线 Alco 已经等价实现（CSM 单 tap 替代 cone trace，更准）。

### 1.3 开关与能量配比

| CVar | 默认 | 作用 |
|------|------|------|
| `e_svoTI_InjectionMultiplier` | 0 | 体素注入强度（`SvoParamsInject.x`，`D3D_SVO.cpp:1434`），**同时**是 ① 的开启条件（>0 才开 RSM 锥追踪注入） |
| `e_svoTI_RsmUseColors` | 1 (PC) | RSM 生成时渲 albedo/normal 并在锥追踪中使用（关掉则用常数 albedo） |
| `e_svoTI_GsmCascadeLod` | 2 | 用哪个太阳级联做 RSM |
| `e_svoTI_RsmConeMaxLength` | 12m | "RSM 射线"最大长度（米），越短越快 |

能量上 CE5 接受**近场双重计算**：体素里注入过太阳直射（mip 采样后模糊），锥追踪又叠加 RSM 锐利太阳。实际画面由 RSM 主导近场（mip 模糊稀释了体素那份），靠 `SVO_RsmSunCol.w` / InjectionMultiplier 控制总量。

---

## 2. 能带来什么效果

### 2.1 Alco 现状的短板

- 近场太阳反弹的唯一来源是**体素化的直射辐射**：`VoxelInject.hlsl` 在体素中心算 `sunColor * NdotL/π * shadowCSM` 存进 radiance volume，最终 gather 用 mip 三线性采样——分辨率上限是体素尺寸（默认最细 0.1m，mip 后实际有效分辨率更粗），且被传播/模糊进一步摊薄。
- `VoxelTrace.hlsl:199` 的 `GatherScreenSpaceNearField`（G-buffer 重着色的屏幕空间近场反弹）**已定义但 MainCS 未调用**，是死代码。即当前没有任何近场补偿机制。
- CSM 范围外（>级联 3 距离）的体素用 24 步 cone march 自遮挡近似太阳阴影（`VoxelInject.hlsl:65-91`），质量粗。

### 2.2 RSM 注入后的具体画面收益

| 场景 | 现状 | 有 RSM 后 |
|------|------|-----------|
| 红砖墙旁的地面 | 模糊的红色渐变，边缘不清 | **锐利的彩色反弹**，形状跟随墙的受光面（albedo 来自 RSM color map） |
| 门窗射入的阳光反弹 | 光斑被体素化+模糊成一团 | 光斑边缘达到 **shadow map 分辨率**，随太阳角度实时变化 |
| 树叶/栅栏/栏杆（低于体素分辨率） | 体素化丢失或半丢失，反弹缺失 | 只要出现在 RSM 级联里就有反弹 |
| 屋檐下、室内深处 | 阴影处欠注入偏暗（CE5 CVar 描述的原问题） | 通过 RSM 路径获得正确强度的反弹 |
| 动态物体（角色走进阳光） | 动态体素 30Hz 重建 + 传播延迟 | RSM 每帧重渲，反弹即时正确 |
| 间接光方向性 | ALD 由模糊体素场驱动，方向感弱 | 反弹主导方向≈太阳方向，**ALD 指向性强**，物体背阳面间接光自然变暗（立体感） |

另一层价值：RSM 注入发生在 final gather（`VoxelTrace.hlsl`），不经过体素存储——绕开了 clipmap 的 brick 对齐 mip 钳制（`VOXEL_BRICK_ALIGNED_MAX_MIP=3`）和滚动相位问题，近场干净。

### 2.3 边界（它不解决什么）

- 只覆盖选定级联视锥内的范围（CE5 默认 cascade 2，中距离）；级联外的远场仍靠体素注入。这与"近场品质飞跃"的定位一致。
- 只增强**太阳**的反弹；点光反弹需要 per-light RSM（CE5 的 `ForwRsmPoolCol/Nor` + `_RT_POINT_LIGHT`），依赖点光阴影图基础设施，Alco 点光目前无阴影，列为远期。
- 半分辨率 trace + demosaic 会轻微柔化锐利反弹（near 层保留主要细节），不会完全抵消收益。

---

## 3. Alco 实现方案

### 3.0 总览

```
ShadowPass ──► RsmPass(新, ~1/4 shadow 几何) ──► GBuffer ──► VoxelGI(TraceCone 内 RSM 注入) ──► DeferredLighting
                albedo+normal+depth                (复用 cascade[2] 的 VP)
```

分三个阶段，各自可独立验证、独立回滚：

### Phase A — RSM 生成（独立 pass，推荐起步方案）

**新节点 `RGNode_RsmPass`**，插在 `RenderPipelines.CreatePBRDeferred` 的 ShadowPass 之后：

- **资源**：1 张 D32 深度 + 2 张 RGBA8（`_rsmAlbedo`、`_rsmNormal`），同尺寸 framebuffer。默认 1024²（3×4 MiB = 12 MiB）。分辨率独立于 CSM，可调。
- **VP**：直接复用 `PBRSceneEnvironment` 的 `cascadeViewProjections[RsmCascadeIndex]`（默认 2，对齐 CE5 `GsmCascadeLod=2`）。语义 = "以 cascade 2 的正交盒从太阳看出去"。
- **Shader `Rsm.hlsl`**：以 `ShadowDepth.hlsl` 为模板——VS 相同（复用 `data` 的级联 VP 或自带 push constant）；PS 输出 `albedo = baseColor 纹理 RGB`（现有 cutout 变体已有采样 `albedoTexture` 的先例）和 `worldNormal * 0.5 + 0.5`（插值世界法线，normal map 可选，建议第一版用几何法线）。
- **内容源**：`ShadowRenderer` 的注册表（`IShadowRenderable` 列表）。扩展接口加一个可选 `RsmMaterial` 属性，或提供 `ShadowRenderer.CreateRsmMaterial(albedoTexture, ...)` 工厂让场景侧自建（Sandbox 的 `BistroShadowRenderable` 已持有 `ModelMaterial`，拿 base color 纹理零成本）。静态 bundle 机制照搬。

**为什么不直接抄 CE5 的 MRT 方案**：CE5 把 RSM MRT 挂在阴影级联 pass 上（零几何开销），但 Alco 的 `RGNode_ShadowPass` 是 depth-only framebuffer + 每级联 scissor + 静态 bundle 按布局录制——改成 MRT 要动 per-cascade framebuffer/布局/bundle 兼容性。独立 pass 结构最简、可单独开关、分辨率解耦，代价只是一个级联的几何重画（shadow pass 的 1/4）。**MRT 化列为 Phase C 的优化项**，接口不变（消费者只看纹理）。

独立深度的额外好处：`VoxelTrace` 现有的 `_shadowMap` 绑定是 compare-sampler（`DEFINE_TEX2D_DEPTH_SAMPLE`），RSM 注入需要读**原始深度值**做匹配。独立 RSM depth 天然解决（和 color 同 VP 同分辨率，匹配最干净），不碰现有绑定。

### Phase B — 锥追踪注入（核心特性）

**`VoxelTrace.hlsl` 的 `TraceCone` march 循环内**（`VoxelTrace.hlsl:350-388`），在 `sample = SampleRadianceBlended(...)` 之后：

```hlsl
// RSM 太阳注入：仅在接近几何体且近场时投影 RSM。
if (rsmParams.x > 0.0 && sample.a > 0.05 && t < rsmParams.y)
{
    float4 clip = mul(sunViewProjection[rsmCascade], float4(position, 1.0));
    float3 ndc = clip.xyz / clip.w;
    if (ndc 在范围内) {
        float2 uv = 象限映射(与 SampleSunShadowScreen:147-148 相同的 y 翻转+quadrant);
        float rsmDepth  = SAMPLE_TEX2D_LEVEL(_rsmDepth, uv, 0).r;
        float fRSM = saturate(1.0 - abs(rsmDepth - ndc.z) * rsmParams.z);   // 深度匹配 = 遮挡测试
        if (fRSM > 0.0 && rsmDepth > ndc.z - rsmBias) {                      // 背面/略深拒绝
            float3 albedo = SAMPLE_TEX2D_LEVEL(_rsmAlbedo, uv, 0).rgb;
            albedo += saturate(rsmParams.w - dot(albedo, 0.333));            // MinReflectance 钳制
            float3 nrm = SAMPLE_TEX2D_LEVEL(_rsmNormal, uv, 0).xyz * 2.0 - 1.0;
            float sunFacing      = saturate(dot(nrm, L));                    // 背阳面不注入（CE5 march 版没有，动态版有——建议加）
            float facingReceiver = dot(nrm, direction) < 0.7;                // CE5 法线拒绝
            color += (1.0 - alpha) * fRSM * sunFacing * facingReceiver
                   * sunColorAndIntensity.rgb * sunColorAndIntensity.w * albedo
                   * rsmParams.x * nearFade;
        }
    }
}
```

要点：

- **深度容差换算**：CE5 的 `(8/ocNodeSize)*450` 是按节点尺寸缩放的 NDC-z 容差（≈ 每米节点 1/3600 NDC）。Alco 传 `rsmParams.z = worldTolerance / cascadeDepthRange`，`worldTolerance` 取 1.5~2 个 RSM texel 世界尺寸（`cascadeTexelSizes` 已有数据），CPU 算好。
- **不更新 `alpha`**：RSM 注入是发光贡献不是遮挡（与 CE5 一致）。
- **只注入 diffuse 锥**：`TraceCone` 加参数（specular 调用传 0），第一版不动 specular。
- **双计能量**：接受 CE5 模式（体素直射 + RSM 叠加，强度系数 `rsmParams.x` 控总量）。若追求物理正确，后续可对注入乘 `(1 - 近场体素太阳份额)`，留作调优项。
- **时域稳定性**：注入结果随 `temporalDiffuse` 走同一条 raw history（1/64 平均 + EMA 0.015625），静止场景自动收敛；低角度掠射的 `fRSM` 抖动被容差（1.5+ texel）+ 蓝噪声 march jitter + 时域累积压制。
- **ALD 自动受益**：反弹亮度直接进 `diffuseBrightness`，太阳侧反弹让 ALD 指向太阳。

**C# / cbuffer 变更**：

| 位置 | 变更 |
|------|------|
| `VoxelCommon.hlsli:24-44` cbuffer | 末尾加 `float4 rsmParams;`（x=intensity, y=maxDistance, z=depthTolerance(NDC), w=minAlbedo）；级联索引用 `rsmParams` 之外再借 `lightingParams.w`（当前 unused）或打包进 z 的整型位——推荐直接再加一个 int 或复用 w 段，C# 侧 `VoxelGiData`（`RGNode_VoxelGI.cs:182-237`）同步 |
| `VoxelTrace.hlsl` 绑定 | set 0 新增 `_rsmDepth`、`_rsmAlbedo`、`_rsmNormal` 三个采样（当前 11 个绑定 +3，WebGPU 单 set 上限宽裕；按 `Shader_Binding_Slot_Collisions.md` 惯例跑 shader 验证） |
| `RGNode_VoxelGI.cs` | 新属性 `RsmInjectionIntensity`（默认 0=关）、`RsmMaxDistance`（默认 24m，对齐 CE5）、`RsmCascadeIndex`（默认 2）、`RsmMinAlbedo`（默认 0.15）；`Render` 里绑三张 RSM 纹理（参照 `_boundShadowMap` 的稳定绑定模式 `:1313-1318`） |
| `RenderPipelines.cs` | `CreatePBRDeferred` 里 RsmPass 排在 ShadowPass 后、GBuffer 前；产物暴露给 VoxelGI 节点（同 shadowMap 的传递方式） |

### Phase C — 可选增强（按收益排序）

1. **RSM MRT 化**（性能优化）：把 `RGNode_RsmPass` 折叠进 `RGNode_ShadowPass` 选定级联的 MRT（CE5 原生方案），省掉 1/4 shadow 几何；需要 per-cascade framebuffer/bundle 支持。消费端零改动。
2. **动态体素 RSM 注入**（对齐 `e_svoTI_SunRSMInject`）：`VoxelInject.hlsl` 动态池的太阳项改用 RSM depth+normal（比 CSM 单 tap 多了精确的太阳视角法线 NdotL）。CE5 默认关、EXPERIMENTAL——价值中等，优先级低。
3. **点光 RSM**：需要 per-light 阴影图基础设施，远期（对比文档 §3.4 的 "Tiled lights" 同级依赖）。

### 3.1 开销与显存估算

| 项 | 估算 |
|----|------|
| RsmPass 几何 | ≈ shadow pass 的 1/4（一个级联的全部投射者，1024² 视口 + 2×RGBA8 MRT 写） |
| RSM 显存 | 1024²：3×4 MiB = **12 MiB**；2048²：48 MiB |
| TraceCone 注入 | 每个有效步 +3 tap（gate：`sample.a>0.05` 且 `t<24m`；多数步在空气中被 gate 掉）。VoxelTrace 是当前帧率瓶颈（对比文档 §6.4：每像素 200-300 次采样），**预计 +5%~15% VoxelTrace 时间**，`RsmMaxDistance` 和 gate 阈值可调 |
| CPU | 复用级联 VP，零额外计算；新节点 pass 录制开销与一个阴影级联相同 |

### 3.2 风险与缓解

| 风险 | 缓解 |
|------|------|
| 掠射角度下 `fRSM` 深度匹配抖动 → 闪烁 | 容差 ≥1.5 texel；RSM 深度用 bilinear 不可（depth）→ 容差带内线性衰减已是 CE5 方案；时域累积兜底 |
| 与体素直射双重计算 | 默认强度系数保守（CE5 同款取舍）；需要时加体素太阳份额衰减项 |
| 半分辨率 trace 柔化锐利反弹 | demosaic near 层保留主要细节；必要时对 RSM 贡献单独进 near 层（改 `_traceRaw` 段结构，成本高，先不做） |
| 级联视锥外无贡献的接缝 | fRSM 容差带 + 近场距离限制天然淡出；与体素注入叠加使过渡不显著（CE5 亦如此） |
| WebGPU 绑定 slot 冲突 | 新绑定跑 `ValidateAllShaders`；对照 `Shader_Binding_Slot_Collisions.md` 的 slot 分配惯例 |
| 动态物体在 RSM 里但不在体素里（或反之） | RSM pass 与 ShadowRenderer 同注册表，天然同步 |

### 3.3 验证计划

1. `DebugView` 新增 **RSM-only** 视图（如 6：只输出 RSM 注入分量），确认注入位置/强度/阴影正确。
2. 对比场景：
   - 高饱和度墙面（红墙 + 白地面）看彩色反弹边缘；
   - 门/窗半开，看光斑锐利度与太阳角度联动；
   - 树冠/栅栏下看 sub-voxel 几何反弹；
   - 屋檐/室内深处看欠注入修复（CE5 CVar 描述的原始问题）；
   - 动态物体（Sandbox 已有动态注册路径）走进/离开阳光。
3. 基准：`BenchmarkDotNet`/现有 GPU timestamp 槽位给 RsmPass 单独计时；VoxelTrace 前后对比（timestamp 槽 4）。
4. 回归：`ValidateAllShaders` + 180 个渲染测试。

### 3.4 建议参数默认值

| 参数 | 默认 | 说明 |
|------|------|------|
| `RsmInjectionIntensity` | 0（关） | 上线后建议 0.5~1.0 起调 |
| `RsmCascadeIndex` | 2 | 对齐 CE5 `e_svoTI_GsmCascadeLod=2` |
| `RsmResolution` | 1024 | 与 CSM 解耦，可独立调 |
| `RsmMaxDistance` | 24m | 对齐 CE5 动态注入的距离限制与 `RsmConeMaxLength` 量级 |
| `RsmMinAlbedo` | 0.15 | MinReflectance 下限（传播 pass 用 0.2，RSM 注入稍低以保色彩） |
| 深度容差 | 1.5 texel 世界尺寸 | `cascadeTexelSizes[cascade] * 1.5 / depthRange` 换算 NDC |

### 3.5 与 CE5 的差异决策表

| 决策 | CE5 | Alco 方案 | 理由 |
|------|-----|-----------|------|
| RSM 生成 | 阴影级联 MRT（零几何开销） | 先独立 pass，后 MRT 化 | Alco shadow pass 是 depth-only framebuffer + bundle 按布局录制，MRT 改造牵连广；先拿效果 |
| march 注入的 NdotSun | 无（march 版） | **加** | CE5 动态注入版有；防背阳面泄漏，采样免费 |
| 注入位置 | ConeTraceBrick 每步（像素 pass） | TraceCone 每步（`sample.a` gate） | 结构等价；gate 省掉空气步的投影计算 |
| RSM 深度 | 复用 GSM 级联深度 | 独立 RSM depth | 避开 compare-sampler 绑定冲突，VP/分辨率解耦 |
| 动态体素 RSM 注入 | 有（EXPERIMENTAL, 默认关） | 暂缓（Phase C2） | Alco inject 已用 CSM，增益小 |
| 点光 RSM | 有（需 light shadow pool） | 远期 | 依赖 per-light 阴影图基础设施 |
