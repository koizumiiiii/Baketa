using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Converters;

/// <summary>
/// Base64エンコードされた画像文字列をBitmapに変換するコンバーター
/// </summary>
public class Base64ToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            // Base64文字列が空やnullの場合はフォールバック画像を返す
            if (value is not string base64String || string.IsNullOrWhiteSpace(base64String))
            {
                return CreateFallbackImage();
            }

            // Base64文字列の基本的な検証
            if (!IsValidBase64String(base64String))
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Base64文字列が無効: 長さ={base64String.Length}");
                return CreateFallbackImage();
            }

            // Base64デコードと画像変換を安全に実行
            var imageBytes = System.Convert.FromBase64String(base64String);
            using var stream = new MemoryStream(imageBytes);
            return new Bitmap(stream);
        }
        catch (FormatException ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Base64フォーマットエラー: {ex.Message}");
            return CreateFallbackImage();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ 画像変換エラー: {ex.Message}");
            return CreateFallbackImage();
        }
    }

    /// <summary>
    /// フォールバック用のプレースホルダー画像を作成
    /// </summary>
    private static WriteableBitmap CreateFallbackImage()
    {
        // 64x64のグレープレースホルダー画像を作成
        var bitmap = new WriteableBitmap(new Avalonia.PixelSize(64, 64), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        
        using var lockedFrameBuffer = bitmap.Lock();
        var ptr = lockedFrameBuffer.Address;
        var stride = lockedFrameBuffer.RowBytes;
        
        unsafe
        {
            byte* buffer = (byte*)ptr.ToPointer();
            
            // グレー色(128, 128, 128, 255)で全体を塗りつぶす
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int offset = y * stride + x * 4;
                    buffer[offset] = 128;     // Blue
                    buffer[offset + 1] = 128; // Green
                    buffer[offset + 2] = 128; // Red
                    buffer[offset + 3] = 255; // Alpha
                }
            }
        }
        
        return bitmap;
    }

    /// <summary>
    /// Base64文字列の基本的な検証
    /// </summary>
    private static bool IsValidBase64String(string base64String)
    {
        // 空文字列チェック
        if (string.IsNullOrWhiteSpace(base64String))
            return false;
            
        // 長さチェック（4の倍数であるべき）
        if (base64String.Length % 4 != 0)
            return false;
            
        // 有効な文字のみかどうかチェック
        return base64String.All(IsValidBase64Char);
    }
    
    /// <summary>
    /// Base64文字で有効な文字かどうかチェック
    /// </summary>
    private static bool IsValidBase64Char(char c)
    {
        return (c >= 'A' && c <= 'Z') || 
               (c >= 'a' && c <= 'z') || 
               (c >= '0' && c <= '9') || 
               c == '+' || c == '/' || c == '=';
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported");
    }
}