#define PI 3.141592
#define TAU 6.283185
#define E 2.718281

#define READ_ONLY [[vk::ext_decorate(24)]] // OpDecorate NonWritable in SPIR-V
#define WRITE_ONLY [[vk::ext_decorate(25)]] // OpDecorate NonReadable in SPIR-V
#define SLOT(set, bind) [[vk::binding(bind, set)]] // layout(binding = bind, set = set) in GLSL