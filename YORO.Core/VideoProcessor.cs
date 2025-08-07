using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;
using System.Collections.Concurrent;

namespace YORO.Core;

/// <summary>
/// Video processor that handles 2D to 3D SBS conversion for video files
/// </summary>
public class VideoProcessor
{
    private readonly YOROProcessor _yoroProcessor;
    private readonly DepthEstimator _depthEstimator;

    public VideoProcessor(YOROConfig config)
    {
        _yoroProcessor = new YOROProcessor(config);
        _depthEstimator = new DepthEstimator();
        
        // Initialize FFmpeg binaries if not already available
        // This will automatically download FFmpeg binaries on first use if needed
        Task.Run(async () =>
        {
            try
            {
                // Check if FFmpeg is available, and download if not
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                Console.WriteLine("FFmpeg binaries verified/downloaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to initialize FFmpeg: {ex.Message}");
                Console.WriteLine("You may need to install FFmpeg manually.");
            }
        });
    }

    /// <summary>
    /// Convert a 2D video to 3D SBS format
    /// </summary>
    /// <param name="inputPath">Path to input video file</param>
    /// <param name="outputPath">Path to output SBS video file</param>
    /// <param name="progress">Progress callback</param>
    /// <returns>True if conversion successful</returns>
    public async Task<bool> ConvertVideoAsync(string inputPath, string outputPath, IProgress<double>? progress = null)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input video file not found: {inputPath}");

        var tempFramesDir = Path.Combine(Path.GetTempPath(), $"yoro_frames_{Guid.NewGuid():N}");
        var tempSbsFramesDir = Path.Combine(Path.GetTempPath(), $"yoro_sbs_{Guid.NewGuid():N}");

        try
        {
            // Create temporary directories
            Directory.CreateDirectory(tempFramesDir);
            Directory.CreateDirectory(tempSbsFramesDir);

            Console.WriteLine("Analyzing video...");
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.First();
            
            var totalFrames = (int)(videoStream.Duration.TotalSeconds * videoStream.Framerate);
            var fps = videoStream.Framerate;
            var width = videoStream.Width;
            var height = videoStream.Height;
            
            Console.WriteLine($"Video info: {width}x{height}, {fps:F2} fps, {totalFrames} frames");

            Console.WriteLine("Estimated size: " +
                $"{(totalFrames * width * height * 3 / (1024.0 * 1024.0)):F2} MB (uncompressed RGB)");

            // Step 1: Extract frames from input video
            Console.WriteLine("Extracting frames...");
            var frameExtractionConversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputPath}\"")
                .AddParameter($"-vf fps={fps}")
                .AddParameter($"\"{Path.Combine(tempFramesDir, "frame_%06d.png")}\"")
                .SetOverwriteOutput(true);
            await frameExtractionConversion.Start();

            // Step 2: Process each frame through 2D-to-3D conversion
            var frameFiles = Directory.GetFiles(tempFramesDir, "frame_*.png")
                .OrderBy(f => f)
                .ToArray();

            Console.WriteLine($"Processing {frameFiles.Length} frames with multi-threading...");

            // Create camera matrices (same as in ConvertImageAsync)
            var viewMatrixRight = CreateViewMatrix(0.032f, 0); // IPD = 64mm, half = 32mm
            var viewMatrixLeft = CreateViewMatrix(-0.032f, 0);
            var projectionMatrix = CreateProjectionMatrix(90.0f, (float)width / height, 0.1f, 1000.0f);

            // Use parallel processing for frame conversion
            var processedCount = 0;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount // Use all available cores
            };

            await Task.Run(() =>
            {
                Parallel.For(0, frameFiles.Length, parallelOptions, i =>
                {
                    var frameFile = frameFiles[i];
                    var sbsFrameFile = Path.Combine(tempSbsFramesDir, $"sbs_frame_{i:D6}.png");

                    // Process frame using the same logic as ConvertImageAsync
                    ProcessSingleFrameAsync(frameFile, sbsFrameFile, 
                        viewMatrixRight, projectionMatrix, viewMatrixLeft, projectionMatrix).Wait();

                    // Thread-safe progress reporting
                    var currentCount = Interlocked.Increment(ref processedCount);
                    var progressValue = (double)currentCount / frameFiles.Length * 0.8; // 80% for frame processing
                    progress?.Report(progressValue);

                    if (currentCount % 100 == 0 || currentCount == frameFiles.Length)
                    {
                        Console.WriteLine($"Processed {currentCount}/{frameFiles.Length} frames");
                    }
                });
            });

            // Step 3: Reassemble processed frames into SBS video
            Console.WriteLine("Assembling SBS video...");
            var sbsVideoTemp = Path.Combine(Path.GetTempPath(), $"yoro_video_temp_{Guid.NewGuid():N}.mp4");
            
            // Check if we have any processed frames
            var sbsFrameFiles = Directory.GetFiles(tempSbsFramesDir, "sbs_frame_*.png")
                .OrderBy(f => f)
                .ToArray();
            
            if (sbsFrameFiles.Length == 0)
            {
                throw new InvalidOperationException("No processed frames found");
            }
            
            Console.WriteLine($"Assembling {sbsFrameFiles.Length} SBS frames into video...");
            
            try
            {
                // Use Xabe.FFmpeg to create video from frames
                var videoAssemblyConversion = FFmpeg.Conversions.New()
                    .AddParameter($"-framerate {fps}")
                    .AddParameter($"-i \"{Path.Combine(tempSbsFramesDir, "sbs_frame_%06d.png")}\"")
                    .AddParameter("-c:v libx265")
                    .AddParameter("-pix_fmt yuv420p")
                    .AddParameter("-crf 23")
                    .SetOutput(sbsVideoTemp)
                    .SetOverwriteOutput(true);
                    
                await videoAssemblyConversion.Start();
                    
                if (!File.Exists(sbsVideoTemp))
                {
                    throw new FileNotFoundException($"SBS video was not created at: {sbsVideoTemp}");
                }
                
                Console.WriteLine($"SBS video created: {sbsVideoTemp} ({new FileInfo(sbsVideoTemp).Length} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FFmpeg assembly error: {ex.Message}");
                throw new InvalidOperationException($"Failed to assemble video: {ex.Message}");
            }

            progress?.Report(0.9); // 90% complete

            // Step 4: Add audio from original video if it exists
            Console.WriteLine("Adding audio track...");
            if (mediaInfo.AudioStreams.Any())
            {
                var audioMergeConversion = FFmpeg.Conversions.New()
                    .AddParameter($"-i \"{sbsVideoTemp}\"")
                    .AddParameter($"-i \"{inputPath}\"")
                    .AddParameter("-c:v copy")
                    .AddParameter("-c:a aac")
                    .AddParameter("-map 0:v:0")
                    .AddParameter("-map 1:a:0")
                    .SetOutput(outputPath)
                    .SetOverwriteOutput(true);
                    
                await audioMergeConversion.Start();
                
                // Clean up temporary video file
                if (File.Exists(sbsVideoTemp))
                    File.Delete(sbsVideoTemp);
            }
            else
            {
                // No audio, just copy the video
                if (File.Exists(sbsVideoTemp))
                    File.Move(sbsVideoTemp, outputPath);
                else
                    throw new FileNotFoundException("Temporary SBS video file not found");
            }

            progress?.Report(1.0); // 100% complete
            
            Console.WriteLine($"Processed {frameFiles.Length} frames into SBS video");
            Console.WriteLine($"Output saved to: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting video: {ex.Message}");
            return false;
        }
        finally
        {
            // Clean up temporary directories and files
            try
            {
                if (Directory.Exists(tempFramesDir))
                    Directory.Delete(tempFramesDir, true);
                if (Directory.Exists(tempSbsFramesDir))
                    Directory.Delete(tempSbsFramesDir, true);
                
                // Clean up any remaining temp video files
                var tempVideoFiles = Directory.GetFiles(Path.GetTempPath(), "yoro_video_temp_*.mp4");
                foreach (var tempFile in tempVideoFiles)
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch { /* Ignore individual file cleanup errors */ }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to clean up temporary files: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Process a single image file to demonstrate YORO conversion
    /// </summary>
    /// <param name="inputImagePath">Path to input image</param>
    /// <param name="outputImagePath">Path to output SBS image</param>
    /// <returns>True if successful</returns>
    public async Task<bool> ConvertImageAsync(string inputImagePath, string outputImagePath)
    {
        if (!File.Exists(inputImagePath))
            throw new FileNotFoundException($"Input image file not found: {inputImagePath}");

        try
        {
            // Create camera matrices (simplified - in real app these would come from camera calibration)
            var viewMatrixRight = CreateViewMatrix(0.032f, 0); // IPD = 64mm, half = 32mm
            var viewMatrixLeft = CreateViewMatrix(-0.032f, 0);
            var projectionMatrix = CreateProjectionMatrix(90.0f, 1.0f, 0.1f, 1000.0f); // aspect ratio will be updated

            await ProcessSingleFrameAsync(inputImagePath, outputImagePath,
                viewMatrixRight, projectionMatrix, viewMatrixLeft, projectionMatrix);

            Console.WriteLine($"SBS image saved to: {outputImagePath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting image: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Process a single frame through the 2D-to-3D SBS conversion pipeline
    /// </summary>
    /// <param name="inputFramePath">Path to input frame image</param>
    /// <param name="outputFramePath">Path to output SBS frame image</param>
    /// <param name="viewMatrixRight">Right eye view matrix</param>
    /// <param name="projectionMatrixRight">Right eye projection matrix</param>
    /// <param name="viewMatrixLeft">Left eye view matrix</param>
    /// <param name="projectionMatrixLeft">Left eye projection matrix</param>
    private async Task ProcessSingleFrameAsync(string inputFramePath, string outputFramePath,
        Matrix4x4 viewMatrixRight, Matrix4x4 projectionMatrixRight,
        Matrix4x4 viewMatrixLeft, Matrix4x4 projectionMatrixLeft)
    {
        using var image = await Image.LoadAsync(inputFramePath);
        using var rgb24Image = image.CloneAs<Rgb24>();
        var width = rgb24Image.Width;
        var height = rgb24Image.Height;

        // Update projection matrix with correct aspect ratio
        var aspectRatio = (float)width / height;
        var projectionMatrix = CreateProjectionMatrix(90.0f, aspectRatio, 0.1f, 1000.0f);

        // Convert image to byte array
        var imageData = new byte[width * height * 3];
        rgb24Image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    var pixel = row[x];
                    var idx = (y * width + x) * 3;
                    imageData[idx] = pixel.R;
                    imageData[idx + 1] = pixel.G;
                    imageData[idx + 2] = pixel.B;
                }
            }
        });

        // Generate depth estimation
        var depthData = _depthEstimator.EstimateDepth(imageData, width, height);

        // Process frame to generate SBS
        var sbsImageData = _yoroProcessor.ProcessFrame(
            imageData, depthData, width, height,
            viewMatrixRight, projectionMatrix,
            viewMatrixLeft, projectionMatrix);

        // Save SBS image
        var sbsWidth = width * 2;
        var sbsHeight = height;
        using var sbsImage = Image.LoadPixelData<Rgb24>(sbsImageData, sbsWidth, sbsHeight);
        await sbsImage.SaveAsync(outputFramePath);
    }

    private Matrix4x4 CreateViewMatrix(float eyeOffsetX, float eyeOffsetY)
    {
        return Matrix4x4.CreateTranslation(eyeOffsetX, eyeOffsetY, 0);
    }

    private Matrix4x4 CreateProjectionMatrix(float fovDegrees, float aspectRatio, float nearPlane, float farPlane)
    {
        var fovRadians = fovDegrees * MathF.PI / 180.0f;
        return Matrix4x4.CreatePerspectiveFieldOfView(fovRadians, aspectRatio, nearPlane, farPlane);
    }
}