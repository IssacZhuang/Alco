# Material System

Alco 的材质系统由两层组成：

- **`MaterialComposer`(Alco.Rendering)** — 引擎级 slang 组合基础设施，与具体渲染管线无关。
- **`MaterialCompiler`(Alco.World3D)** — World3D 的材质编译器：surface 契约 + 开放 pass 注册表 + 材质实例管理。

目标：游戏侧不修改引擎就能定义新材质(surface)和新 pass(渲染设施），双方各自独立演进，由 slang 的泛型特化在编译期组合。

## 分层

```
游戏侧 surface（Materials/pbr-standard.slang 或游戏自己的 .slang）
        │  public struct Surface : ISurface { override ... }
        ▼
MaterialCompiler（pass 注册表 + 每 pass 编译/缓存 + 纹理槽/参数块打包）
        │  RegisterPass(MaterialPassDesc)
        ▼
MaterialComposer.ComposeGraphics / ComposeCompute（slang 泛型特化 + 链接 + 反射）
        ▼
渲染设施（GBufferRenderer / ShadowRenderer / RGNode_Forward / RGNode_VoxelGI / 游戏自定义）
```

## Surface 契约(`Libs/alco-world3d-surface.slang`,module `alco_world3d_surface`)

材质是一个实现 `ISurface` 的 struct。`ISurface` 聚合六个细粒度接口，**全部默认实现**——新 surface 可以从空 struct 开始，只重写自己关心的部分：

| 接口 | 方法 | 默认行为 |
| --- | --- | --- |
| `IVertexSurface` | `ModifyVertex(inout worldPos, inout normalWS, uv)` | 恒等（实例变换后、投影前调用，所有 pass 一致） |
| `IAlbedoSurface` | `GetBaseColor(SurfaceInput)` | `baseColorFactor` |
| `INormalSurface` | `GetNormalTS(SurfaceInput)` | `(0,0,1)`（切线空间，模板负责 TBN 提升到世界空间） |
| `IMaterialPropsSurface` | `GetMetallicRoughnessAO(SurfaceInput)` | `metallicRoughnessAO` 因子 |
| `IEmissiveSurface` | `GetEmissive(SurfaceInput)` | `emissiveFactor` |
| `IVoxelFeedSurface` | `GetVoxelBaseColor/GetVoxelEmissive(SurfaceInput, lod)` | 纯因子（compute 域，必须 `SampleLevel` 显式 lod) |

覆盖接口方法**必须写 `override`**（否则 slang 报 error 36107)——surface 作者的意图显式化。pass 不调用的方法在特化后是死代码，零开销。

`SurfaceInput` 由模板填充：`worldPos / normalWS / tangentWS / uv` + 每实例因子（`baseColorFactor / metallicRoughnessAO / emissiveFactor / alphaCutoff`)。需要时间等全局数据时，surface 在自己模块作用域声明 engine 约定的 `_globalRenderData` cbuffer。

surface 声明的资源是它的**自描述数据需求**：模块全局作用域的 set-scoped cbuffer 块（space2，材质集），引擎按成员名绑定，未绑的走兜底。内置示例是 `Materials/pbr-standard.slang` 的 `Surface`(glTF metallic-roughness 四纹理槽 + 因子相乘）。

## Pass 模板约定（`Pipelines/*.slang`)

一个 pass = 一个 slang module，自带**泛型入口点**：

```slang
[shader("vertex")] public MainVOut MainVS<T : ISurface>(MainVIn v) { ... }
[shader("fragment")] public MainPOut MainPS<T : ISurface>(MainVOut v) { ... }
```

组合 = composite + link-time specialization(`specialize(entryPoint, surfaceType)`)，没有字符串拼接的 wrapper shader。内置 pass:

| pass id | 模板 module | 入口 | 消费的 surface 接口 | 引擎资源 |
| --- | --- | --- | --- | --- |
| `gbuffer` | `gbuffer` | MainVS/MainPS | 除体素化外全部 | camera(space0)+ instance(space1) |
| `shadow` | `shadow_depth` | 同上 | IVertexSurface(AlphaTest 时 + IAlbedoSurface) | light VP + instance;`MainPS<T> where let AlphaTest : bool` 值特化 |
| `rsm` | `rsm` | 同上 | IVertex + IAlbedo + INormal | camera(light VP)+ rsmData |
| `glass` | `glass` | 同上 | 同 gbuffer | camera + lightingData（前向玻璃） |
| `voxelize` | `voxelize` | `MainCS<T : IVoxelFeedSurface>` compute | IVoxelFeedSurface | `_data` + `_vertices/_indices/_attrOut/_pageTable` |

新管线可以注册自己的 pass——模板 module 只是磁盘上的一个 .slang 文件。

## MaterialCompiler 开放注册

```csharp
compiler.RegisterPass(new MaterialPassDesc(
    Id: "gbuffer",
    TemplateModule: "gbuffer",
    CreateMaterial: (asset, shader) => rendering.CreateShaderMaterial(shader, layout, buffers),
    ValueSpecArgs: asset => [...],   // 可选；shadow 用它传 AlphaTest
    Accepts: asset => asset.AlphaMode != MeshAlphaMode.Blend)); // 可选
```

- 每个 renderer 在自己的构造函数里注册 pass 并持有 `CreateMaterial` 闭包（布局/缓冲是 renderer 私有知识）。
- `Accepts` 拒绝的材质**不编译**该 pass（如 OPAQUE 材质不编 glass),`TryGet` 返回 null。
- 同 surface module 的不同特化（如 shadow 的 AlphaTest true/false）是独立缓存条目。

## 纹理槽规则

- surface 里的 `Texture2D _albedoTexture` → 槽名 **`albedoTexture`**（去前导下划线，大小写敏感）。
- `BindTextures(asset, { ["albedoTexture"] = tex })` 对持有该 asset 材质的所有 pass 批量绑纹。
- 未绑的槽按名字兜底：`_normal*` → flat normal,`_emissive*` → 黑，其余 → 白。surface 作者用命名换兜底行为。
- surface 资源在 **space2**(`SurfaceResourceSet`),set-scoped cbuffer 块约定与引擎其它部分一致（`cbuffer _material : register(b0, space2)`);pass 模板的引擎资源占用低位集，互不冲突。

## 参数块规则

- surface 声明 `cbuffer _materialParams`(space2)，成员可以是任意标量/向量 float 组合 → `GetParamsLayout` 从反射读每个成员的类型和字节偏移，`PackParamsBuffer` 把 `Dictionary<string, object>` 按 std430 打包成 GPU buffer。
- 传了 `Parameters` 但 surface 没有参数块 → `InvalidDataException`（拼写错误的快速失败）。
- 参数块没声明任何成员时其 binding 会被编译器裁掉，composer 容忍（`GetParamsLayout` 返回 null)。

## 值特化优先于 define

shader 内行为分支用 slang 值特化而不是字符串 define:

```slang
public MainPOut MainPS<T : ISurface>(MainVOut v) where let AlphaTest : bool { ... }
```

C# 侧 `MaterialPassDesc.ValueSpecArgs` 返回 `["true"]` / `["false"]`,composer 以 `let AlphaTest : bool = true` 实例化特化类型，特化参数进程序缓存标识。define 只留给真正需要整个 module 级文本开关的场景（如 `MaterialAsset.Defines`、sprite 的 `REPEATED`)。

## GI 体素化（compute feed)

`RGNode_VoxelGI` 不再是单一手写 shader:`RegisterMesh(mesh, stride, bounds, materialAsset, textures)` 按材质组合 `voxelize` 模板（compute,`IVoxelFeedSurface`),surface 的纹理槽/参数块规则与 graphics pass 完全一致。同材质的多 mesh 共享一个 feed。

## 给游戏侧的清单

新增一个材质效果：

1. 写一个 .slang:`import alco_world3d_surface; public struct Surface : ISurface { override ... }`（文件名 kebab-case,module 名下划线）。
2. 在模块作用域声明需要的纹理/参数块（space2，自描述）。
3. `MaterialAsset { SurfaceShader = myShader }`,pass 自动可用；`BindTextures` 绑纹。

新增一个渲染设施（新管线/新 pass):

1. 写模板 module（泛型入口点）。
2. `RegisterPass` 一个 `MaterialPassDesc`（带上自己的 `CreateMaterial` 布局闭包）。
3. 用 `Get/TryGet` 拿材质画。

两侧都不需要改引擎代码。
