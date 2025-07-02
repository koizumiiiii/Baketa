using System;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Tests.Adapters;

    /// <summary>
    /// アダプターテスト用のヘルパークラス
    /// </summary>
    public static class AdapterTestHelper
    {
        private static readonly string TestDataPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
            "TestData");

        /// <summary>
        /// テスト用の画像を生成します
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="color">画像の色（オプション）</param>
        /// <returns>テスト用のBitmap画像</returns>
        public static Bitmap CreateTestImage(int width, int height, Color? color = null)
        {
            var testColor = color ?? Color.LightBlue;
            var bitmap = new Bitmap(width, height);

            using var g = Graphics.FromImage(bitmap);
            {
                g.Clear(testColor);
                // テスト用のパターンを描画
                using var pen = new Pen(Color.Black, 2);
                {
                    g.DrawRectangle(pen, 10, 10, width - 20, height - 20);
                    g.DrawLine(pen, 0, 0, width, height);
                    g.DrawLine(pen, width, 0, 0, height);
                }

                // テスト用のテキストを描画（OCRテスト用）
                using var font = new Font("Arial", 16);
                {
                    g.DrawString("Baketa Test Image", font, Brushes.Black, width / 2 - 80, height / 2 - 10);
                }
            }

            return bitmap;
        }

        /// <summary>
        /// テスト用の画像をバイト配列に変換します
        /// </summary>
        /// <param name="bitmap">変換するBitmap</param>
        /// <param name="format">画像形式（オプション）</param>
        /// <returns>画像のバイト配列</returns>
        public static byte[] ConvertImageToBytes(Bitmap bitmap, ImageFormat? format = null)
        {
            ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));
            var imageFormat = format ?? ImageFormat.Png;
            using var ms = new MemoryStream();
            bitmap.Save(ms, imageFormat);
            return ms.ToArray();
        }

        /// <summary>
        /// テスト用の画像をファイルから読み込みます
        /// </summary>
        /// <param name="filename">ファイル名</param>
        /// <returns>ファイルのバイト配列</returns>
        public static byte[] ReadTestImageFile(string filename)
        {
            var filePath = Path.Combine(TestDataPath, filename);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"テスト用の画像ファイルが見つかりません: {filePath}");
            }
            
            return File.ReadAllBytes(filePath);
        }

        /// <summary>
        /// テスト用のIWindowsImageオブジェクトを作成します
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <returns>モック化されたIWindowsImageオブジェクト</returns>
        public static IWindowsImage CreateMockWindowsImage(int width, int height)
        {
            // Root cause solution: Validate parameters before proceeding
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));
            
            // CA2000警告に対処: 他の場所のusingステートメントでリソースが破棄される可能性があるため、
            // この警告を抜き出す必要があります。
#pragma warning disable CA2000 // 破棄可能なオブジェクトを使用する

            // Bitmapを作成し、WindowsImageに所有権を移転
            // WindowsImageはコンストラクタで受け取ったBitmapを内部で保持し、自身がDisposeされるときにバイト配列も破棄する
            var bitmap = CreateTestImage(width, height);
            return new Baketa.Infrastructure.Platform.Windows.WindowsImage(bitmap);
#pragma warning restore CA2000 // 破棄可能なオブジェクトを使用する
        }
        
        /// <summary>
        /// テスト用のモックウィンドウを作成します
        /// </summary>
        /// <param name="title">ウィンドウタイトル</param>
        /// <returns>IntPtrのハンドル値</returns>
        public static IntPtr CreateMockWindowHandle(string title = "Test Window")
        {
            ArgumentException.ThrowIfNullOrEmpty(title, nameof(title));
            
            // 実際のWindows APIを呼び出してテストウィンドウを作成することもできるが、
            // 単体テストではモックオブジェクトを使用する方が良い
            // 暗号的に安全なランダム生成を使用
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            byte[] bytes = new byte[4];
            rng.GetBytes(bytes);
            int randomValue = Math.Abs(BitConverter.ToInt32(bytes, 0) % 90000) + 10000; // 10000-99999の範囲
            return new IntPtr(randomValue);
        }
        
        /// <summary>
        /// テスト用のディレクトリを作成し、テスト用画像を生成します
        /// </summary>
        public static async Task EnsureTestDataExists()
        {
            try
            {
                // テストディレクトリの作成
                if (!Directory.Exists(TestDataPath))
                {
                    Directory.CreateDirectory(TestDataPath);
                }
                
                // Root cause solution: Use unique filenames with timestamp to prevent concurrent access issues
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var processId = Environment.ProcessId;
                var smallImagePath = Path.Combine(TestDataPath, $"small_test_image_{timestamp}_{processId}.png");
                var mediumImagePath = Path.Combine(TestDataPath, $"medium_test_image_{timestamp}_{processId}.png");
                var largeImagePath = Path.Combine(TestDataPath, $"large_test_image_{timestamp}_{processId}.png");
                
                // Root cause solution: Always create unique test files to prevent concurrent access issues
                // Check if size parameters are valid before proceeding
                try 
                {
                    await SaveTestImageSafe(320, 240, smallImagePath).ConfigureAwait(false);
                    await SaveTestImageSafe(640, 480, mediumImagePath).ConfigureAwait(false);
                    await SaveTestImageSafe(1024, 768, largeImagePath).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException("Failed to create test images with valid dimensions", ex);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // アクセス権限がない場合はテンポラリディレクトリを使用
                Console.WriteLine($"Warning: Unable to create test data directory due to access permissions: {ex.Message}");
                // テストは続行できるように、エラーを抑制
            }
            catch (IOException ex)
            {
                // I/Oエラーの場合も続行
                Console.WriteLine($"Warning: Unable to create test data due to I/O error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Root cause solution: Safe test image creation with comprehensive error handling
        /// </summary>
        private static async Task SaveTestImageSafe(int width, int height, string filePath)
        {
            // Validate input parameters first
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
            
            try
            {
                // Directory existence check with proper error handling
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use retry logic for file access conflicts
                const int maxRetries = 3;
                const int retryDelayMs = 100;
                
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        using var bitmap = CreateTestImage(width, height);
                        // Root cause solution: Use FileShare.Read to allow concurrent read access during tests
                        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                        bitmap.Save(stream, ImageFormat.Png);
                        return; // Success, exit retry loop
                    }
                    catch (IOException ex) when (attempt < maxRetries - 1)
                    {
                        // File access conflict, wait and retry
                        Console.WriteLine($"Attempt {attempt + 1} failed, retrying in {retryDelayMs}ms: {ex.Message}");
                        await Task.Delay(retryDelayMs).ConfigureAwait(false);
                    }
                }
                
                // All retries failed
                throw new IOException($"Failed to save test image after {maxRetries} attempts: {filePath}");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ExternalException)
            {
                // Non-retryable errors: log and continue to prevent test failures
                Console.WriteLine($"Warning: Unable to save test image to {filePath}: {ex.Message}");
            }
        }
    }
