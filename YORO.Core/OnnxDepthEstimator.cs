using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

namespace YORO.Core;

/// <summary>
/// ONNX-based depth estimator using Depth-Anything-ONNX v2.0.0 model
/// </summary>
public class OnnxDepthEstimator : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _inputHeight = 518;
    private readonly int _inputWidth = 518;
    private readonly bool _isUsingCuda;
    private bool _disposed = false;

    /// <summary>
    /// Get information about available ONNX Runtime execution providers
    /// </summary>
    /// <returns>Information about CUDA availability and version support</returns>
    public static string GetExecutionProviderInfo()
    {
        var info = new System.Text.StringBuilder();
        info.AppendLine("ONNX Runtime Execution Provider Information:");
        info.AppendLine($"ONNX Runtime Version: {OrtEnv.Instance().GetVersionString()}");
        
        var availableProviders = OrtEnv.Instance().GetAvailableProviders();
        info.AppendLine($"Available Providers: {string.Join(", ", availableProviders)}");
        
        if (availableProviders.Contains("CUDAExecutionProvider"))
        {
            info.AppendLine("✓ CUDA Execution Provider is available");
            info.AppendLine("✓ Supports CUDA 12.8, 12.9 and later versions");
        }
        else
        {
            info.AppendLine("✗ CUDA Execution Provider not available");
            info.AppendLine("  Install CUDA 12.8+ and compatible drivers for GPU acceleration");
        }
        
        return info.ToString();
    }

    /// <summary>
    /// Initialize the ONNX depth estimator with a model file
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file</param>
    public OnnxDepthEstimator(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"ONNX model file not found: {modelPath}");
        }

        try
        {
            // Create ONNX Runtime session with optimized configuration
            var sessionOptions = new SessionOptions();
            
            // Configure session options for better performance
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.ExecutionMode = ExecutionMode.ORT_PARALLEL;
            
            // Try to use GPU if available, fallback to CPU
            try
            {
                // Configure CUDA execution provider for CUDA 12.x support
                // Use simple device ID configuration first
                sessionOptions.AppendExecutionProvider_CUDA(0);
                _isUsingCuda = true;
                Console.WriteLine("CUDA execution provider initialized for ONNX Runtime 1.21.0");
                Console.WriteLine("Compatible with CUDA 12.8/12.9 and later versions");
            }
            catch (Exception cudaEx)
            {
                _isUsingCuda = false;
                Console.WriteLine($"CUDA not available or failed to initialize: {cudaEx.Message}");
                Console.WriteLine("Falling back to CPU execution provider");
                Console.WriteLine("For GPU acceleration, ensure:");
                Console.WriteLine("  1. NVIDIA GPU with CUDA 12.8+ drivers");
                Console.WriteLine("  2. CUDA Runtime 12.8+ installed");
                Console.WriteLine("  3. cuDNN library compatible with CUDA 12.x");
            }

            _session = new InferenceSession(modelPath, sessionOptions);

            // Get input and output names from the model
            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();

            var provider = _isUsingCuda ? "CUDA (GPU)" : "CPU";
            Console.WriteLine($"Depth estimation model loaded: {Path.GetFileName(modelPath)}");
            Console.WriteLine($"Execution provider: {provider}");
            Console.WriteLine($"Input: {_inputName}, Output: {_outputName}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load ONNX model: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Check if this instance is using CUDA for acceleration
    /// </summary>
    public bool IsUsingCuda => _isUsingCuda;

    /// <summary>
    /// Estimate depth from a single RGB image using the ONNX model
    /// </summary>
    /// <param name="imageData">RGB image data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <returns>Depth values normalized to 0-1 range</returns>
    public float[] EstimateDepth(byte[] imageData, int width, int height)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OnnxDepthEstimator));

        try
        {
            // Convert RGB data to Image and preprocess
            using var image = Image.LoadPixelData<Rgb24>(imageData, width, height);
            var preprocessedTensor = PreprocessImage(image);

            // Run inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, preprocessedTensor)
            };

            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            // Post-process the depth map
            return PostprocessDepthMap(outputTensor, width, height);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Depth estimation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Preprocess the input image for the ONNX model
    /// </summary>
    private DenseTensor<float> PreprocessImage(Image<Rgb24> image)
    {
        // Resize image to model input size
        using var resizedImage = image.Clone(ctx => ctx.Resize(_inputWidth, _inputHeight));
        
        // Create tensor with shape [1, 3, height, width] (NCHW format)
        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });

        // Normalize and convert to tensor format expected by Depth-Anything
        // ImageNet normalization: mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]
        var mean = new float[] { 0.485f, 0.456f, 0.406f };
        var std = new float[] { 0.229f, 0.224f, 0.225f };

        resizedImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < _inputHeight; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < _inputWidth; x++)
                {
                    var pixel = row[x];
                    
                    // Convert to [0,1] and normalize
                    var r = (pixel.R / 255.0f - mean[0]) / std[0];
                    var g = (pixel.G / 255.0f - mean[1]) / std[1];
                    var b = (pixel.B / 255.0f - mean[2]) / std[2];

                    // Set tensor values in NCHW format
                    tensor[0, 0, y, x] = r; // Red channel
                    tensor[0, 1, y, x] = g; // Green channel
                    tensor[0, 2, y, x] = b; // Blue channel
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// Post-process the depth map output from the ONNX model
    /// </summary>
    private float[] PostprocessDepthMap(Tensor<float> outputTensor, int targetWidth, int targetHeight)
    {
        // Get the depth map dimensions from the output tensor
        var outputShape = outputTensor.Dimensions.ToArray();
        var outputHeight = outputShape[^2]; // Second to last dimension
        var outputWidth = outputShape[^1];  // Last dimension

        // Extract the depth values and normalize
        var depthValues = new float[outputHeight * outputWidth];
        float minDepth = float.MaxValue;
        float maxDepth = float.MinValue;

        // Extract raw depth values and find min/max for normalization
        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                var depth = outputTensor[0, y, x]; // Assuming output is [1, H, W]
                depthValues[y * outputWidth + x] = depth;
                minDepth = Math.Min(minDepth, depth);
                maxDepth = Math.Max(maxDepth, depth);
            }
        }

        // Normalize depth values to [0, 1] range
        var depthRange = maxDepth - minDepth;
        if (depthRange > 0)
        {
            for (int i = 0; i < depthValues.Length; i++)
            {
                depthValues[i] = (depthValues[i] - minDepth) / depthRange;
            }
        }

        // Resize depth map to match target image dimensions if needed
        if (outputWidth != targetWidth || outputHeight != targetHeight)
        {
            return ResizeDepthMap(depthValues, outputWidth, outputHeight, targetWidth, targetHeight);
        }

        return depthValues;
    }

    /// <summary>
    /// Resize depth map to target dimensions using bilinear interpolation
    /// </summary>
    private float[] ResizeDepthMap(float[] depthMap, int srcWidth, int srcHeight, int targetWidth, int targetHeight)
    {
        var resized = new float[targetWidth * targetHeight];
        var xScale = (float)srcWidth / targetWidth;
        var yScale = (float)srcHeight / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                var srcX = x * xScale;
                var srcY = y * yScale;

                var x0 = (int)Math.Floor(srcX);
                var y0 = (int)Math.Floor(srcY);
                var x1 = Math.Min(x0 + 1, srcWidth - 1);
                var y1 = Math.Min(y0 + 1, srcHeight - 1);

                var fx = srcX - x0;
                var fy = srcY - y0;

                // Bilinear interpolation
                var v00 = depthMap[y0 * srcWidth + x0];
                var v01 = depthMap[y0 * srcWidth + x1];
                var v10 = depthMap[y1 * srcWidth + x0];
                var v11 = depthMap[y1 * srcWidth + x1];

                var v0 = v00 * (1 - fx) + v01 * fx;
                var v1 = v10 * (1 - fx) + v11 * fx;
                var value = v0 * (1 - fy) + v1 * fy;

                resized[y * targetWidth + x] = value;
            }
        }

        return resized;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
    }
}