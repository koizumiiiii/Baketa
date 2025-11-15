using System;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;

namespace Baketa.Emergency;

/// <summary>
/// 緊急PaddleOCR失敗カウンターリセットユーティリティ
/// </summary>
public static class EmergencyPaddleOcrReset
{
    /// <summary>
    /// 実行中のPaddleOCRエンジンプールの失敗カウンターをリセット
    /// </summary>
    public static void ResetAllFailureCounters()
    {
        try
        {
            Console.WriteLine("🚨 [EMERGENCY] PaddleOCR失敗カウンターリセット開始");
            
            // PooledOcrServiceのプール内の全PaddleOcrEngineインスタンスをリセット
            // この実装は簡易版 - 実際のプールアクセスは複雑
            
            Console.WriteLine("⚠️ この機能は開発中 - 手動でPaddleOcrEngine.ResetFailureCounter()を呼び出してください");
            Console.WriteLine("💡 推奨: アプリケーション再起動によるクリーンリセット");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ リセット中にエラー: {ex.Message}");
        }
    }
}