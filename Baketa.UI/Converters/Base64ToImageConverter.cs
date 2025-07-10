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
        System.Diagnostics.Debug.WriteLine($"🔄 Base64Converter.Convert呼び出し: value={value?.ToString() ?? "null"}");
        
        if (value is null)
        {
            System.Diagnostics.Debug.WriteLine($"❓ Base64変換スキップ: valueがnull");
            return null;
        }
        
        if (value is not string base64String)
        {
            System.Diagnostics.Debug.WriteLine($"❓ Base64変換スキップ: valueが文字列ではない ({value.GetType().Name})");
            return null;
        }
        
        if (string.IsNullOrEmpty(base64String))
        {
            System.Diagnostics.Debug.WriteLine($"❓ Base64変換スキップ: 文字列が空またはnull");
            return null;
        }
        
        if (string.IsNullOrWhiteSpace(base64String))
        {
            System.Diagnostics.Debug.WriteLine($"❓ Base64変換スキップ: 文字列が空白のみ");
            return null;
        }
        
        try
        {
            // Base64文字列の長さと先頭部分をログ出力
            var trimmedString = base64String.Length > 50 ? base64String[..50] + "..." : base64String;
            System.Diagnostics.Debug.WriteLine($"🖼️ Base64変換開始: 長さ={base64String.Length}, 先頭={trimmedString}");
            
            // Base64文字列の妥当性をチェック
            if (base64String.Length % 4 != 0)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Base64文字列の長さが4の倍数ではありません: {base64String.Length}");
            }
            
            var bytes = System.Convert.FromBase64String(base64String);
            System.Diagnostics.Debug.WriteLine($"✅ Base64デコード成功: {bytes.Length}バイト");
            
            if (bytes.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine($"❌ デコード結果が空です");
                return null;
            }
            
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            System.Diagnostics.Debug.WriteLine($"✅ Bitmap作成成功: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            
            return bitmap;
        }
        catch (Exception ex)
        {
            // 詳細なエラー情報をログ出力
            System.Diagnostics.Debug.WriteLine($"❌ Base64変換エラー: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"🔍 Base64文字列: 長さ={base64String.Length}");
            System.Diagnostics.Debug.WriteLine($"🔍 スタックトレース: {ex.StackTrace}");
            
            if (base64String.Length > 0)
            {
                var firstChar = base64String[0];
                var lastChar = base64String[^1];
                System.Diagnostics.Debug.WriteLine($"🔍 文字列詳細: 最初='{firstChar}', 最後='{lastChar}'");
                
                // 無効な文字があるかチェック
                var invalidChars = base64String.Where(c => !IsValidBase64Char(c)).Take(5).ToArray();
                if (invalidChars.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 無効な文字: {string.Join(", ", invalidChars.Select(c => $"'{c}'({(int)c})"))}");
                }
            }
            
            return null;
        }
    }
    
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