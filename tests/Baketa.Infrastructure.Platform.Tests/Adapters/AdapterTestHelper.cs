using System;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Tests.Adapters
{
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
            // CA2000警告に対処: 他の場所のusingステートメントでリソースが破棄される可能性があるため、
            // この警告を抜き出す必要があります。
#pragma warning disable CA2000 // 破棄可能なオブジェクトを使用する
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("幅と高さは正の値である必要があります", nameof(width));
            }

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
        public static void EnsureTestDataExists()
        {
            ArgumentException.ThrowIfNullOrEmpty(TestDataPath, nameof(TestDataPath));
            
            if (!Directory.Exists(TestDataPath))
            {
                Directory.CreateDirectory(TestDataPath);
            }
            
            var smallImagePath = Path.Combine(TestDataPath, "small_test_image.png");
            var mediumImagePath = Path.Combine(TestDataPath, "medium_test_image.png");
            var largeImagePath = Path.Combine(TestDataPath, "large_test_image.png");
            
            if (!File.Exists(smallImagePath))
            {
                SaveTestImage(320, 240, smallImagePath);
            }
            
            if (!File.Exists(mediumImagePath))
            {
                SaveTestImage(1024, 768, mediumImagePath);
            }
            
            if (!File.Exists(largeImagePath))
            {
                SaveTestImage(1920, 1080, largeImagePath);
            }
        }
        
        /// <summary>
        /// テスト画像を生成して指定パスに保存します
        /// </summary>
        private static void SaveTestImage(int width, int height, string filePath)
        {
            // CA2000警告を解決するため、usingステートメントでリソースを確実に破棄
            using var bitmap = CreateTestImage(width, height);
            bitmap.Save(filePath, ImageFormat.Png);
        }
    }
}