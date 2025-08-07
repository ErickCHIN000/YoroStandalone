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
public class VideoProcessor : IDisposable
{
    private readonly YOROProcessor _yoroProcessor;
    private readonly DepthEstimator _depthEstimator;
    private readonly OnnxDepthEstimator? _onnxDepthEstimator;
    private readonly int _chunkSize;
    private bool _disposed = false;

    /// <summary>
    /// Initialize video processor with configuration
    /// </summary>
    /// <param name="config">YORO configuration</param>
    /// <param name="modelPath">Optional path to ONNX depth model. If not provided, falls back to gradient-based estimation</param>
    /// <param name="chunkSize">Number of frames to process at once (default: 100)</param>
    public VideoProcessor(YOROConfig config, string? modelPath = null, int chunkSize = 100)
    {
        _yoroProcessor = new YOROProcessor(config);
        _depthEstimator = new DepthEstimator();
        _chunkSize = Math.Max(1, chunkSize);

        // Initialize ONNX depth estimator if model path is provided
        if (!string.IsNullOrEmpty(modelPath) && File.Exists(modelPath))
        {
            try
            {
                // Display ONNX Runtime execution provider information
                Console.WriteLine(OnnxDepthEstimator.GetExecutionProviderInfo());
                
                _onnxDepthEstimator = new OnnxDepthEstimator(modelPath);
                Console.WriteLine("Using ONNX-based depth estimation (Depth-Anything V2)");
                Console.WriteLine($"GPU Acceleration: {(_onnxDepthEstimator.IsUsingCuda ? "Enabled (CUDA)" : "Disabled (CPU)")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to load ONNX model, falling back to gradient-based depth estimation: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Using gradient-based depth estimation (fallback)");
        }
        
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
    /// Convert a 2D video to 3D SBS format using chunked processing to minimize storage usage
    /// </summary>
    /// <param name="inputPath">Path to input video file</param>
    /// <param name="outputPath">Path to output SBS video file</param>
    /// <param name="progress">Progress callback</param>
    /// <returns>True if conversion successful</returns>
    public async Task<bool> ConvertVideoAsync(string inputPath, string outputPath, IProgress<double>? progress = null)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input video file not found: {inputPath}");

        var tempChunksDir = Path.Combine(Path.GetTempPath(), $"yoro_chunks_{Guid.NewGuid():N}");

        try
        {
            // Create temporary directory for chunks
            Directory.CreateDirectory(tempChunksDir);

            Console.WriteLine("Analyzing video...");
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.First();
            
            var totalFrames = (int)(videoStream.Duration.TotalSeconds * videoStream.Framerate);
            var fps = videoStream.Framerate;
            var width = videoStream.Width;
            var height = videoStream.Height;
            
            Console.WriteLine($"Video info: {width}x{height}, {fps:F2} fps, {totalFrames} frames");
            Console.WriteLine($"Processing in chunks of {_chunkSize} frames to minimize storage usage");

            var totalChunks = (int)Math.Ceiling((double)totalFrames / _chunkSize);
            Console.WriteLine($"Total chunks: {totalChunks}");

            // Create camera matrices (same as before)
            var viewMatrixRight = CreateViewMatrix(0.032f, 0); 
            var viewMatrixLeft = CreateViewMatrix(-0.032f, 0);
            var projectionMatrix = CreateProjectionMatrix(90.0f, (float)width / height, 0.1f, 1000.0f);

            var processedChunks = new List<string>();

            // Process video in chunks
            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                var startFrame = chunkIndex * _chunkSize;
                var endFrame = Math.Min(startFrame + _chunkSize - 1, totalFrames - 1);
                var framesInChunk = endFrame - startFrame + 1;
                
                Console.WriteLine($"Processing chunk {chunkIndex + 1}/{totalChunks} (frames {startFrame}-{endFrame})");

                var chunkResult = await ProcessVideoChunkAsync(
                    inputPath, tempChunksDir, chunkIndex,
                    startFrame, framesInChunk, fps,
                    viewMatrixRight, projectionMatrix, viewMatrixLeft, projectionMatrix,
                    width, height);

                if (chunkResult != null)
                {
                    processedChunks.Add(chunkResult);
                }

                // Report progress
                var chunkProgress = (double)(chunkIndex + 1) / totalChunks * 0.9; // 90% for chunk processing
                progress?.Report(chunkProgress);
            }

            if (processedChunks.Count == 0)
            {
                throw new InvalidOperationException("No chunks were processed successfully");
            }

            // Combine all chunks into final video
            Console.WriteLine("Combining chunks into final video...");
            await CombineVideoChunksAsync(processedChunks, outputPath, mediaInfo);

            progress?.Report(1.0); // 100% complete
            
            Console.WriteLine($"Successfully processed {totalFrames} frames in {totalChunks} chunks");
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
            // Clean up temporary files
            await CleanupTempDirectoryAsync(tempChunksDir);
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
    /// Process a single video chunk
    /// </summary>
    private async Task<string?> ProcessVideoChunkAsync(
        string inputVideoPath, string tempDir, int chunkIndex,
        int startFrame, int frameCount, double fps,
        Matrix4x4 viewMatrixRight, Matrix4x4 projectionMatrixRight,
        Matrix4x4 viewMatrixLeft, Matrix4x4 projectionMatrixLeft,
        int width, int height)
    {
        var chunkFramesDir = Path.Combine(tempDir, $"chunk_{chunkIndex}_frames");
        var chunkSbsFramesDir = Path.Combine(tempDir, $"chunk_{chunkIndex}_sbs");
        var chunkVideoPath = Path.Combine(tempDir, $"chunk_{chunkIndex}.mp4");

        try
        {
            Directory.CreateDirectory(chunkFramesDir);
            Directory.CreateDirectory(chunkSbsFramesDir);

            // Extract frames for this chunk only
            var startTime = TimeSpan.FromSeconds(startFrame / fps);
            var duration = TimeSpan.FromSeconds(frameCount / fps);

            var frameExtractionConversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputVideoPath}\"")
                .AddParameter($"-ss {startTime.TotalSeconds:F3}")
                .AddParameter($"-t {duration.TotalSeconds:F3}")
                .AddParameter($"-vf fps={fps}")
                .AddParameter($"\"{Path.Combine(chunkFramesDir, "frame_%06d.png")}\"")
                .SetOverwriteOutput(true);
            
            await frameExtractionConversion.Start();

            // Get extracted frames
            var frameFiles = Directory.GetFiles(chunkFramesDir, "frame_*.png")
                .OrderBy(f => f)
                .ToArray();

            if (frameFiles.Length == 0)
            {
                Console.WriteLine($"Warning: No frames extracted for chunk {chunkIndex}");
                return null;
            }

            Console.WriteLine($"  Chunk {chunkIndex}: Processing {frameFiles.Length} frames");

            // Process frames in parallel
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            await Task.Run(() =>
            {
                Parallel.For(0, frameFiles.Length, parallelOptions, i =>
                {
                    var frameFile = frameFiles[i];
                    var sbsFrameFile = Path.Combine(chunkSbsFramesDir, $"sbs_frame_{i:D6}.png");

                    ProcessSingleFrameAsync(frameFile, sbsFrameFile,
                        viewMatrixRight, projectionMatrixRight, viewMatrixLeft, projectionMatrixLeft).Wait();
                });
            });

            // Create chunk video from processed frames
            var sbsFrameFiles = Directory.GetFiles(chunkSbsFramesDir, "sbs_frame_*.png")
                .OrderBy(f => f)
                .ToArray();

            if (sbsFrameFiles.Length > 0)
            {
                var videoAssemblyConversion = FFmpeg.Conversions.New()
                    .AddParameter($"-framerate {fps}")
                    .AddParameter($"-i \"{Path.Combine(chunkSbsFramesDir, "sbs_frame_%06d.png")}\"")
                    .AddParameter("-c:v libx265")
                    .AddParameter("-pix_fmt yuv420p")
                    .AddParameter("-crf 23")
                    .SetOutput(chunkVideoPath)
                    .SetOverwriteOutput(true);

                await videoAssemblyConversion.Start();

                if (File.Exists(chunkVideoPath))
                {
                    Console.WriteLine($"  Chunk {chunkIndex}: Created video ({new FileInfo(chunkVideoPath).Length / (1024 * 1024):F2} MB)");
                }
            }

            return File.Exists(chunkVideoPath) ? chunkVideoPath : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing chunk {chunkIndex}: {ex.Message}");
            return null;
        }
        finally
        {
            // Clean up chunk frame directories immediately to save space
            try
            {
                if (Directory.Exists(chunkFramesDir))
                    Directory.Delete(chunkFramesDir, true);
                if (Directory.Exists(chunkSbsFramesDir))
                    Directory.Delete(chunkSbsFramesDir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to clean up chunk {chunkIndex} frames: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Combine multiple video chunks into a single output video
    /// </summary>
    private async Task CombineVideoChunksAsync(List<string> chunkPaths, string outputPath, IMediaInfo originalMediaInfo)
    {
        if (chunkPaths.Count == 1)
        {
            // Single chunk, just add audio if needed
            await AddAudioToVideoAsync(chunkPaths[0], outputPath, originalMediaInfo);
            return;
        }

        // Create concat file for FFmpeg
        var concatFilePath = Path.Combine(Path.GetTempPath(), $"yoro_concat_{Guid.NewGuid():N}.txt");
        
        try
        {
            // Write concat file
            var concatContent = string.Join(Environment.NewLine, 
                chunkPaths.Select(path => $"file '{path.Replace("\\", "/")}'")); // Use forward slashes for FFmpeg
            
            await File.WriteAllTextAsync(concatFilePath, concatContent);

            // Combine video chunks
            var tempCombinedPath = Path.Combine(Path.GetTempPath(), $"yoro_combined_{Guid.NewGuid():N}.mp4");
            
            var concatConversion = FFmpeg.Conversions.New()
                .AddParameter($"-f concat")
                .AddParameter($"-safe 0")
                .AddParameter($"-i \"{concatFilePath}\"")
                .AddParameter("-c copy")
                .SetOutput(tempCombinedPath)
                .SetOverwriteOutput(true);

            await concatConversion.Start();

            if (!File.Exists(tempCombinedPath))
            {
                throw new InvalidOperationException("Failed to combine video chunks");
            }

            // Add audio from original video
            await AddAudioToVideoAsync(tempCombinedPath, outputPath, originalMediaInfo);

            // Clean up temporary combined file
            if (File.Exists(tempCombinedPath))
                File.Delete(tempCombinedPath);
        }
        finally
        {
            // Clean up concat file
            if (File.Exists(concatFilePath))
            {
                try { File.Delete(concatFilePath); } catch { }
            }
        }
    }

    /// <summary>
    /// Add audio track from original video to processed video
    /// </summary>
    private async Task AddAudioToVideoAsync(string videoPath, string outputPath, IMediaInfo originalMediaInfo)
    {
        if (originalMediaInfo.AudioStreams.Any())
        {
            // Get the original video path from the media info
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
            // No audio, just copy the video
            File.Copy(videoPath, outputPath, true);
        }
    }

    /// <summary>
    /// Clean up temporary directory and all chunk files
    /// </summary>
    private async Task CleanupTempDirectoryAsync(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                // Delete chunk videos first
                var chunkVideos = Directory.GetFiles(tempDir, "chunk_*.mp4");
                foreach (var chunkVideo in chunkVideos)
                {
                    try { File.Delete(chunkVideo); } catch { }
                }

                // Wait a bit for file handles to be released
                await Task.Delay(100);

                // Delete the entire directory
                Directory.Delete(tempDir, true);
                Console.WriteLine("Cleaned up temporary files");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to clean up temporary directory {tempDir}: {ex.Message}");
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

        // Generate depth estimation using ONNX model if available, otherwise fallback to gradient-based
        float[] depthData;
        if (_onnxDepthEstimator != null)
        {
            depthData = _onnxDepthEstimator.EstimateDepth(imageData, width, height);
        }
        else
        {
            depthData = _depthEstimator.EstimateDepth(imageData, width, height);
        }

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

    /// <summary>
    /// Create a VideoProcessor with automatic model download
    /// </summary>
    /// <param name="config">YORO configuration</param>
    /// <param name="useOnnxDepth">Whether to use ONNX-based depth estimation</param>
    /// <param name="modelSize">ONNX model size (vits, vitb, vitl)</param>
    /// <param name="chunkSize">Chunk size for video processing</param>
    /// <returns>Configured VideoProcessor</returns>
    public static async Task<VideoProcessor> CreateAsync(YOROConfig config, bool useOnnxDepth = true, string modelSize = "vitb", int chunkSize = 100)
    {
        string? modelPath = null;
        
        if (useOnnxDepth)
        {
            try
            {
                Console.WriteLine("Initializing Depth-Anything ONNX model...");
                modelPath = await DepthAnythingModelDownloader.GetModelPathAsync(modelSize);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download ONNX model, falling back to gradient-based depth estimation: {ex.Message}");
            }
        }

        return new VideoProcessor(config, modelPath, chunkSize);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _onnxDepthEstimator?.Dispose();
            _disposed = true;
        }
    }
}