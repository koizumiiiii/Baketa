using System;
using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>
/// 座標変換テスト用のスタンドアロンプログラム
/// </summary>
class CoordinateConversionTest
{
    // Win32 API declarations
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect lpRect);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    static void Main(string[] args)
    {
        Console.WriteLine("🎯 座標変換テスト開始");
        
        // テスト用ROI座標（実際のOCR結果を模擬）
        var testRoiBounds = new Rectangle(50, 100, 200, 30);
        
        Console.WriteLine($"🎯 [TEST] 入力ROI座標: {testRoiBounds}");
        
        // 座標変換実行
        var convertedBounds = ConvertRoiToScreenCoordinates(testRoiBounds);
        
        Console.WriteLine($"🎯 [TEST] 変換後画面座標: {convertedBounds}");
        
        // 期待値との比較
        var expectedScaledBounds = new Rectangle(
            (int)(testRoiBounds.X * 4.0f), // スケールファクタ0.25の逆数
            (int)(testRoiBounds.Y * 4.0f),
            (int)(testRoiBounds.Width * 4.0f),
            (int)(testRoiBounds.Height * 4.0f)
        );
        
        Console.WriteLine($"🎯 [EXPECTED] 期待されるスケーリング後座標: {expectedScaledBounds}");
        
        var windowOffset = GetTargetWindowOffset();
        Console.WriteLine($"🎯 [WINDOW] ウィンドウオフセット: {windowOffset}");
        
        var expectedFinalBounds = new Rectangle(
            expectedScaledBounds.X + windowOffset.X,
            expectedScaledBounds.Y + windowOffset.Y,
            expectedScaledBounds.Width,
            expectedScaledBounds.Height
        );
        
        Console.WriteLine($"🎯 [EXPECTED] 期待される最終座標: {expectedFinalBounds}");
        
        // 検証
        bool isCorrect = convertedBounds == expectedFinalBounds;
        Console.WriteLine($"🎯 [RESULT] 座標変換は正しく動作: {isCorrect}");
        
        if (!isCorrect)
        {
            Console.WriteLine($"❌ [ERROR] 座標変換に問題があります");
            Console.WriteLine($"   実際: {convertedBounds}");
            Console.WriteLine($"   期待: {expectedFinalBounds}");
        }
        else
        {
            Console.WriteLine($"✅ [SUCCESS] 座標変換は期待通りに動作しています");
        }
        
        Console.WriteLine("🎯 座標変換テスト完了");
        Console.WriteLine("Enterキーで終了...");
        Console.ReadLine();
    }
    
    private static Rectangle ConvertRoiToScreenCoordinates(Rectangle roiBounds)
    {
        // 🎯 [COORDINATE_TRANSFORM] ROI座標を画面座標に適切に変換
        
        try
        {
            // ROIスケールファクタ（CaptureModels.csのデフォルト値と一致）
            // TODO: 設定から動的に取得するように改善
            const float roiScaleFactor = 0.25f;
            var inverseScale = 1.0f / roiScaleFactor;
            
            // 1. ROI座標を実際の画面座標にスケーリング
            var scaledBounds = new Rectangle(
                (int)(roiBounds.X * inverseScale),
                (int)(roiBounds.Y * inverseScale),
                (int)(roiBounds.Width * inverseScale),
                (int)(roiBounds.Height * inverseScale)
            );
            
            // 2. ターゲットウィンドウのオフセットを取得
            var windowOffset = GetTargetWindowOffset();
            
            // 3. 最終的な画面座標を計算
            var finalBounds = new Rectangle(
                scaledBounds.X + windowOffset.X,
                scaledBounds.Y + windowOffset.Y,
                scaledBounds.Width,
                scaledBounds.Height
            );
            
            // デバッグログ: 座標変換の詳細を出力
            Console.WriteLine($"🎯 [COORDINATE_DEBUG] ROI→画面座標変換:");
            Console.WriteLine($"   入力ROI座標: {roiBounds}");
            Console.WriteLine($"   スケールファクタ: {roiScaleFactor} (逆数: {inverseScale})");
            Console.WriteLine($"   スケーリング後: {scaledBounds}");
            Console.WriteLine($"   ウィンドウオフセット: {windowOffset}");
            Console.WriteLine($"   最終画面座標: {finalBounds}");
            
            return finalBounds;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [COORDINATE_ERROR] 座標変換エラー: {ex.Message}");
            // フォールバック: 元の座標をそのまま返す
            return roiBounds;
        }
    }
    
    private static Point GetTargetWindowOffset()
    {
        try
        {
            // アクティブウィンドウのハンドルを取得
            var activeWindowHandle = GetForegroundWindow();
            
            if (activeWindowHandle != IntPtr.Zero)
            {
                // ウィンドウの矩形情報を取得
                if (GetWindowRect(activeWindowHandle, out var rect))
                {
                    var offset = new Point(rect.Left, rect.Top);
                    Console.WriteLine($"🎯 [WINDOW_OFFSET] アクティブウィンドウオフセット: {offset}");
                    return offset;
                }
            }
            
            Console.WriteLine($"⚠️ [WINDOW_OFFSET] ウィンドウオフセット取得失敗、(0,0)を使用");
            return Point.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [WINDOW_OFFSET_ERROR] ウィンドウオフセット取得エラー: {ex.Message}");
            return Point.Empty;
        }
    }
}