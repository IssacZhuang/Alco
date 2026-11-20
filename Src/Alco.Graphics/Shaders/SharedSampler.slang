// Shared sampler access requires the RenderingSystem sampler heap and its fixed bank slots.

module SharedSampler;

import Core;

[ForceInline]
public SamplerState shared_sampler_linear_repeat()
{
    return gpu_sampler(0);
}

[ForceInline]
public SamplerState shared_sampler_linear_clamp()
{
    return gpu_sampler(1);
}

[ForceInline]
public SamplerState shared_sampler_nearest_repeat()
{
    return gpu_sampler(2);
}

[ForceInline]
public SamplerState shared_sampler_nearest_clamp()
{
    return gpu_sampler(3);
}

[ForceInline]
public SamplerState shared_sampler_anisotropic_repeat()
{
    return gpu_sampler(4);
}

[ForceInline]
public SamplerState shared_sampler_anisotropic_clamp()
{
    return gpu_sampler(5);
}

[ForceInline]
public SamplerComparisonState shared_sampler_depth_comparison()
{
    return gpu_sampler<SamplerComparisonState>(6);
}
