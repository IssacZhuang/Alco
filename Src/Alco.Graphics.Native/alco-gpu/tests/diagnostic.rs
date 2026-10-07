//! Diagnostic: enumerate adapters on this machine and print backend/features
//! so the AUTO selection policy can be tuned against real hardware.

use wgpu_core::global::Global;
use wgpu_types as wgt;

#[test]
fn print_adapters() {
    let desc = wgt::InstanceDescriptor {
        backends: wgt::Backends::PRIMARY,
        flags: wgt::InstanceFlags::empty(),
        memory_budget_thresholds: wgt::MemoryBudgetThresholds::default(),
        backend_options: wgt::BackendOptions::default(),
        display: None,
    };
    let global = Global::new("diag", desc, None);
    for (i, adapter) in global
        .enumerate_adapters(wgt::Backends::PRIMARY, false)
        .iter()
        .enumerate()
    {
        let info = global.adapter_get_info(*adapter);
        let features = global.adapter_features(*adapter);
        let limits = global.adapter_limits(*adapter);
        println!("    full_features = {:?}", features);
        println!(
            "#{i} backend={:?} name={:?} vendor={:#x} device={:#x}\n    \
             passthrough={} taffs={} vws={} immediates={} multi_draw={} ts={} ts_pass={}\n    \
             max_bind_groups={} max_immediate={}",
            info.backend,
            info.name,
            info.vendor,
            info.device,
            features.contains(wgt::Features::PASSTHROUGH_SHADERS),
            features.contains(wgt::Features::TEXTURE_ADAPTER_SPECIFIC_FORMAT_FEATURES),
            features.contains(wgt::Features::VERTEX_WRITABLE_STORAGE),
            features.contains(wgt::Features::IMMEDIATES),
            features.contains(wgt::Features::MULTI_DRAW_INDIRECT_COUNT),
            features.contains(wgt::Features::TIMESTAMP_QUERY),
            features.contains(wgt::Features::TIMESTAMP_QUERY_INSIDE_PASSES),
            limits.max_bind_groups,
            limits.max_immediate_size,
        );
    }
}
