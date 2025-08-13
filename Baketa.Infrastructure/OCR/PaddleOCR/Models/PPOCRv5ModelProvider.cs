using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Details;
using Sdcb.PaddleOCR.Models.Local;
using System.IO;
using System.Reflection;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Models;

/// <summary>
/// PP-OCRv5モデルプロバイダー
/// 多言語対応（日本語、英語、中国語など）のためのPP-OCRv5モデルを提供
/// </summary>
public static class PPOCRv5ModelProvider
{
    private static readonly string ModelsBasePath = GetModelsBasePath();
    
    private static string GetModelsBasePath()
    {
        // 実行ファイルの場所からmodelsフォルダを探す
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var baseDir = Path.GetDirectoryName(assemblyLocation) ?? "";
        
        // 開発環境とリリース環境の両方に対応
        var possiblePaths = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "models", "pp-ocrv5"),
            Path.Combine(baseDir, "models", "pp-ocrv5"),
            Path.Combine(Environment.CurrentDirectory, "models", "pp-ocrv5"),
        };
        
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }
        
        // デフォルトパス
        return Path.Combine(baseDir, "models", "pp-ocrv5");
    }
    
    /// <summary>
    /// PP-OCRv5多言語モデルを取得
    /// </summary>
    /// <returns>PP-OCRv5モデル、利用不可の場合はnull</returns>
    public static FullOcrModel? GetPPOCRv5MultilingualModel()
    {
        try
        {
            DebugLogUtility.WriteLog("🔍 GetPPOCRv5MultilingualModel開始");
            DebugLogUtility.WriteLog($"🔍 ModelsBasePath = {ModelsBasePath}");
            
            var detModelPath = Path.Combine(ModelsBasePath, "PP-OCRv5_server_det");
            var recModelPath = Path.Combine(ModelsBasePath, "PP-OCRv5_server_rec");
            var clsModelPath = Path.Combine(ModelsBasePath, "ch_ppocr_mobile_v2.0_cls_infer");
            
            DebugLogUtility.WriteLog($"🔍 検出モデルパス: {detModelPath}");
            DebugLogUtility.WriteLog($"🔍 認識モデルパス: {recModelPath}");
            DebugLogUtility.WriteLog($"🔍 分類モデルパス: {clsModelPath}");
            
            // モデルファイルの存在確認
            var detExists = Directory.Exists(detModelPath);
            var recExists = Directory.Exists(recModelPath);
            var clsExists = Directory.Exists(clsModelPath);
            
            DebugLogUtility.WriteLog($"🔍 検出モデル存在: {detExists}");
            DebugLogUtility.WriteLog($"🔍 認識モデル存在: {recExists}");
            DebugLogUtility.WriteLog($"🔍 分類モデル存在: {clsExists}");
            
            if (!detExists || !recExists || !clsExists)
            {
                DebugLogUtility.WriteLog("❌ PP-OCRv5必要ファイルが不足しています");
                return null;
            }
            
            // PP-OCRv5モデルを構築 - 安全な実装戦略
            DebugLogUtility.WriteLog("🔍 LocalFullModels.ChineseV5取得試行");
            var chineseV5 = LocalFullModels.ChineseV5;
            DebugLogUtility.WriteLog($"🔍 LocalFullModels.ChineseV5 = {chineseV5 != null}");
            
            if (chineseV5 != null)
            {
                DebugLogUtility.WriteLog("✅ PP-OCRv5 (多言語対応モデル) を使用 - 保守的設定で安定化");
            }
            
            return chineseV5;
        }
        catch (Exception ex)
        {
            // モデル読み込みエラー
            DebugLogUtility.WriteLog($"❌ PP-OCRv5モデル読み込みエラー: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"PP-OCRv5モデル読み込みエラー: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// PP-OCRv5モデルが利用可能かチェック
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            DebugLogUtility.WriteLog("🔍 IsAvailable開始");
            
            // LocalFullModels.ChineseV5が利用可能かチェック
            var chineseV5 = LocalFullModels.ChineseV5;
            var result = chineseV5 != null;
            
            DebugLogUtility.WriteLog($"🔍 LocalFullModels.ChineseV5 = {chineseV5 != null}");
            DebugLogUtility.WriteLog($"🔍 IsAvailable結果 = {result}");
            
            return result;
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ IsAvailable例外: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}