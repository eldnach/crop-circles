# Procedural VFX Demo 

Compute, Indirect Rendering and GPU Culling used to render large amounts of procedural geometry with low rendering overhead. 

Procedural drawing also reduces build size, by eliminating the need for prebaked assets. The demo uses a single 256x256 texture set for grass and noise:

<p align="center">
  <img width="100%" src="https://github.com/eldnach/crop-circles/blob/main/.github/images/crop-circles_1.gif?raw=true" alt="crop-circles_1">
</p>

<p align="center">
  <img width="100%" src="https://github.com/eldnach/crop-circles/blob/main/.github/images/crop-circles_2.gif?raw=true" alt="crop-circles_2">
</p2>

Particle effects created using shader and VFX graph:

<p align="center">
  <img width="100%" src="https://github.com/eldnach/crop-circles/blob/main/.github/images/shader.gif?raw=true" alt="shader">
</p2>

Made in Unity 6 using a custom scriptable render pass, compute, vertex and fragment shaders.
