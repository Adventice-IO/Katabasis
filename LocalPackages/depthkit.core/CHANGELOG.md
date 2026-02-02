## Changelog

### 0.14.3
* Fix bug where certain fields were not serializing after GUI changes

### 0.14.2
* Update SDK license agreement

### 0.14.1
* Fixed an issue where Built-in RP Looks were not rendering in Unity 2022.3.19

### 0.14.0
* Updated to Unity 2022.3
* Better handling of clip reconstruction settings when setting a new metadata asset

### 0.13.4
* Updated assembly definitions.

### 0.13.3
* Fixes a runtime error when disabling and re-enabling a component that uses a Mask Generator
* Fixes an issue on Quest 2 where certain compute shaders would fail to link

### 0.13.2
* Bug fixes

### 0.13.1
* Bug fixes

### 0.13.0
* Support new 0.5.0 metadata version with preconfigured texture blending and depth bias compensation settings

### 0.12.2
* Bug fixes

### 0.12.1
* Minor UI updates

### 0.12.0
* Updated to Unity 2020.3
* Improved edge masking usability and performance.
* Added edge dithering to soften clipped edges. 

### 0.11.1
* Updated BiRP Photo Look shader to use light probes by default.
* Exposed Disable Main Directional Shadows toggle in BiRP Photo Look shader.

### 0.11.0
* Apply depth bias to depth everywhere depth is sampled.

### 0.10.2
* Allow BiRP looks to use shadows from all light types. 

### 0.10.1
* If OpenVR is detected, force single pass instanced stereo rendering mode.  A hack to work around OpenVR not reporting to XRSettings.

### 0.10.0
* Allow BiRP light probes
* Pause video or data generation on invisible
* AVPro 2
* Sync collider and triangle mesh to clip bounds

### 0.9.0
* Lifecycle fixes
* Allow changing video player from API

### 0.8.0
* Improved GPU resource management and execution order
* Added support for material property blocks
* Bug fixes

### 0.6.0
* Removed depthkit info from depthkit clip and added depthkit icon to all depthkit components
* Added Edge Smoothing Scale to **Core Mesh Source** Component
* Reorganized utils files into Utils folder

### 0.5.1
* removed Unity Video Player's component menu