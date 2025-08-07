using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace YORO.Core;

/// <summary>
/// YORO processing modes
/// </summary>
public enum YOROMode
{
    Quality,
    Performance,
}

/// <summary>
/// YORO reprojection scale for performance mode
/// </summary>
public enum YOROScale
{
    Half = 2,
    Quarter = 4,
    Eighth = 8,
    OneSixteen = 16,
}

/// <summary>
/// Performance patcher mode
/// </summary>
public enum YOROPerformancePatcher
{
    YORO,
    Sample,
}

/// <summary>
/// Configuration for YORO processing
/// </summary>
public class YOROConfig
{
    public YOROMode Mode { get; set; } = YOROMode.Quality;
    public YOROScale ReprojectionScale { get; set; } = YOROScale.Half;
    public YOROPerformancePatcher Patcher { get; set; } = YOROPerformancePatcher.YORO;
    public float EyeTextureResolutionScale { get; set; } = 0.67f;
}

/// <summary>
/// Represents a pixel shift with depth information
/// </summary>
public struct PixelShift
{
    public float Shift;
    public float Depth;
    
    public PixelShift(float shift, float depth)
    {
        Shift = shift;
        Depth = depth;
    }
}

/// <summary>
/// Main YORO processor class for converting 2D video to 3D SBS
/// </summary>
public class YOROProcessor
{
    private readonly YOROConfig _config;
    private int _width, _height;
    private int _intermediateWidth, _intermediateHeight;

    public YOROProcessor(YOROConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Process a single frame to generate stereo view
    /// </summary>
    /// <param name="rightEyeImage">The right eye view image data (RGB24)</param>
    /// <param name="depthData">Depth data corresponding to the image</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="rightViewMatrix">Right eye view matrix</param>
    /// <param name="rightProjectionMatrix">Right eye projection matrix</param>
    /// <param name="leftViewMatrix">Left eye view matrix</param>
    /// <param name="leftProjectionMatrix">Left eye projection matrix</param>
    /// <returns>Side-by-side stereo image (RGB24)</returns>
    public byte[] ProcessFrame(
        byte[] rightEyeImage,
        float[] depthData,
        int width,
        int height,
        Matrix4x4 rightViewMatrix,
        Matrix4x4 rightProjectionMatrix,
        Matrix4x4 leftViewMatrix,
        Matrix4x4 leftProjectionMatrix)
    {
        if (rightEyeImage == null) throw new ArgumentNullException(nameof(rightEyeImage));
        if (depthData == null) throw new ArgumentNullException(nameof(depthData));

        _width = width;
        _height = height;

        if (_config.Mode == YOROMode.Quality)
        {
            return ProcessQualityMode(rightEyeImage, depthData, 
                rightViewMatrix, rightProjectionMatrix, leftViewMatrix, leftProjectionMatrix);
        }
        else
        {
            return ProcessPerformanceMode(rightEyeImage, depthData,
                rightViewMatrix, rightProjectionMatrix, leftViewMatrix, leftProjectionMatrix);
        }
    }

    private byte[] ProcessQualityMode(byte[] rightEyeImage, float[] depthData,
        Matrix4x4 rightViewMatrix, Matrix4x4 rightProjectionMatrix,
        Matrix4x4 leftViewMatrix, Matrix4x4 leftProjectionMatrix)
    {
        // Calculate pixel shifts
        var pixelShifts = CalculatePixelShifts(depthData, 
            rightViewMatrix, rightProjectionMatrix, leftViewMatrix, leftProjectionMatrix);

        // Reproject image using computed shifts
        var leftEyeImage = ReprojectQuality(rightEyeImage, pixelShifts);

        // Create side-by-side output
        return CreateSideBySideImage(leftEyeImage, rightEyeImage);
    }

    private byte[] ProcessPerformanceMode(byte[] rightEyeImage, float[] depthData,
        Matrix4x4 rightViewMatrix, Matrix4x4 rightProjectionMatrix,
        Matrix4x4 leftViewMatrix, Matrix4x4 leftProjectionMatrix)
    {
        var scale = 1.0f / (int)_config.ReprojectionScale;
        _intermediateWidth = (int)(_width * scale);
        _intermediateHeight = (int)(_height * scale);

        // Downsample for performance
        var downsampledDepth = DownsampleDepth(depthData, _width, _height, _intermediateWidth, _intermediateHeight);
        
        // Calculate shifts at reduced resolution
        var pixelShifts = CalculatePixelShifts(downsampledDepth,
            rightViewMatrix, rightProjectionMatrix, leftViewMatrix, leftProjectionMatrix,
            _intermediateWidth, _intermediateHeight);

        // Reproject using performance mode
        var leftEyeImage = ReprojectPerformance(rightEyeImage, pixelShifts);

        return CreateSideBySideImage(leftEyeImage, rightEyeImage);
    }

    private PixelShift[] CalculatePixelShifts(float[] depthData, 
        Matrix4x4 rightViewMatrix, Matrix4x4 rightProjectionMatrix,
        Matrix4x4 leftViewMatrix, Matrix4x4 leftProjectionMatrix,
        int? widthOverride = null, int? heightOverride = null)
    {
        var width = widthOverride ?? _width;
        var height = heightOverride ?? _height;
        var shifts = new PixelShift[width * height];

        var inputVP = rightProjectionMatrix * rightViewMatrix;
        var targetVP = leftProjectionMatrix * leftViewMatrix;
        Matrix4x4.Invert(inputVP, out var inputVPInverse);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var idx = y * width + x;
                var depth = depthData[idx];

                // Convert pixel coordinates to normalized device coordinates
                var u = (float)x / width;
                var v = (float)y / height;
                var ndc = new Vector4(u * 2 - 1, v * 2 - 1, depth, 1);

                // Transform to target view
                var worldPos = Vector4.Transform(ndc, inputVPInverse);
                if (worldPos.W != 0)
                {
                    worldPos /= worldPos.W;
                }

                var targetNdc = Vector4.Transform(worldPos, targetVP);
                if (targetNdc.W != 0)
                {
                    targetNdc /= targetNdc.W;
                }

                var shift = (targetNdc.X + 1) * 0.5f;
                shifts[idx] = new PixelShift(shift, targetNdc.Z);
            }
        }

        return shifts;
    }

    private float[] DownsampleDepth(float[] depthData, int originalWidth, int originalHeight, 
        int targetWidth, int targetHeight)
    {
        var downsampled = new float[targetWidth * targetHeight];
        var scaleX = (float)originalWidth / targetWidth;
        var scaleY = (float)originalHeight / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                var sourceX = (int)(x * scaleX);
                var sourceY = (int)(y * scaleY);
                var sourceIdx = sourceY * originalWidth + sourceX;
                
                if (sourceIdx < depthData.Length)
                {
                    downsampled[y * targetWidth + x] = depthData[sourceIdx];
                }
            }
        }

        return downsampled;
    }

    private byte[] ReprojectQuality(byte[] rightEyeImage, PixelShift[] pixelShifts)
    {
        var leftEyeImage = new byte[rightEyeImage.Length];
        var outputProcessed = new bool[_width * _height];

        // Reprojection with gap filling (based on Unity compute shader)
        for (int y = 0; y < _height; y++)
        {
            var lastX = 0;
            var lastShift = 0.0f;

            for (int x = 0; x < _width; x++)
            {
                var idx = y * _width + x;
                var shift = pixelShifts[idx].Shift;
                var depth = pixelShifts[idx].Depth;

                if (shift > 1) // Fix blind spot at right edge
                {
                    var newX = (int)(shift * _width + 0.5f);
                    if (newX > lastX + 1)
                    {
                        // Fill gap
                        for (int k = lastX + 1; k < newX && k < _width; k++)
                        {
                            var fillIdx = y * _width + k;
                            if (!outputProcessed[fillIdx])
                            {
                                // Interpolate color
                                InterpolatePixel(leftEyeImage, fillIdx, rightEyeImage, idx, lastShift, shift);
                                outputProcessed[fillIdx] = true;
                            }
                        }
                    }
                    break;
                }

                var newX2 = (int)(shift * _width + 0.5f);
                if (newX2 >= 0 && newX2 < _width)
                {
                    var newIdx = y * _width + newX2;

                    if (lastX == 0)
                    {
                        // Fill from start
                        for (int k = 0; k < newX2; k++)
                        {
                            var fillIdx = y * _width + k;
                            if (!outputProcessed[fillIdx])
                            {
                                CopyPixel(leftEyeImage, fillIdx, rightEyeImage, idx);
                                outputProcessed[fillIdx] = true;
                            }
                        }
                    }
                    else if (newX2 > lastX + 1)
                    {
                        // Fill gap
                        for (int k = lastX + 1; k < newX2; k++)
                        {
                            var fillIdx = y * _width + k;
                            if (!outputProcessed[fillIdx])
                            {
                                InterpolatePixel(leftEyeImage, fillIdx, rightEyeImage, idx, lastShift, shift);
                                outputProcessed[fillIdx] = true;
                            }
                        }
                    }

                    if (!outputProcessed[newIdx])
                    {
                        CopyPixel(leftEyeImage, newIdx, rightEyeImage, idx);
                        outputProcessed[newIdx] = true;
                    }

                    lastX = newX2;
                    lastShift = shift;
                }
            }
        }

        return leftEyeImage;
    }

    private byte[] ReprojectPerformance(byte[] rightEyeImage, PixelShift[] pixelShifts)
    {
        var leftEyeImage = new byte[rightEyeImage.Length];

        // Simple sampling-based reprojection
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var idx = y * _width + x;
                
                // Map to reduced resolution shift map
                var reducedX = x * _intermediateWidth / _width;
                var reducedY = y * _intermediateHeight / _height;
                var shiftIdx = reducedY * _intermediateWidth + reducedX;
                
                if (shiftIdx < pixelShifts.Length)
                {
                    var shift = pixelShifts[shiftIdx].Shift;
                    var sourceX = (int)(shift * _width);
                    
                    if (sourceX >= 0 && sourceX < _width)
                    {
                        var sourceIdx = y * _width + sourceX;
                        CopyPixel(leftEyeImage, idx, rightEyeImage, sourceIdx);
                    }
                }
            }
        }

        return leftEyeImage;
    }

    private void CopyPixel(byte[] dest, int destIdx, byte[] src, int srcIdx)
    {
        var destOffset = destIdx * 3;
        var srcOffset = srcIdx * 3;
        
        if (destOffset + 2 < dest.Length && srcOffset + 2 < src.Length)
        {
            dest[destOffset] = src[srcOffset];     // R
            dest[destOffset + 1] = src[srcOffset + 1]; // G
            dest[destOffset + 2] = src[srcOffset + 2]; // B
        }
    }

    private void InterpolatePixel(byte[] dest, int destIdx, byte[] src, int srcIdx, float t1, float t2)
    {
        var destOffset = destIdx * 3;
        var srcOffset = srcIdx * 3;
        
        if (destOffset + 2 < dest.Length && srcOffset + 2 < src.Length)
        {
            // Simple linear interpolation
            var factor = 0.5f; // Simplified interpolation
            dest[destOffset] = (byte)(src[srcOffset] * factor);
            dest[destOffset + 1] = (byte)(src[srcOffset + 1] * factor);
            dest[destOffset + 2] = (byte)(src[srcOffset + 2] * factor);
        }
    }

    private byte[] CreateSideBySideImage(byte[] leftImage, byte[] rightImage)
    {
        var sbsImage = new byte[_width * 2 * _height * 3];

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var srcIdx = (y * _width + x) * 3;
                var leftDestIdx = (y * _width * 2 + x) * 3;
                var rightDestIdx = (y * _width * 2 + x + _width) * 3;

                // Copy left eye to left half
                sbsImage[leftDestIdx] = leftImage[srcIdx];
                sbsImage[leftDestIdx + 1] = leftImage[srcIdx + 1];
                sbsImage[leftDestIdx + 2] = leftImage[srcIdx + 2];

                // Copy right eye to right half
                sbsImage[rightDestIdx] = rightImage[srcIdx];
                sbsImage[rightDestIdx + 1] = rightImage[srcIdx + 1];
                sbsImage[rightDestIdx + 2] = rightImage[srcIdx + 2];
            }
        }

        return sbsImage;
    }
}
