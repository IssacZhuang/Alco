// Shared sampler access requires the RenderingSystem sampler heap and its fixed bank slots.

#pragma once

#include <NoGraphicsAPI/shader.slang>

[ForceInline]
SamplerState shared_sampler_linear_repeat()
{
    return gpu_sampler(0);
}

[ForceInline]
SamplerState shared_sampler_linear_clamp()
{
    return gpu_sampler(1);
}

[ForceInline]
SamplerState shared_sampler_nearest_repeat()
{
    return gpu_sampler(2);
}

[ForceInline]
SamplerState shared_sampler_nearest_clamp()
{
    return gpu_sampler(3);
}

[ForceInline]
SamplerState shared_sampler_anisotropic_repeat()
{
    return gpu_sampler(4);
}

[ForceInline]
SamplerState shared_sampler_anisotropic_clamp()
{
    return gpu_sampler(5);
}

[ForceInline]
SamplerComparisonState shared_sampler_depth_comparison()
{
    return gpu_sampler<SamplerComparisonState>(6);
}
