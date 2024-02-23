There is a potential issue: if a renderPass is created from a framebuffer and then the original framebuffer is destroyed, the renderPass in the new framebuffer becomes a null pointer.
