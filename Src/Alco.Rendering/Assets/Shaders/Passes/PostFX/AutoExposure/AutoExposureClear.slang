// file "AutoExposureClear.slang" - module "AutoExposureClear"
//
// Clears the 256-bin transient log2 luminance histogram buffer.

#language slang 2025
module AutoExposureClear;

import Core;
import AlcoRendering_AutoExposure;

[GPURoot]
public struct AutoExposureClearRoot
{
    public GPUBufferView Histogram;
};

[shader("compute")]
[numthreads(AUTO_EXPOSURE_BIN_COUNT, 1, 1)]
void mainCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= AUTO_EXPOSURE_BIN_COUNT)
    {
        return;
    }

    AutoExposureClearRoot root = gpu_load_root<AutoExposureClearRoot>();
    gpu_store<uint>(root.Histogram, id.x, 0u);
}
