# YORO Standalone - 2D to 3D SBS Video Converter

This is a standalone .NET library that converts 2D input videos/images to 3D Side-by-Side (SBS) format without Unity dependencies. It's based on the YORO (You Only Render Once) VR rendering optimization technique. [YORO-VR](https://github.com/YORO-VR/YORO-VR)

## ✨ New Features (v2.0)

### 🧠 Advanced Depth Estimation with Depth-Anything V2
- **ONNX-based depth estimation** using [Depth-Anything-ONNX v2.0.0](https://github.com/fabio-sim/Depth-Anything-ONNX/releases/tag/v2.0.0)
- **Automatic model download** - Downloads the appropriate ViT model (S/B/L) on first use
- **Fallback support** - Gracefully falls back to gradient-based estimation if ONNX fails
- **GPU acceleration** - Uses CUDA when available, CPU otherwise
- **CUDA 12.8/12.9 Support** - Compatible with CUDA 12.8, 12.9 and later versions for optimal GPU performance

### 🎬 Storage-Efficient Video Processing  
- **Chunked processing** - Processes videos in small segments (default: 100 frames)
- **Massive storage savings** - Avoids extracting all frames at once (200GB+ for 4K videos)
- **Memory efficient** - Processes chunks sequentially and cleans up immediately
- **Progress tracking** - Real-time progress reporting during conversion

## Features

- Convert 2D images to 3D SBS format
- Support for Quality and Performance processing modes
- Configurable reprojection scales for performance optimization
- Advanced depth estimation using state-of-the-art ML models
- Storage-efficient video processing for large files
- Console application demonstrating usage
- No Unity dependencies - pure .NET implementation

## Architecture

### YORO.Core Library
The main library containing:
- `YOROProcessor`: Core algorithm for depth-based reprojection
- `VideoProcessor`: High-level interface for image/video processing with chunked video support
- `OnnxDepthEstimator`: Advanced ML-based depth estimation using Depth-Anything V2
- `DepthEstimator`: Fallback gradient-based depth estimation
- `DepthAnythingModelDownloader`: Automatic ONNX model management
- `YOROConfig`: Configuration options for processing modes

### YORO.ConsoleApp
Sample console application demonstrating the library usage.

## How It Works

1. **Advanced Depth Estimation**: Uses Depth-Anything V2 ONNX models for state-of-the-art monocular depth estimation
2. **Chunked Video Processing**: Processes large videos in small segments to minimize storage usage
3. **Pixel Reprojection**: Calculates pixel shifts based on depth and camera matrices
4. **Gap Filling**: Interpolates missing pixels in the reprojected image
5. **SBS Generation**: Combines original and reprojected images into side-by-side format

## Processing Modes

### Quality Mode
- Full resolution processing
- Advanced gap filling algorithms
- Better visual quality
- Higher computational cost

### Performance Mode
- Reduced resolution processing (configurable scale: 2x, 4x, 8x, 16x)
- Simplified gap filling
- Faster processing
- Lower memory usage

## Usage

### Command Line
```bash
# Convert an image with default quality mode
YORO.ConsoleApp.exe input.jpg output_sbs.jpg

# Convert with performance mode and quarter resolution
YORO.ConsoleApp.exe input.jpg output_sbs.jpg --mode performance --scale 4

# Help
YORO.ConsoleApp.exe --help
```

### Programmatic API
```csharp
using YORO.Core;

// Configure YORO
var config = new YOROConfig
{
    Mode = YOROMode.Quality,
    ReprojectionScale = YOROScale.Half
};

// Create processor with ONNX depth estimation
using var processor = await VideoProcessor.CreateAsync(
    config, 
    useOnnxDepth: true,    // Use Depth-Anything V2 model
    modelSize: "vitb",     // ViT-Base model (vitb, vits, vitl)
    chunkSize: 100         // Process 100 frames at a time
);

// Convert image
await processor.ConvertImageAsync("input.jpg", "output_sbs.jpg");

// Convert video (storage efficient)
await processor.ConvertVideoAsync("input.mp4", "output_sbs.mp4");
```

## Installation Requirements

### CUDA 12.8/12.9 GPU Support (Optional)

For optimal GPU performance, ensure the following are installed:

1. **NVIDIA GPU**: Compatible graphics card with CUDA Compute Capability 3.5+
2. **NVIDIA CUDA Toolkit 12.8 or 12.9**: Download from [NVIDIA Developer](https://developer.nvidia.com/cuda-downloads)
3. **cuDNN Library**: Compatible with CUDA 12.x (usually included with CUDA toolkit)
4. **NVIDIA Graphics Drivers**: Latest drivers supporting CUDA 12.x

The application will automatically detect CUDA availability and fall back to CPU processing if GPU acceleration is not available.

## Dependencies

- **Microsoft.ML.OnnxRuntime.Gpu 1.21.0**: For ONNX model inference with CUDA 12.8/12.9 support
- **Xabe.FFmpeg**: For video processing capabilities (requires FFmpeg binaries)
- **SixLabors.ImageSharp**: For image manipulation
- **System.Numerics**: For matrix operations

### ONNX Model Requirements

This application automatically downloads Depth-Anything V2 ONNX models on first use:

1. **Automatic download**: Models are downloaded from the official Depth-Anything-ONNX v2.0.0 release
2. **Model selection**: Choose from ViT-Small (~100MB), ViT-Base (~290MB), or ViT-Large (~1GB)
3. **Local caching**: Models are cached locally in the `models/` directory
4. **GPU acceleration**: CUDA 12.8/12.9+ support with automatic fallback to CPU when GPU unavailable

### FFmpeg Binary Requirements

This application uses Xabe.FFmpeg which requires FFmpeg binaries to be available. You can:

1. **Auto-download**: Xabe.FFmpeg can automatically download FFmpeg binaries on first use
2. **Manual installation**: Install FFmpeg manually and ensure it's in your system PATH
3. **Local binaries**: Place FFmpeg binaries in your application directory

For most users, the auto-download feature will handle this automatically.

## Performance Improvements

### Advanced Depth Estimation
- **State-of-the-art accuracy**: Uses Depth-Anything V2 for superior depth estimation
- **Multiple model sizes**: Choose speed vs. accuracy tradeoff
  - ViT-Small: ~13ms inference on RTX4080 (fastest)
  - ViT-Base: ~29ms inference on RTX4080 (balanced)
  - ViT-Large: ~83ms inference on RTX4080 (most accurate)

### Storage-Efficient Video Processing
- **Chunked processing**: Process videos in small segments (default: 100 frames)
- **Minimal storage footprint**: Avoids extracting all frames simultaneously
- **Example**: 4K 120fps 208s video (25,000 frames)
  - **Old approach**: ~200GB temporary storage required
  - **New approach**: ~2GB temporary storage (100x reduction)

### Multi-Threading Support
- Frame processing now uses parallel processing with `Parallel.For`
- Utilizes all available CPU cores for faster video conversion
- Significantly improves processing time for video files with many frames

### Processing Modes
Both Quality and Performance modes now benefit from multi-threading:
- **Quality Mode**: Full resolution with parallel frame processing
- **Performance Mode**: Reduced resolution with parallel processing for maximum speed

## Supported Formats

### Input
- Images: JPEG, PNG, BMP
- Videos: MP4, AVI (via FFMpeg)

### Output
- Images: JPEG, PNG
- Videos: MP4 (via FFMpeg)

## Limitations

- Video processing requires FFmpeg binaries
- ONNX models require significant GPU memory for optimal performance
- Large videos may take considerable time to process even with chunking
- Real-time processing not yet optimized

## Building

```bash
cd YOROStandalone
dotnet build
```

## Running Tests

```bash
cd YORO.ConsoleApp
dotnet run
# Follow interactive prompts for demo
```

## Performance Notes

- **ONNX Depth Estimation**: Quality varies by model size, with ViT-Large providing best results
- **Chunked Processing**: Dramatically reduces storage requirements for large video files
- **Quality mode**: Uses full resolution with ONNX depth estimation for best results  
- **Performance mode**: Uses reduced resolution processing for faster conversion
- **Memory usage**: Scales with chunk size and video resolution

## Algorithm Details

The core algorithm implements the YORO technique with advanced depth estimation:

1. **Model-based Depth Estimation**: Uses Depth-Anything V2 ONNX models for accurate depth prediction
2. **Matrix Setup**: Calculate view and projection matrices for left/right eyes
3. **Chunked Video Processing**: Process large videos in manageable segments to minimize storage
4. **Depth-based Shift Calculation**: For each pixel, calculate where it should appear in the target eye view
5. **Reprojection**: Map pixels from source to target positions
6. **Gap Filling**: Fill gaps using interpolation techniques
7. **SBS Composition**: Combine left and right eye views side-by-side

This approach enables generating high-quality stereo views from single camera input, while efficiently handling large video files that would otherwise require prohibitive amounts of storage space during processing.
