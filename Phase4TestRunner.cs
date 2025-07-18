using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.OCR.Ensemble;

namespace Baketa.TestRunner;

/// <summary>
/// Phase 4アンサンブルOCRシステムの統合テスト実行用エントリーポイント
/// </summary>
public static class Phase4TestRunner
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Phase 4 アンサンブルOCRシステム統合テスト開始");
        
        try
        {
            // Phase 4の包括的テストを実行
            await Phase4TestApp.RunComprehensiveTestAsync();
            
            // 実画像でのテスト（テスト用画像がある場合）
            // await Phase4TestApp.TestWithRealImageAsync("test_image.png");
            
            Console.WriteLine("✅ Phase 4統合テスト完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Phase 4統合テストでエラーが発生しました: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
            Environment.Exit(1);
        }
    }
}