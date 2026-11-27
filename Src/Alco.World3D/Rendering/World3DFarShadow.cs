using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const float FarHeightCaptureZMax = 2048f;
    private const float FarHeightCaptureZMin = -512f;
    private const float FarHeightCaptureZSpan = FarHeightCaptureZMax - FarHeightCaptureZMin;

    private readonly record struct FarHeightPendingUpload(
        int X,
        int Y,
        int Width,
        int Height,
        float[] Data,
        bool FullReplace,
        float WorldCoverageMeters,
        World3DCell? OriginAnchorCell);

    private Texture? _farHeightCaptureDepth;
    private Texture? _farHeightExplicitBase;
    private Texture? _farHeightPyramid;
    private Shader? _farHeightBuildShader;

    private readonly List<FarHeightPendingUpload> _farHeightPendingUploads = [];
    private readonly List<(FarHeightPendingUpload Upload, BufferSlice Slice)> _recordedFarHeightUploads = [];

    private bool _hasExplicitFarHeight;
    private int _explicitFarHeightWidth;
    private int _explicitFarHeightHeight;
    private bool _farHeightCapturedValid;
    private ulong _farHeightCapturedSceneRevision;
    private World3DCell _farHeightCapturedAnchorCell;
    private int _farHeightCapturedResolution;
    private float _farHeightCapturedCoverage;
    private bool _farHeightExplicitDirty;

    private bool _recordedFarHeightActive;
    private bool _recordedFarHeightAutoCapture;
    private bool _recordedFarHeightNeedsRebuild;
    private uint _recordedFarHeightCaptureViewSlot;
    private int _recordedFarHeightResolution;
    private uint _recordedFarHeightMipCount;
    private float _recordedFarHeightCoverage;
    private World3DCell _recordedFarHeightAnchorCell;
    private RGTexture _frameFarHeightRG;

    /// <summary>Replaces the host-streamed explicit far heightfield centered on the anchor cell.</summary>
    /// <param name="width">Width of the height grid in texels.</param>
    /// <param name="height">Height of the height grid in texels.</param>
    /// <param name="worldCoverageMeters">Square world-space coverage in meters centered on the anchor cell.</param>
    /// <param name="heights">Row-major height samples in meters (+Z up; values &lt;= -99999 mark empty cells).</param>
    /// <param name="originAnchorCell">Optional anchor cell; defaults to (0, 0, 0) or active camera anchor.</param>
    /// <returns>Whether the heightfield parameters were valid and staged for upload.</returns>
    public bool TrySetFarHeightfield(int width, int height, float worldCoverageMeters, ReadOnlySpan<float> heights,
        World3DCell? originAnchorCell = null)
    {
        BeginEdit();
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096
            || !float.IsFinite(worldCoverageMeters) || worldCoverageMeters <= 0f
            || heights.Length != checked(width * height))
            return false;

        _farHeightPendingUploads.Clear();
        _farHeightPendingUploads.Add(new FarHeightPendingUpload(
            0, 0, width, height, heights.ToArray(), true, worldCoverageMeters, originAnchorCell));
        _farHeightExplicitDirty = true;
        return true;
    }

    /// <summary>Streams a rectangular sub-region patch into the existing explicit far heightfield.</summary>
    /// <param name="x">The left texel coordinate.</param>
    /// <param name="y">The top texel coordinate.</param>
    /// <param name="width">Patch width in texels.</param>
    /// <param name="height">Patch height in texels.</param>
    /// <param name="heights">Row-major patch height samples matching <paramref name="width"/> * <paramref name="height"/>.</param>
    /// <returns>Whether an explicit far heightfield was initialized and the region bounds were valid.</returns>
    public bool TryUpdateFarHeightfieldRegion(int x, int y, int width, int height, ReadOnlySpan<float> heights)
    {
        BeginEdit();
        if (!_hasExplicitFarHeight && _farHeightPendingUploads.Count == 0)
            return false;

        int targetW = _farHeightPendingUploads.Count > 0 && _farHeightPendingUploads[0].FullReplace
            ? _farHeightPendingUploads[0].Width : _explicitFarHeightWidth;
        int targetH = _farHeightPendingUploads.Count > 0 && _farHeightPendingUploads[0].FullReplace
            ? _farHeightPendingUploads[0].Height : _explicitFarHeightHeight;

        if (x < 0 || y < 0 || width <= 0 || height <= 0
            || x + width > targetW || y + height > targetH
            || heights.Length != checked(width * height))
            return false;

        _farHeightPendingUploads.Add(new FarHeightPendingUpload(
            x, y, width, height, heights.ToArray(), false, 0f, null));
        _farHeightExplicitDirty = true;
        return true;
    }

    /// <summary>Clears any host-streamed explicit far heightfield and forces a far pyramid rebuild when enabled.</summary>
    public void ClearFarHeightfield()
    {
        BeginEdit();
        _farHeightPendingUploads.Clear();
        _hasExplicitFarHeight = false;
        _explicitFarHeightWidth = 0;
        _explicitFarHeightHeight = 0;
        _farHeightExplicitDirty = true;
    }

    private void PrepareFarShadow(RenderContext context, in World3DView primaryView,
        List<World3DView> views, List<World3DCullViewGpu> cullViews, List<uint> targetSlots)
    {
        _recordedFarHeightUploads.Clear();
        _recordedFarHeightActive = false;
        _recordedFarHeightAutoCapture = false;
        _recordedFarHeightNeedsRebuild = false;
        _recordedFarHeightCaptureViewSlot = 0;
        _frameFarHeightRG = default;

        World3DFarShadowSettings far = Shadows.FarField;
        if (!_recordedWorld || !Shadows.Enabled || !far.Enabled)
            return;

        bool hasExplicitSource = _hasExplicitFarHeight || _farHeightPendingUploads.Count != 0;
        if (!far.AutoCaptureFromScene && !hasExplicitSource)
            return;

        int res = _farHeightPendingUploads.Count > 0 && _farHeightPendingUploads[0].FullReplace
            ? Math.Max(_farHeightPendingUploads[0].Width, _farHeightPendingUploads[0].Height)
            : (_hasExplicitFarHeight ? Math.Max(_explicitFarHeightWidth, _explicitFarHeightHeight)
            : Math.Clamp(far.Resolution, 64, 2048));
        res = Math.Clamp(res, 64, 2048);

        float coverage = Math.Clamp(far.WorldCoverageMeters, 256f, 65536f);
        if (_farHeightPendingUploads.Count > 0 && _farHeightPendingUploads[0].FullReplace)
            coverage = Math.Clamp(_farHeightPendingUploads[0].WorldCoverageMeters, 256f, 65536f);

        World3DCell anchorCell = primaryView.OriginAnchorCell;
        if (_farHeightPendingUploads.Count > 0 && _farHeightPendingUploads[0].OriginAnchorCell is World3DCell explicitAnchor)
            anchorCell = explicitAnchor;

        // Stage explicit height buffer uploads.
        if (_farHeightPendingUploads.Count != 0)
        {
            if (_farHeightPendingUploads[0].FullReplace)
                EnsureExplicitFarBaseTexture(_farHeightPendingUploads[0].Width, _farHeightPendingUploads[0].Height);
            foreach (FarHeightPendingUpload upload in _farHeightPendingUploads)
            {
                BufferSlice slice = context.UploadTransient<float>(upload.Data);
                _recordedFarHeightUploads.Add((upload, slice));
            }
        }

        uint mipCount = Math.Min(ComputeMipCount(new Extent2D((uint)res, (uint)res)), MaxHiZMips);
        EnsureFarHeightPyramid(res, mipCount);
        if (far.AutoCaptureFromScene)
            EnsureFarHeightCaptureDepth(res);

        bool needsRebuild = !_farHeightCapturedValid
            || _farHeightExplicitDirty
            || _farHeightCapturedResolution != res
            || MathF.Abs(_farHeightCapturedCoverage - coverage) > 0.5f
            || !_farHeightCapturedAnchorCell.Equals(anchorCell)
            || (far.AutoCaptureFromScene && _farHeightCapturedSceneRevision != _shadowSceneRevision);

        _recordedFarHeightActive = true;
        _recordedFarHeightNeedsRebuild = needsRebuild;
        _recordedFarHeightAutoCapture = needsRebuild && far.AutoCaptureFromScene && _frameDrawSlots != 0;
        _recordedFarHeightResolution = res;
        _recordedFarHeightMipCount = mipCount;
        _recordedFarHeightCoverage = coverage;
        _recordedFarHeightAnchorCell = anchorCell;

        if (_recordedFarHeightAutoCapture)
        {
            // Look straight down (-Z) with up = -Y so NDC +X -> +U and NDC +Y -> -V matches sampleUV = xy * invCov + 0.5.
            Vector3 eyeLocal = new(0f, 0f, FarHeightCaptureZMax);
            Vector3 targetLocal = Vector3.Zero;
            Matrix4x4 capView = Matrix4x4.CreateLookAtLeftHanded(eyeLocal, targetLocal, -Vector3.UnitY);
            Matrix4x4 capProj = Matrix4x4.CreateOrthographicLeftHanded(coverage, coverage, 0f, FarHeightCaptureZSpan);
            Matrix4x4 capVP = capView * capProj;
            World3DView.TryCreateOrthographic(capVP, anchorCell, eyeLocal, res, out World3DView viewData);
            uint slot = (uint)views.Count;
            _recordedFarHeightCaptureViewSlot = slot;
            views.Add(viewData);
            targetSlots.Add(slot);
            cullViews.Add(new World3DCullViewGpu(
                viewData.CameraVP,
                viewData.EyeCell,
                viewData.EyeLocalNear,
                slot,
                CullViewEnabled | CullViewOrtho | CullViewShadowCasterOnly,
                far.StaticFarMinObjectRadius > 0f ? far.StaticFarMinObjectRadius * 0.5f : 0f,
                50f,
                0f));
        }

        LastRecordedShadows = LastRecordedShadows with { FarHeightfieldActive = true };
    }

    private void ReconcileFarShadow(bool submitted)
    {
        if (submitted && _recordedWorld && _recordedFarHeightActive)
        {
            foreach ((FarHeightPendingUpload upload, _) in _recordedFarHeightUploads)
            {
                if (upload.FullReplace)
                {
                    _hasExplicitFarHeight = true;
                    _explicitFarHeightWidth = upload.Width;
                    _explicitFarHeightHeight = upload.Height;
                }
            }
            if (_recordedFarHeightUploads.Count != 0)
                _farHeightPendingUploads.Clear();

            if (_recordedFarHeightNeedsRebuild)
            {
                _farHeightCapturedValid = true;
                _farHeightCapturedResolution = _recordedFarHeightResolution;
                _farHeightCapturedCoverage = _recordedFarHeightCoverage;
                _farHeightCapturedAnchorCell = _recordedFarHeightAnchorCell;
                _farHeightCapturedSceneRevision = _shadowSceneRevision;
                _farHeightExplicitDirty = false;
            }
        }
        else if (!submitted)
        {
            _farHeightCapturedValid = false;
        }
        _recordedFarHeightUploads.Clear();
    }

    private void DisposeFarShadowResources()
    {
        _farHeightCaptureDepth?.Dispose();
        _farHeightCaptureDepth = null;
        _farHeightExplicitBase?.Dispose();
        _farHeightExplicitBase = null;
        _farHeightPyramid?.Dispose();
        _farHeightPyramid = null;
        _farHeightPendingUploads.Clear();
        _recordedFarHeightUploads.Clear();
        _farHeightCapturedValid = false;
    }

    private void EnsureExplicitFarBaseTexture(int width, int height)
    {
        if (_farHeightExplicitBase != null && _farHeightExplicitBase.Extent.X == (uint)width && _farHeightExplicitBase.Extent.Y == (uint)height)
            return;
        _farHeightExplicitBase?.Dispose();
        _farHeightExplicitBase = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            Format = Format.R32Float,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
    }

    private void EnsureFarHeightCaptureDepth(int res)
    {
        if (_farHeightCaptureDepth != null && _farHeightCaptureDepth.Extent.X == (uint)res)
            return;
        _farHeightCaptureDepth?.Dispose();
        _farHeightCaptureDepth = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D((uint)res, (uint)res, 1),
            Format = Format.D32Float,
            Usage = TextureUsage.Sampled | TextureUsage.DepthStencilAttachment,
        });
    }

    private void EnsureFarHeightPyramid(int res, uint mipCount)
    {
        if (_farHeightPyramid != null && _farHeightPyramid.Extent.X == (uint)res && _farHeightPyramid.MipLevels == mipCount)
            return;
        _farHeightPyramid?.Dispose();
        _farHeightPyramid = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D((uint)res, (uint)res, 1),
            MipLevels = mipCount,
            Format = Format.Rg32Float,
            Usage = TextureUsage.Sampled | TextureUsage.Storage,
        });
        _farHeightCapturedValid = false;
    }

    private sealed class FarShadowPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int UploadPass = 0;
        private const int CapturePass = 1;
        private const int BuildMipBase = 10;

        private readonly World3DScene _scene = scene;
        private RGTexture _explicitBaseRG;
        private RGTexture _captureDepthRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            if (!_scene._recordedWorld || !_scene._recordedFarHeightActive || _scene._farHeightPyramid == null)
                return;

            RGTexture pyramid = builder.ImportTexture(_scene._farHeightPyramid, "World3D.FarHeightPyramid");
            _scene._frameFarHeightRG = pyramid;

            _explicitBaseRG = default;
            _captureDepthRG = default;

            if (_scene._recordedFarHeightUploads.Count != 0 && _scene._farHeightExplicitBase != null)
            {
                _explicitBaseRG = builder.ImportTexture(_scene._farHeightExplicitBase, "World3D.FarHeightExplicitBase");
                RenderGraphPassBuilder upload = builder.AddPass("World3D.FarHeight.UploadBase", UploadPass);
                foreach ((_, BufferSlice slice) in _scene._recordedFarHeightUploads)
                    upload.Read(builder.ImportBuffer(slice.Buffer!, "World3D.FarHeightUpload"), RGUsage.TransferRead);
                upload.Write(_explicitBaseRG, RGUsage.TransferWrite);
            }
            else if (_scene._hasExplicitFarHeight && _scene._farHeightExplicitBase != null)
            {
                _explicitBaseRG = builder.ImportTexture(_scene._farHeightExplicitBase, "World3D.FarHeightExplicitBase");
            }

            if (!_scene._recordedFarHeightNeedsRebuild)
                return;

            if (_scene._recordedFarHeightAutoCapture && _scene._farHeightCaptureDepth != null)
            {
                _captureDepthRG = builder.ImportTexture(_scene._farHeightCaptureDepth, "World3D.FarHeightCaptureDepth");
                RenderGraphPassBuilder cap = builder.AddPass("World3D.FarHeight.Capture", CapturePass);
                cap.Write(_captureDepthRG, RGUsage.DepthStencil);
                if (_scene._frameDrawSlots != 0)
                {
                    RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
                    RGBuffer args = builder.ImportBuffer(_scene._drawArgs!, "World3D.IndexedArgs");
                    RGBuffer drawRefs = builder.ImportBuffer(_scene._drawRefs!, "World3D.DrawRefs");
                    DeclareShadowGeometryReads(_scene, builder, cap, views, args, drawRefs);
                }
            }

            uint mips = _scene._recordedFarHeightMipCount;
            for (uint mip = 0; mip < mips; ++mip)
            {
                RenderGraphPassBuilder build = builder.AddPass($"World3D.FarHeight.BuildMip{mip}", BuildMipBase + (int)mip);
                if (mip == 0)
                {
                    if (_captureDepthRG.IsValid)
                        build.Read(_captureDepthRG, RGUsage.ComputeRead);
                    if (_explicitBaseRG.IsValid)
                        build.Read(_explicitBaseRG, RGUsage.ComputeRead);
                    build.Write(pyramid, RGUsage.ComputeWrite);
                }
                else
                {
                    build.ReadWrite(pyramid, RGUsage.ComputeReadWrite);
                }
                if (mip == mips - 1)
                    build.ProducesOutput();
            }
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            if (passId == UploadPass)
            {
                foreach ((FarHeightPendingUpload upload, BufferSlice slice) in _scene._recordedFarHeightUploads)
                {
                    context.RenderContext.CopyBufferToTexture(slice, _scene._farHeightExplicitBase!, new TextureCopyDesc
                    {
                        Offset = new Extent3D((uint)upload.X, (uint)upload.Y, 0),
                        Extent = new Extent3D((uint)upload.Width, (uint)upload.Height, 1),
                    });
                }
                return;
            }
            if (passId == CapturePass)
            {
                RenderingDesc desc = new()
                {
                    Depth = new DepthAttachment { Target = _scene._farHeightCaptureDepth!.GetRenderTarget(0), Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1.0f },
                };
                using RenderContext.RenderScope pass = context.RenderContext.BeginRenderPass(desc);
                if (_scene._frameDrawSlots != 0)
                    _scene.SubmitShadowBucketDraws(pass, _scene._recordedFarHeightCaptureViewSlot, rsm: false);
                return;
            }

            uint mip = (uint)(passId - BuildMipBase);
            _scene._farHeightBuildShader ??= _scene.LoadSceneCompute<FarHeightBuildRoot>("World3DFarHeightBuild");
            uint baseRes = (uint)_scene._recordedFarHeightResolution;
            uint srcRes = mip == 0 ? baseRes : Math.Max(1u, baseRes >> (int)(mip - 1));
            uint dstRes = Math.Max(1u, baseRes >> (int)mip);
            uint flags = 0;
            if (mip == 0)
            {
                if (_captureDepthRG.IsValid) flags |= 1u;
                if (_explicitBaseRG.IsValid) flags |= 2u;
            }

            Texture pyramid = _scene._farHeightPyramid!;
            FarHeightBuildRoot root = new(
                mip == 0 && _captureDepthRG.IsValid ? GPUTextureId.FromSampled(_scene._farHeightCaptureDepth!) : default,
                mip == 0 && _explicitBaseRG.IsValid ? GPUTextureId.FromSampled(_scene._farHeightExplicitBase!) : default,
                mip > 0 ? GPUTextureId.FromSampled(pyramid) : default,
                pyramid.GetStorageId(mip),
                srcRes,
                srcRes,
                dstRes,
                dstRes,
                mip,
                flags,
                FarHeightCaptureZMax,
                FarHeightCaptureZSpan);
            BufferSlice data = context.RenderContext.UploadTransient(in root);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            uint groups = (dstRes + 7) / 8;
            compute.Dispatch(_scene._farHeightBuildShader, data, new Extent3D(groups, groups, 1));
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct FarHeightBuildRoot(
        GPUTextureId CaptureDepthTex,
        GPUTextureId ExplicitBaseTex,
        GPUTextureId SrcMipTex,
        GPUTextureId DstMipTex,
        uint SrcWidth,
        uint SrcHeight,
        uint DstWidth,
        uint DstHeight,
        uint MipLevel,
        uint Flags,
        float CaptureZMax,
        float CaptureZSpan);
}
