using YORO.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace YORO.ConsoleApp;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("YORO Standalone - 2D to 3D SBS Video Converter");
        Console.WriteLine("===============================================");
        Console.WriteLine();

        if (args.Length == 0)
        {
            ShowUsage();
            await RunInteractiveMode();
        }
        else
        {
            await ProcessCommandLineArgs(args);
        }
    }

    static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  YORO.ConsoleApp.exe <input> <output> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -m, --mode <quality|performance>  Processing mode (default: quality)");
        Console.WriteLine("  -s, --scale <2|4|8|16>           Reprojection scale for performance mode (default: 2)");
        Console.WriteLine("  -p, --patcher <yoro|sample>      Performance patcher mode (default: yoro)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  YORO.ConsoleApp.exe input.jpg output_sbs.jpg");
        Console.WriteLine("  YORO.ConsoleApp.exe input.mp4 output_sbs.mp4 --mode performance --scale 4");
        Console.WriteLine();
    }

    static async Task RunInteractiveMode()
    {
        Console.WriteLine("Interactive Mode");
        Console.WriteLine("Press Enter to run demo with sample image conversion...");
        Console.ReadLine();

        try
        {
            await RunDemo();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Demo failed: {ex.Message}");
        }
    }

    static async Task RunDemo()
    {
        Console.WriteLine("Running YORO Demo...");
        
        // Create a simple test image
        var demoImagePath = "demo_input.jpg";
        var outputImagePath = "demo_output_sbs.jpg";
        
        Console.WriteLine("Creating demo image...");
        await CreateDemoImageAsync(demoImagePath);

        // Configure YORO
        var config = new YOROConfig
        {
            Mode = YOROMode.Quality,
            ReprojectionScale = YOROScale.Half,
            Patcher = YOROPerformancePatcher.YORO
        };

        using var processor = await VideoProcessor.CreateAsync(config, useOnnxDepth: true);

        Console.WriteLine("Processing image...");
        var success = await processor.ConvertImageAsync(demoImagePath, outputImagePath);

        if (success)
        {
            Console.WriteLine($"✓ Demo completed successfully!");
            Console.WriteLine($"  Input: {demoImagePath}");
            Console.WriteLine($"  Output: {outputImagePath}");
            Console.WriteLine($"  The output image contains side-by-side stereo views.");
        }
        else
        {
            Console.WriteLine("✗ Demo failed.");
        }

        // Clean up demo files
        try
        {
            if (File.Exists(demoImagePath))
                File.Delete(demoImagePath);
        }
        catch { }
    }

    static async Task ProcessCommandLineArgs(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Error: Input and output files are required.");
            ShowUsage();
            return;
        }

        var inputPath = args[0];
        var outputPath = args[1];

        // Parse options
        var config = new YOROConfig();
        
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "-m":
                case "--mode":
                    if (i + 1 < args.Length)
                    {
                        if (Enum.TryParse<YOROMode>(args[++i], true, out var mode))
                            config.Mode = mode;
                        else
                            Console.WriteLine($"Warning: Invalid mode '{args[i]}', using default.");
                    }
                    break;
                case "-s":
                case "--scale":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var scale))
                    {
                        config.ReprojectionScale = scale switch
                        {
                            2 => YOROScale.Half,
                            4 => YOROScale.Quarter,
                            8 => YOROScale.Eighth,
                            16 => YOROScale.OneSixteen,
                            _ => YOROScale.Half
                        };
                    }
                    break;
                case "-p":
                case "--patcher":
                    if (i + 1 < args.Length)
                    {
                        if (Enum.TryParse<YOROPerformancePatcher>(args[++i], true, out var patcher))
                            config.Patcher = patcher;
                        else
                            Console.WriteLine($"Warning: Invalid patcher '{args[i]}', using default.");
                    }
                    break;
            }
        }

        Console.WriteLine($"Input: {inputPath}");
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine($"Mode: {config.Mode}");
        Console.WriteLine($"Scale: {config.ReprojectionScale}");
        Console.WriteLine($"Patcher: {config.Patcher}");
        Console.WriteLine();

        using var processor = await VideoProcessor.CreateAsync(config, useOnnxDepth: true);

        try
        {
            bool success;
            var extension = Path.GetExtension(inputPath).ToLower();
            
            if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".bmp")
            {
                Console.WriteLine("Processing image...");
                success = await processor.ConvertImageAsync(inputPath, outputPath);
            }
            else
            {
                Console.WriteLine("Processing video...");
                var progress = new Progress<double>(p => 
                {
                    Console.Write($"\rProgress: {p:P1}");
                });
                success = await processor.ConvertVideoAsync(inputPath, outputPath, progress);
                Console.WriteLine();
            }

            if (success)
            {
                Console.WriteLine("✓ Conversion completed successfully!");
            }
            else
            {
                Console.WriteLine("✗ Conversion failed.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task CreateDemoImageAsync(string path)
    {
        // Create a simple gradient test image using ImageSharp
        const int width = 640;
        const int height = 480;
        
        using var image = new Image<Rgb24>(width, height);
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    // Create a gradient pattern
                    var r = (byte)(255 * x / width);
                    var g = (byte)(255 * y / height);
                    var b = (byte)(255 * (x + y) / (width + height));
                    
                    row[x] = new Rgb24(r, g, b);
                }
            }
        });

        await image.SaveAsJpegAsync(path);
        Console.WriteLine($"Demo image created: {path}");
    }
}
