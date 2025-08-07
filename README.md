# YORO Standalone - 2D to 3D SBS Video Converter

This is a standalone .NET library that converts 2D input videos/images to 3D Side-by-Side (SBS) format without Unity dependencies. It's based on the YORO (You Only Render Once) VR rendering optimization technique. [YORO-VR](https://github.com/YORO-VR/YORO-VR)

## Features

- Convert 2D images to 3D SBS format
- Support for Quality and Performance processing modes
- Configurable reprojection scales for performance optimization
- Built-in depth estimation using gradient-based algorithms
- Console application demonstrating usage
- No Unity dependencies - pure .NET implementation

## Architecture

### YORO.Core Library
The main library containing:
- `YOROProcessor`: Core algorithm for depth-based reprojection
- `VideoProcessor`: High-level interface for image/video processing
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

// Create processor
var processor = new VideoProcessor(config);

// Convert image
await processor.ConvertImageAsync("input.jpg", "output_sbs.jpg");
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
