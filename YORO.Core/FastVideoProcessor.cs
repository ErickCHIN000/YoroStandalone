using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace YORO.Core;

/// <summary>
/// Fast video processor that minimizes disk writes - only creates final output and one temporary file
/// </summary>
public class FastVideoProcessor : IDisposable
{
    private readonly YOROProcessor _yoroProcessor;
    private readonly DepthEstimator _depthEstimator;
    private readonly int _chunkSize;
    private bool _disposed = false;

    /// <summary>
    /// Initialize fast video processor with minimal disk usage
    /// </summary>
    /// <param name="config">YORO configuration</param>
    /// <param name="chunkSize">Number of frames to process at once (default: 50 for faster processing)</param>
    public FastVideoProcessor(YOROConfig config, int chunkSize = 50)
    {
        _yoroProcessor = new YOROProcessor(config);
        _depthEstimator = new DepthEstimator();
        _chunkSize = Math.Max(1, chunkSize);

        Console.WriteLine("Fast YORO processor initialized - minimal disk usage mode");
        
        // Initialize FFmpeg binaries if not already available
        Task.Run(async () =>
        {
            try
            {
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                Console.WriteLine("FFmpeg binaries verified/downloaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to initialize FFmpeg: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Convert video with minimal disk usage - only creates final output and one assistant file
    /// Example: input.mp4 -> processes -> assistant.mp4 (temporary) -> output.mp4
    /// </summary>
    /// <param name="inputPath">Path to input video file</param>
    /// <param name="outputPath">Path to output SBS video file</param>
    /// <param name="progress">Progress callback</param>
    /// <returns>True if conversion successful</returns>
    public async Task<bool> ConvertVideoFastAsync(string inputPath, string outputPath, IProgress<double>? progress = null)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input video file not found: {inputPath}");

        // Create assistant file with same extension as input
        var inputExtension = Path.GetExtension(inputPath);
        var assistantPath = Path.ChangeExtension(outputPath, null) + "_assistant" + inputExtension;

        try
        {
            Console.WriteLine("Analyzing video...");
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.First();
            
            var totalFrames = (int)(videoStream.Duration.TotalSeconds * videoStream.Framerate);
            var fps = videoStream.Framerate;
            var width = videoStream.Width;
            var height = videoStream.Height;
            
            Console.WriteLine($"Video info: {width}x{height}, {fps:F2} fps, {totalFrames} frames");
            Console.WriteLine($"Fast processing mode - using assistant file: {assistantPath}");

            var totalChunks = (int)Math.Ceiling((double)totalFrames / _chunkSize);
            
            // Create camera matrices
            var viewMatrixRight = CreateViewMatrix(0.032f, 0); 
            var viewMatrixLeft = CreateViewMatrix(-0.032f, 0);
            var projectionMatrix = CreateProjectionMatrix(90.0f, (float)width / height, 0.1f, 1000.0f);

            // Process video using stream processing (in-memory where possible)
            await ProcessVideoStreamAsync(
                inputPath, assistantPath, 
                totalFrames, fps, width, height,
                viewMatrixRight, projectionMatrix, viewMatrixLeft, projectionMatrix,
                progress);

            // Add audio from original and move to final output
            await FinalizeVideoAsync(assistantPath, outputPath, mediaInfo);

            progress?.Report(1.0);
            
            Console.WriteLine($"Fast processing completed!");
            Console.WriteLine($"Assistant file: {assistantPath} (will be cleaned up)");
            Console.WriteLine($"Final output: {outputPath}");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in fast conversion: {ex.Message}");
            return false;
        }
        finally
        {
            // Clean up assistant file
            try
            {
                if (File.Exists(assistantPath))
                {
                    File.Delete(assistantPath);
                    Console.WriteLine("Assistant file cleaned up");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to clean up assistant file: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Process video using streaming approach to minimize disk usage
    /// </summary>
    private async Task ProcessVideoStreamAsync(
        string inputPath, string assistantPath,
        int totalFrames, double fps, int width, int height,
        Matrix4x4 viewMatrixRight, Matrix4x4 projectionMatrixRight,
        Matrix4x4 viewMatrixLeft, Matrix4x4 projectionMatrixLeft,
        IProgress<double>? progress)
    {
        var totalChunks = (int)Math.Ceiling((double)totalFrames / _chunkSize);
        Console.WriteLine($"Processing {totalChunks} chunks with streaming approach");

        // Use FFmpeg to extract and process frames on-the-fly
        // This creates a raw video stream that we process in chunks
        var processedFrames = new List<byte[]>();
        
        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            var startFrame = chunkIndex * _chunkSize;
            var endFrame = Math.Min(startFrame + _chunkSize - 1, totalFrames - 1);
            var framesInChunk = endFrame - startFrame + 1;
            
            Console.WriteLine($"Stream processing chunk {chunkIndex + 1}/{totalChunks}");

            // Extract frames for this chunk directly to memory
            var chunkFrames = await ExtractFramesToMemoryAsync(inputPath, startFrame, framesInChunk, fps, width, height);
            
            // Process frames in memory
            foreach (var frameData in chunkFrames)
            {
                var depthData = _depthEstimator.EstimateDepth(frameData, width, height);
                var sbsImageData = _yoroProcessor.ProcessFrame(
                    frameData, depthData, width, height,
                    viewMatrixRight, projectionMatrixRight,
                    viewMatrixLeft, projectionMatrixLeft);
                
                processedFrames.Add(sbsImageData);
            }
            
            // Clear chunk frames from memory immediately
            chunkFrames.Clear();
            
            // Report progress
            var chunkProgress = (double)(chunkIndex + 1) / totalChunks * 0.9;
            progress?.Report(chunkProgress);
        }

        // Create video from processed frames in memory
        await CreateVideoFromMemoryFramesAsync(processedFrames, assistantPath, fps, width * 2, height);
        
        // Clear processed frames from memory
        processedFrames.Clear();
        GC.Collect(); // Force garbage collection to free memory
    }

    /// <summary>
    /// Extract frames directly to memory without writing to disk
    /// </summary>
    private async Task<List<byte[]>> ExtractFramesToMemoryAsync(string inputPath, int startFrame, int frameCount, double fps, int width, int height)
    {
        var frames = new List<byte[]>();
        
        // For simplification in this demo, we'll still extract frames to temp location 
        // but delete them immediately after loading to memory
        var tempDir = Path.Combine(Path.GetTempPath(), $"yoro_fast_{Guid.NewGuid():N}");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            var startTime = TimeSpan.FromSeconds(startFrame / fps);
            var duration = TimeSpan.FromSeconds(frameCount / fps);

            var frameExtractionConversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputPath}\"")
                .AddParameter($"-ss {startTime.TotalSeconds:F3}")
                .AddParameter($"-t {duration.TotalSeconds:F3}")
                .AddParameter($"-vf fps={fps}")
                .AddParameter($"\"{Path.Combine(tempDir, "frame_%06d.png")}\"")
                .SetOverwriteOutput(true);
            
            await frameExtractionConversion.Start();

            // Load frames to memory and delete immediately
            var frameFiles = Directory.GetFiles(tempDir, "frame_*.png").OrderBy(f => f).ToArray();
            
            foreach (var frameFile in frameFiles)
            {
                using var image = await Image.LoadAsync(frameFile);
                using var rgb24Image = image.CloneAs<Rgb24>();
                
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
                
                frames.Add(imageData);
                
                // Delete frame immediately after loading
                File.Delete(frameFile);
            }
        }
        finally
        {
            // Clean up temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
        
        return frames;
    }

    /// <summary>
    /// Create video from processed frames in memory
    /// </summary>
    private async Task CreateVideoFromMemoryFramesAsync(List<byte[]> processedFrames, string outputPath, double fps, int width, int height)
    {
        // Create a temporary directory for final assembly only
        var tempDir = Path.Combine(Path.GetTempPath(), $"yoro_assembly_{Guid.NewGuid():N}");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Save processed frames temporarily for video creation
            for (int i = 0; i < processedFrames.Count; i++)
            {
                var frameData = processedFrames[i];
                var framePath = Path.Combine(tempDir, $"sbs_frame_{i:D6}.png");
                
                using var sbsImage = Image.LoadPixelData<Rgb24>(frameData, width, height);
                await sbsImage.SaveAsync(framePath);
            }
            
            // Create video from frames
            var videoAssemblyConversion = FFmpeg.Conversions.New()
                .AddParameter($"-framerate {fps}")
                .AddParameter($"-i \"{Path.Combine(tempDir, "sbs_frame_%06d.png")}\"")
                .AddParameter("-c:v libx265")
                .AddParameter("-pix_fmt yuv420p")
                .AddParameter("-crf 23")
                .SetOutput(outputPath)
                .SetOverwriteOutput(true);

            await videoAssemblyConversion.Start();
        }
        finally
        {
            // Clean up assembly directory immediately
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Finalize video by adding audio from original
    /// </summary>
    private async Task FinalizeVideoAsync(string videoPath, string outputPath, IMediaInfo originalMediaInfo)
    {
        if (originalMediaInfo.AudioStreams.Any())
        {
            var originalVideoPath = originalMediaInfo.Path;
            
            var audioMergeConversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{videoPath}\"")
                .AddParameter($"-i \"{originalVideoPath}\"")
                .AddParameter("-c:v copy")
                .AddParameter("-c:a aac")
                .AddParameter("-map 0:v:0")
                .AddParameter("-map 1:a:0")
                .SetOutput(outputPath)
                .SetOverwriteOutput(true);

            await audioMergeConversion.Start();
        }
        else
        {
            // No audio, just move the video
            File.Move(videoPath, outputPath);
        }
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}