// using System.Diagnostics;
// using System.Runtime.CompilerServices;
// using WebGPU;
// using WGPU = WebGPU.WebGPU;

// namespace Vocore.Graphics.WebGPU;

// internal unsafe partial class WebGPUCommandBuffer {
//     WGPUCommandEncoder wgpuDeviceCreateCommandEncoder(WGPUDevice device, WGPUCommandEncoderDescriptor* descriptor){
//         GraphicsLogger.Info("Creating command encoder=======");
//         return WGPU.wgpuDeviceCreateCommandEncoder(device, descriptor);
//     }

//     WGPUCommandBuffer wgpuCommandEncoderFinish(WGPUCommandEncoder commandEncoder, WGPUCommandBufferDescriptor* descriptor){
//         GraphicsLogger.Info("Create command buffer");
//         return WGPU.wgpuCommandEncoderFinish(commandEncoder, descriptor);
//     }

//     void wgpuCommandEncoderRelease(WGPUCommandEncoder commandEncoder){
//         GraphicsLogger.Info("Release command encoder");
//         WGPU.wgpuCommandEncoderRelease(commandEncoder);
//     }

//     void wgpuCommandBufferRelease(WGPUCommandBuffer commandBuffer){
//         GraphicsLogger.Info("Release command buffer");
//         WGPU.wgpuCommandBufferRelease(commandBuffer);
//     }

//     WGPURenderPassEncoder wgpuCommandEncoderBeginRenderPass(WGPUCommandEncoder commandEncoder, WGPURenderPassDescriptor* descriptor){
//         GraphicsLogger.Info("Begin render pass");
//         return WGPU.wgpuCommandEncoderBeginRenderPass(commandEncoder, descriptor);
//     }

//     void wgpuRenderPassEncoderEnd(WGPURenderPassEncoder renderPassEncoder){
//         GraphicsLogger.Info("End render pass");
//         WGPU.wgpuRenderPassEncoderEnd(renderPassEncoder);
//     }

//     void wgpuRenderPassEncoderRelease(WGPURenderPassEncoder renderPassEncoder){
//         GraphicsLogger.Info("Release render pass");
//         WGPU.wgpuRenderPassEncoderRelease(renderPassEncoder);
//     }

//     WGPUComputePassEncoder wgpuCommandEncoderBeginComputePass(WGPUCommandEncoder commandEncoder, WGPUComputePassDescriptor* descriptor){
//         GraphicsLogger.Info("Begin compute pass");
//         return WGPU.wgpuCommandEncoderBeginComputePass(commandEncoder, descriptor);
//     }

//     void wgpuComputePassEncoderEnd(WGPUComputePassEncoder computePassEncoder){
//         GraphicsLogger.Info("End compute pass");
//         WGPU.wgpuComputePassEncoderEnd(computePassEncoder);
//     }

//     void wgpuComputePassEncoderRelease(WGPUComputePassEncoder computePassEncoder){
//         GraphicsLogger.Info("Release compute pass");
//         WGPU.wgpuComputePassEncoderRelease(computePassEncoder);
//     }
// }