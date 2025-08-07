using System.Security.Cryptography;

namespace YORO.Core;

/// <summary>
/// Helper class to download and manage Depth-Anything-ONNX v2.0.0 models
/// </summary>
public static class DepthAnythingModelDownloader
{
    // Depth-Anything V2 model URLs from the GitHub release v2.0.0
    private static readonly Dictionary<string, (string Url, string Sha256)> ModelUrls = new()
    {
        ["vits"] = ("https://github.com/fabio-sim/Depth-Anything-ONNX/releases/download/v2.0.0/depth_anything_v2_vits.onnx", ""),
        ["vitb"] = ("https://github.com/fabio-sim/Depth-Anything-ONNX/releases/download/v2.0.0/depth_anything_v2_vitb.onnx", ""),
        ["vitl"] = ("https://github.com/fabio-sim/Depth-Anything-ONNX/releases/download/v2.0.0/depth_anything_v2_vitl.onnx", "")
    };

    /// <summary>
    /// Get the path to a Depth-Anything model, downloading it if necessary
    /// </summary>
    /// <param name="modelSize">Model size: "vits" (small), "vitb" (base), or "vitl" (large)</param>
    /// <param name="modelsDirectory">Directory to store models (defaults to ./models)</param>
    /// <returns>Path to the model file</returns>
    public static async Task<string> GetModelPathAsync(string modelSize = "vitb", string? modelsDirectory = null)
    {
        if (!ModelUrls.ContainsKey(modelSize.ToLower()))
        {
            throw new ArgumentException($"Invalid model size '{modelSize}'. Valid options: {string.Join(", ", ModelUrls.Keys)}");
        }

        // Default models directory
        modelsDirectory ??= Path.Combine(AppContext.BaseDirectory, "models");
        
        // Ensure models directory exists
        Directory.CreateDirectory(modelsDirectory);

        var modelFileName = $"depth_anything_v2_{modelSize.ToLower()}.onnx";
        var modelPath = Path.Combine(modelsDirectory, modelFileName);

        // Check if model already exists
        if (File.Exists(modelPath))
        {
            Console.WriteLine($"Model already exists: {modelPath}");
            return modelPath;
        }

        // Download the model
        var (url, expectedSha256) = ModelUrls[modelSize.ToLower()];
        Console.WriteLine($"Downloading Depth-Anything V2 {modelSize.ToUpper()} model...");
        Console.WriteLine($"URL: {url}");
        Console.WriteLine($"Destination: {modelPath}");

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30); // Allow long download time

            // Download with progress reporting
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            Console.WriteLine($"Model size: {totalBytes / (1024 * 1024):F2} MB");

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(modelPath);

            var buffer = new byte[81920]; // 80KB buffer
            var totalRead = 0L;
            var lastReportTime = DateTime.Now;

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                // Report progress every 2 seconds
                if (DateTime.Now - lastReportTime > TimeSpan.FromSeconds(2))
                {
                    var percentage = totalBytes > 0 ? (double)totalRead / totalBytes * 100 : 0;
                    Console.WriteLine($"Downloaded: {totalRead / (1024 * 1024):F2} MB ({percentage:F1}%)");
                    lastReportTime = DateTime.Now;
                }
            }

            Console.WriteLine($"✓ Download completed: {modelPath}");

            // Verify file size
            var fileInfo = new FileInfo(modelPath);
            if (totalBytes > 0 && fileInfo.Length != totalBytes)
            {
                File.Delete(modelPath);
                throw new InvalidOperationException($"Downloaded file size mismatch. Expected: {totalBytes}, Actual: {fileInfo.Length}");
            }

            return modelPath;
        }
        catch (Exception ex)
        {
            // Clean up partial download
            if (File.Exists(modelPath))
            {
                try { File.Delete(modelPath); } catch { }
            }
            
            throw new InvalidOperationException($"Failed to download model: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get the recommended model size based on available memory and performance requirements
    /// </summary>
    /// <param name="preferPerformance">If true, prefer faster models; if false, prefer accuracy</param>
    /// <returns>Recommended model size</returns>
    public static string GetRecommendedModelSize(bool preferPerformance = true)
    {
        // Get available memory (this is a rough estimation)
        var availableMemoryGB = GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0);

        if (preferPerformance)
        {
            return "vits"; // Fastest model
        }
        else if (availableMemoryGB > 8)
        {
            return "vitl"; // Most accurate but requires more memory
        }
        else if (availableMemoryGB > 4)
        {
            return "vitb"; // Good balance
        }
        else
        {
            return "vits"; // Smallest memory footprint
        }
    }

    /// <summary>
    /// List all available models in the models directory
    /// </summary>
    /// <param name="modelsDirectory">Directory to check for models</param>
    /// <returns>List of available model paths</returns>
    public static string[] ListAvailableModels(string? modelsDirectory = null)
    {
        modelsDirectory ??= Path.Combine(AppContext.BaseDirectory, "models");
        
        if (!Directory.Exists(modelsDirectory))
            return Array.Empty<string>();

        return Directory.GetFiles(modelsDirectory, "depth_anything_v2_*.onnx");
    }
}