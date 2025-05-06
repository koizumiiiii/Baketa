using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Tests.Helpers
{
    /// <summary>
    /// テスト用のサンプル画像を生成するヘルパークラス
    /// </summary>
    public static class TestImageGenerator
    {
        /// <summary>
        /// テスト用の単色の有効なWindowsImageを生成します
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="color">色（省略時は白）</param>
        /// <returns>生成された画像</returns>
        public static IWindowsImage CreateValidWindowsImage(int width = 100, int height = 100, Color? color = null)
        {
            if (width <= 0 || height <= 0)
            {
                width = Math.Max(1, width);
                height = Math.Max(1, height);
            }
            
            color ??= Color.White;
            
            // CA2000警告に対応およびNull許容参照型の警告対応
            Bitmap? tempBitmap = null;
            Bitmap? persistentBitmap = null;
            
            try
            {
                // 新しいビットマップを作成
                tempBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                
                // 指定された色で塗りつぶし
                using var g = Graphics.FromImage(tempBitmap);
                g.Clear(color.Value);
                
                // テスト用のパターンを描画（サンプル画像をより高品質に）
                using var pen = new Pen(Color.Black, 2);
                g.DrawRectangle(pen, 5, 5, width - 10, height - 10);
                
                // 小さなテキストを描画
                if (width >= 60 && height >= 20)
                {
                    using var font = new Font("Arial", 8);
                    g.DrawString("Test", font, Brushes.Black, width / 2 - 10, height / 2 - 8);
                }
                
                // WindowsImageに渡すための永続化されたBitmapを作成
                persistentBitmap = new Bitmap(tempBitmap);
                
                // 所有権はここでWindowsImageに移転される
                var windowsImage = new WindowsImage(persistentBitmap);
                persistentBitmap = null; // 所有権移転後は参照をクリア
                
                return windowsImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: 有効な画像の生成中にエラーが発生しました: {ex.Message}");
                throw;
            }
            finally
            {
                // リソースの確実な解放
                tempBitmap?.Dispose();
                persistentBitmap?.Dispose(); // 所有権移転が発生しなかった場合のみ破棄される
            }
        }
        
        /// <summary>
        /// テスト用の単色の有効なバイト配列画像データを生成します
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="color">色（省略時は白）</param>
        /// <returns>生成されたPNG形式の画像データ</returns>
        public static byte[] CreateValidImageBytes(int width = 100, int height = 100, Color? color = null)
        {
            // 別の方法で直接バイト配列を生成する
            byte[] result;
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                // 指定された色で塗りつぶし
                using var g = Graphics.FromImage(bitmap);
                color ??= Color.White;
                g.Clear(color.Value);
                
                // テスト用のパターンを描画
                using var pen = new Pen(Color.Black, 2);
                g.DrawRectangle(pen, 5, 5, width - 10, height - 10);
                
                // メモリストリームに保存（簡素化されたusing文を使用）
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                result = ms.ToArray();
            }
            
            return result;
        }
        
        /// <summary>
        /// テスト用のグラデーション画像を生成します
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <returns>生成されたWindowsImage</returns>
        public static IWindowsImage CreateGradientImage(int width = 100, int height = 100)
        {
            if (width <= 0 || height <= 0)
            {
                width = Math.Max(1, width);
                height = Math.Max(1, height);
            }
            
            // CA2000警告に対応した実装
            Bitmap? tempBitmap = null;
            Bitmap? persistentBitmap = null;
            
            try
            {
                // 新しいビットマップを作成
                tempBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                
                // グラデーションで塗りつぶし
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int r = (int)(255 * x / (float)width);
                        int g = (int)(255 * y / (float)height);
                        int b = (int)(255 * (x + y) / (float)(width + height));
                        
                        tempBitmap.SetPixel(x, y, Color.FromArgb(255, r, g, b));
                    }
                }
                
                // 永続的なビットマップを作成
                persistentBitmap = new Bitmap(tempBitmap);
                
                // WindowsImageに所有権を移転
                var windowsImage = new WindowsImage(persistentBitmap);
                persistentBitmap = null; // 所有権移転後は参照をクリア
                
                return windowsImage;
            }
            catch (Exception ex)
            {
                // 例外ログを追加
                System.Diagnostics.Debug.WriteLine($"ERROR: グラデーション画像の生成中にエラーが発生しました: {ex.Message}");
                throw; // 例外を上位に伝搬
            }
            finally
            {
                // リソースの確実な解放
                tempBitmap?.Dispose();
                persistentBitmap?.Dispose(); // 所有権移転が発生しなかった場合のみ破棄される
            }
        }
        
        /// <summary>
        /// テスト用のパターン画像を生成します（チェッカーボード）
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <param name="cellSize">セルのサイズ</param>
        /// <returns>生成されたWindowsImage</returns>
        public static IWindowsImage CreateCheckerboardImage(int width = 100, int height = 100, int cellSize = 10)
        {
            if (width <= 0 || height <= 0)
            {
                width = Math.Max(1, width);
                height = Math.Max(1, height);
            }
            
            if (cellSize <= 0)
            {
                cellSize = 10;
            }
            
            // CA2000警告に対応
            Bitmap? tempBitmap = null;
            Bitmap? persistentBitmap = null;
            
            try
            {
                // 新しいビットマップを作成
                tempBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                
                // チェッカーボードで塗りつぶし
                using var g = Graphics.FromImage(tempBitmap);
                g.Clear(Color.White);
                
                using var brush = new SolidBrush(Color.Black);
                
                for (int y = 0; y < height; y += cellSize)
                {
                    for (int x = 0; x < width; x += cellSize)
                    {
                        if ((x / cellSize + y / cellSize) % 2 == 0)
                        {
                            g.FillRectangle(brush, x, y, 
                                Math.Min(cellSize, width - x), 
                                Math.Min(cellSize, height - y));
                        }
                    }
                }
                
                // 永続的なビットマップを作成
                persistentBitmap = new Bitmap(tempBitmap);
                
                // WindowsImageに所有権を移転
                var windowsImage = new WindowsImage(persistentBitmap);
                persistentBitmap = null; // 所有権移転後は参照をクリア
                
                return windowsImage;
            }
            catch (Exception ex)
            {
                // 例外ログを追加
                System.Diagnostics.Debug.WriteLine($"ERROR: チェッカーボード画像の生成中にエラーが発生しました: {ex.Message}");
                throw; // WindowsImageコンストラクタが例外をスローした場合、上位に伝搬
            }
            finally
            {
                // リソースの確実な解放
                tempBitmap?.Dispose();
                persistentBitmap?.Dispose(); // 所有権移転が発生しなかった場合のみ破棄される
            }
        }
        
        /// <summary>
        /// CA2000警告に対応した最適化バージョンのテキスト画像生成
        /// </summary>
        /// <param name="text">描画するテキスト</param>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <returns>生成されたWindowsImage</returns>
        public static IWindowsImage CreateTextImage(string text, int width = 200, int height = 100)
        {
            if (width <= 0 || height <= 0)
            {
                width = Math.Max(1, width);
                height = Math.Max(1, height);
            }
            
            if (string.IsNullOrEmpty(text))
            {
                text = "Test Text";
            }
            
            // CA2000警告に対応：明示的にリソース管理を行う
            Bitmap? tempBitmap = null;
            Bitmap? persistentBitmap = null;
            
            try
            {
                // 新しいビットマップを作成
                tempBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                
                // テキストを描画
                using var g = Graphics.FromImage(tempBitmap);
                g.Clear(Color.White);
                
                using var font = new Font("Arial", 12);
                using var brush = new SolidBrush(Color.Black);
                
                g.DrawString(text, font, brush, 10, 10);
                
                // 永続的なビットマップを作成
                persistentBitmap = new Bitmap(tempBitmap);
                
                // WindowsImageに所有権を移転
                var windowsImage = new WindowsImage(persistentBitmap);
                persistentBitmap = null; // 所有権移転後は参照をクリア
                
                return windowsImage;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                // 特定の例外タイプを捕捉してCA1031にも対応
                System.Diagnostics.Debug.WriteLine($"ERROR: テキスト画像の生成中にエラーが発生しました: {ex.Message}");
                throw; // 元の例外を再スロー
            }
            finally
            {
                // リソースの確実な解放
                tempBitmap?.Dispose();
                persistentBitmap?.Dispose(); // 所有権移転が発生しなかった場合のみ破棄される
            }
        }
    }
}