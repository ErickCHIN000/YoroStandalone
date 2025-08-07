# YORO Standalone - 2D to 3D SBS Video Converter

A standalone .NET implementation of YORO (You Only Render Once) for converting 2D videos to 3D Side-by-Side (SBS) format with **massive storage efficiency improvements**. Based on the YORO VR rendering optimization technique: [YORO-VR](https://github.com/YORO-VR/YORO-VR)

## 🚀 Key Features

### Storage-Efficient Video Processing ⭐ **NEW**
- **Chunked processing** - Processes videos in small segments (default: 100 frames)
- **Massive storage savings** - Avoids extracting all frames at once (up to **86.7% storage reduction**)
- **Memory efficient** - Processes chunks sequentially and cleans up immediately
- **Progress tracking** - Real-time progress reporting during conversion

### Comparison: Old vs New Approach

| Video Type | Old Approach | New Chunked | Storage Savings |
|------------|--------------|-------------|-----------------|
| 4K 30fps 10min | 1,501.7 GB | 200.2 GB | **86.7%** |
| 4K 60fps 10min | 3,003.4 GB | 400.5 GB | **86.7%** |
| 1080p 1hour | 2,252.5 GB | 300.3 GB | **86.7%** |

### Two Processing Modes ⭐ **NEW**

1. **Standard VideoProcessor** - Chunked processing with configurable chunk sizes
2. **FastVideoProcessor** - Minimal disk usage, only creates final output + one assistant file

Example of FastVideoProcessor operation:
```
input.mp4 → [processing] → assistant.mp4 → output.mp4
          ↗ (temporary)                    ↗ (final result)
```

### Core Features
- Convert 2D images to 3D SBS format
- Support for Quality and Performance processing modes
- Configurable reprojection scales for performance optimization
- Built-in depth estimation using gradient-based algorithms
- Console application demonstrating usage
- No Unity dependencies - pure .NET implementation

## Architecture

### YORO.Core Library ⭐ **UPDATED**
The main library containing:
- `YOROProcessor`: Core algorithm for depth-based reprojection
- `VideoProcessor`: **NEW** Chunked video processing with storage efficiency
- `FastVideoProcessor`: **NEW** Minimal disk usage variant
- `DepthEstimator`: Simple depth estimation from monocular images
- `YOROConfig`: Configuration options for processing modes

### YORO.ConsoleApp
Sample console application demonstrating the library usage.

## How It Works

1. **Depth Estimation**: Estimates depth from a single 2D image using gradient-based methods
2. **Pixel Reprojection**: Calculates pixel shifts based on depth and camera matrices
3. **Gap Filling**: Interpolates missing pixels in the reprojected image
4. **SBS Generation**: Combines original and reprojected images into side-by-side format

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

### Command Line Interface ⭐ **UPDATED**

```bash
# Basic image conversion
YORO.ConsoleApp.exe input.jpg output_sbs.jpg

# Video conversion with custom chunk size (NEW)
YORO.ConsoleApp.exe input.mp4 output_sbs.mp4 --chunk 50

# Performance mode with custom settings
YORO.ConsoleApp.exe input.mp4 output_sbs.mp4 --mode performance --scale 4 --chunk 100
```

### New Options ⭐
- `-c, --chunk <size>` - Chunk size for video processing (default: 100)

### All Options
- `-m, --mode <quality|performance>` - Processing mode (default: quality)
- `-s, --scale <2|4|8|16>` - Reprojection scale for performance mode (default: 2)
- `-p, --patcher <yoro|sample>` - Performance patcher mode (default: yoro)
- `-c, --chunk <size>` - Chunk size for video processing (default: 100)

### Programmatic Usage ⭐ **UPDATED**

```csharp
using YORO.Core;

// Standard chunked processing (NEW)
var config = new YOROConfig
{
    Mode = YOROMode.Quality,
    ReprojectionScale = YOROScale.Half
};

using var processor = new VideoProcessor(config, chunkSize: 100);
var success = await processor.ConvertVideoAsync(inputPath, outputPath, progress);

// Fast processing with minimal disk usage (NEW)
using var fastProcessor = new FastVideoProcessor(config, chunkSize: 50);
var success = await fastProcessor.ConvertVideoFastAsync(inputPath, outputPath, progress);
```

## Dependencies

- **Xabe.FFmpeg**: For video processing capabilities (requires FFmpeg binaries)
- **SixLabors.ImageSharp**: For image manipulation
- **System.Numerics**: For matrix operations

### FFmpeg Binary Requirements

This application uses Xabe.FFmpeg which requires FFmpeg binaries to be available. You can:

1. **Auto-download**: Xabe.FFmpeg can automatically download FFmpeg binaries on first use
2. **Manual installation**: Install FFmpeg manually and ensure it's in your system PATH
3. **Local binaries**: Place FFmpeg binaries in your application directory

For most users, the auto-download feature will handle this automatically.

## 🔧 Implementation Details ⭐ **NEW**

### Chunked Processing Architecture

The new chunked approach processes videos in segments:

1. **Analyze video** - Get frame count, dimensions, fps
2. **Process chunks** - Extract → Process → Create chunk video → Cleanup frames
3. **Combine chunks** - Concatenate all chunk videos
4. **Add audio** - Merge audio from original video
5. **Cleanup** - Remove all temporary files

### Storage Efficiency Techniques

- **Immediate cleanup** - Frame directories deleted after each chunk
- **Streaming processing** - Minimal memory footprint per chunk
- **Compressed intermediates** - Chunk videos use H.265 compression
- **Progressive assembly** - Chunks combined without re-extraction

### Memory Management

- Parallel frame processing within chunks
- Automatic garbage collection after large operations
- Configurable chunk sizes for memory/speed tradeoffs
- IDisposable pattern for proper resource cleanup

## 📊 Storage Analysis ⭐ **NEW**

Run the included storage comparison tool:

```bash
python3 storage_comparison.py
```

This shows detailed storage usage comparisons between old and new approaches for various video scenarios.

## Performance Improvements

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

- Depth estimation is simplified (gradient-based) - production use would benefit from ML-based depth estimation
- Video processing is currently basic - frame-by-frame conversion
- No real-time processing optimization

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

- Quality mode: ~2-5x slower but better visual results
- Performance mode with 4x scale: ~4x faster than quality mode
- Memory usage scales with image resolution and processing mode

## Algorithm Details

The core algorithm implements the YORO technique:

1. **Matrix Setup**: Calculate view and projection matrices for left/right eyes
2. **Depth-based Shift Calculation**: For each pixel, calculate where it should appear in the target eye view
3. **Reprojection**: Map pixels from source to target positions
4. **Gap Filling**: Fill gaps using interpolation techniques
5. **SBS Composition**: Combine left and right eye views side-by-side


This approach allows generating stereo views from a single camera input, enabling VR content creation from traditional 2D sources.
