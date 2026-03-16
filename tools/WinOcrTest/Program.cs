using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

// ベンチマークモード
if (args.Length > 0 && args[0] == "--bench")
{
    var benchOutput = args.Length > 1 ? args[1] : "benchmark_output";
    var benchScale = 2;
    var benchPadding = 15;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--scale" && i + 1 < args.Length) benchScale = int.Parse(args[++i]);
        if (args[i] == "--padding" && i + 1 < args.Length) benchPadding = int.Parse(args[++i]);
    }
    await WinOcrTest.BenchmarkMode.RunAsync(benchOutput, benchScale, benchPadding);
    return;
}

if (args.Length == 0)
{
    Console.WriteLine("Usage: WinOcrTest <image-path> [language-tag] [options]");
    Console.WriteLine("  language-tag: en, ja, zh-Hans, ko, etc. (default: en)");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --crop x,y,w,h    Crop region in pixels before OCR");
    Console.WriteLine("  --scale N          Scale factor (default: 1, try 2-4 for small text)");
    Console.WriteLine("  --invert           Invert colors (for light text on dark background)");
    Console.WriteLine("  --grayscale        Convert to grayscale");
    Console.WriteLine("  --binarize N       Binarize with threshold N (0-255, default: 128)");
    Console.WriteLine("  --save             Save preprocessed image for inspection");
    Console.WriteLine("  --all              Apply all preprocessing (invert+grayscale+binarize+scale 3x)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  WinOcrTest game.png ja --crop 100,1600,2000,200 --all");
    Console.WriteLine("  WinOcrTest game.png en --scale 2 --invert");
    Console.WriteLine();
    Console.WriteLine("Available OCR languages:");
    foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
    {
        Console.WriteLine($"  {lang.LanguageTag} ({lang.DisplayName})");
    }
    return;
}

// Parse arguments
var imagePath = args[0];
var langTag = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "en";

Rectangle? cropRect = null;
int scaleFactor = 1;
int padding = 0;
int minTextHeight = 0; // 動的拡大率: テキスト高さがこの値未満なら自動拡大
bool invert = false;
bool grayscale = false;
int? binarizeThreshold = null;
bool savePreprocessed = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--crop" when i + 1 < args.Length:
            var parts = args[++i].Split(',');
            if (parts.Length == 4)
                cropRect = new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
            break;
        case "--scale" when i + 1 < args.Length:
            scaleFactor = int.Parse(args[++i]);
            break;
        case "--invert":
            invert = true;
            break;
        case "--grayscale":
            grayscale = true;
            break;
        case "--binarize" when i + 1 < args.Length:
            binarizeThreshold = int.Parse(args[++i]);
            break;
        case "--binarize":
            binarizeThreshold = 128;
            break;
        case "--save":
            savePreprocessed = true;
            break;
        case "--padding" when i + 1 < args.Length:
            padding = int.Parse(args[++i]);
            break;
        case "--padding":
            padding = 15;
            break;
        case "--min-height" when i + 1 < args.Length:
            minTextHeight = int.Parse(args[++i]);
            break;
        case "--all":
            invert = true;
            grayscale = true;
            binarizeThreshold = 128;
            scaleFactor = 3;
            savePreprocessed = true;
            break;
    }
}

if (!File.Exists(imagePath))
{
    Console.WriteLine($"Error: File not found: {imagePath}");
    return;
}

var language = new Windows.Globalization.Language(langTag);
if (!OcrEngine.IsLanguageSupported(language))
{
    Console.WriteLine($"Error: Language '{langTag}' is not supported.");
    return;
}

var engine = OcrEngine.TryCreateFromLanguage(language);
if (engine is null)
{
    Console.WriteLine($"Error: Failed to create OCR engine for '{langTag}'");
    return;
}

Console.WriteLine($"=== Windows OCR Test ===");
Console.WriteLine($"Image: {imagePath}");
Console.WriteLine($"Language: {langTag}");
Console.WriteLine($"Preprocessing: crop={cropRect}, padding={padding}px, scale={scaleFactor}x, minHeight={minTextHeight}, invert={invert}, grayscale={grayscale}, binarize={binarizeThreshold}");
Console.WriteLine();

// Load and preprocess with System.Drawing
using var originalBitmap = new Bitmap(imagePath);
Console.WriteLine($"Original size: {originalBitmap.Width} x {originalBitmap.Height}");

Bitmap processed = originalBitmap;

// Step 1: Crop
if (cropRect is { } crop)
{
    Console.WriteLine($"Cropping: ({crop.X}, {crop.Y}, {crop.Width}x{crop.Height})");
    var cropped = new Bitmap(crop.Width, crop.Height);
    using var g = Graphics.FromImage(cropped);
    g.DrawImage(processed, 0, 0, crop, GraphicsUnit.Pixel);
    if (processed != originalBitmap) processed.Dispose();
    processed = cropped;
}

// Step 1.5: Padding (add border around cropped image)
if (padding > 0)
{
    Console.WriteLine($"Adding padding: {padding}px");
    var padded = new Bitmap(processed.Width + padding * 2, processed.Height + padding * 2);
    using var g = Graphics.FromImage(padded);
    // Fill with background color sampled from corners
    g.Clear(processed.GetPixel(0, 0));
    g.DrawImage(processed, padding, padding);
    if (processed != originalBitmap) processed.Dispose();
    processed = padded;
}

// Step 1.6: Dynamic scale (auto-increase scale if text height is small)
if (minTextHeight > 0 && processed.Height < minTextHeight && scaleFactor <= 1)
{
    scaleFactor = (int)Math.Ceiling((double)minTextHeight / processed.Height);
    Console.WriteLine($"Auto scale: {scaleFactor}x (text height {processed.Height}px < min {minTextHeight}px)");
}

// Step 2: Grayscale
if (grayscale)
{
    Console.WriteLine("Applying grayscale...");
    var gray = new Bitmap(processed.Width, processed.Height);
    for (int y = 0; y < processed.Height; y++)
    for (int x = 0; x < processed.Width; x++)
    {
        var pixel = processed.GetPixel(x, y);
        int lum = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
        gray.SetPixel(x, y, Color.FromArgb(lum, lum, lum));
    }
    if (processed != originalBitmap) processed.Dispose();
    processed = gray;
}

// Step 3: Invert
if (invert)
{
    Console.WriteLine("Inverting colors...");
    var inverted = new Bitmap(processed.Width, processed.Height);
    for (int y = 0; y < processed.Height; y++)
    for (int x = 0; x < processed.Width; x++)
    {
        var pixel = processed.GetPixel(x, y);
        inverted.SetPixel(x, y, Color.FromArgb(255 - pixel.R, 255 - pixel.G, 255 - pixel.B));
    }
    if (processed != originalBitmap) processed.Dispose();
    processed = inverted;
}

// Step 4: Binarize
if (binarizeThreshold is { } threshold)
{
    Console.WriteLine($"Binarizing with threshold {threshold}...");
    var binarized = new Bitmap(processed.Width, processed.Height);
    for (int y = 0; y < processed.Height; y++)
    for (int x = 0; x < processed.Width; x++)
    {
        var pixel = processed.GetPixel(x, y);
        int lum = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
        var bw = lum > threshold ? Color.White : Color.Black;
        binarized.SetPixel(x, y, bw);
    }
    if (processed != originalBitmap) processed.Dispose();
    processed = binarized;
}

// Step 5: Scale
if (scaleFactor > 1)
{
    Console.WriteLine($"Scaling {scaleFactor}x...");
    var scaled = new Bitmap(processed.Width * scaleFactor, processed.Height * scaleFactor);
    using var g = Graphics.FromImage(scaled);
    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
    g.DrawImage(processed, 0, 0, scaled.Width, scaled.Height);
    if (processed != originalBitmap) processed.Dispose();
    processed = scaled;
}

Console.WriteLine($"Processed size: {processed.Width} x {processed.Height}");

// Save preprocessed image
if (savePreprocessed)
{
    var savePath = Path.Combine(Path.GetDirectoryName(imagePath)!,
        Path.GetFileNameWithoutExtension(imagePath) + "_preprocessed.png");
    processed.Save(savePath, ImageFormat.Png);
    Console.WriteLine($"Saved preprocessed image: {savePath}");
}

// Convert System.Drawing.Bitmap to SoftwareBitmap via MemoryStream
using var ms = new MemoryStream();
processed.Save(ms, ImageFormat.Png);
ms.Position = 0;

var randomAccessStream = new InMemoryRandomAccessStream();
var outputStream = randomAccessStream.GetOutputStreamAt(0);
var writer = new DataWriter(outputStream);
writer.WriteBytes(ms.ToArray());
await writer.StoreAsync();
await outputStream.FlushAsync();
randomAccessStream.Seek(0);

var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
    BitmapPixelFormat.Bgra8,
    BitmapAlphaMode.Premultiplied);

// Run OCR
Console.WriteLine();
var sw = System.Diagnostics.Stopwatch.StartNew();
var result = await engine.RecognizeAsync(softwareBitmap);
sw.Stop();

Console.WriteLine($"OCR completed in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Text angle: {result.TextAngle}");
Console.WriteLine($"Lines found: {result.Lines.Count}");
Console.WriteLine();

Console.WriteLine("=== Recognized Text ===");
Console.WriteLine(result.Text);
Console.WriteLine();

Console.WriteLine("=== Detailed Results ===");
for (int i = 0; i < result.Lines.Count; i++)
{
    var line = result.Lines[i];
    Console.WriteLine($"Line {i}: \"{line.Text}\"");
    for (int j = 0; j < line.Words.Count; j++)
    {
        var word = line.Words[j];
        var r = word.BoundingRect;
        Console.WriteLine($"  Word {j}: \"{word.Text}\" @ ({r.X:F0}, {r.Y:F0}, {r.Width:F0}x{r.Height:F0})");
    }
    Console.WriteLine();
}

// Cleanup
if (processed != originalBitmap) processed.Dispose();
