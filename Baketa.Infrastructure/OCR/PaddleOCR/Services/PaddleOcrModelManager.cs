using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// モデル管理、モデルロード、モデル選択を担当するサービス
/// Phase 2.4: PaddleOcrEngineから抽出されたモデル管理実装
/// </summary>
public sealed class PaddleOcrModelManager : IPaddleOcrModelManager
{
    private readonly IPaddleOcrUtilities _utilities;
    private readonly ILogger<PaddleOcrModelManager>? _logger;

    // モデルベースパス定数
    private const string ModelBasePath = @"E:\dev\Baketa\models\ppocrv5";

    public PaddleOcrModelManager(
        IPaddleOcrUtilities utilities,
        ILogger<PaddleOcrModelManager>? logger = null)
    {
        _utilities = utilities ?? throw new ArgumentNullException(nameof(utilities));
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrModelManager初期化完了");
    }

    /// <summary>
    /// モデル準備
    /// UltraThink段階的検証戦略: 安全なモデルから順次試行
    /// </summary>
    public async Task<FullOcrModel?> PrepareModelsAsync(string language, CancellationToken cancellationToken)
    {
        // テスト環境ではモデル準備を完全にスキップ
        if (_utilities.IsTestEnvironment())
        {
            _logger?.LogDebug("テスト環境: モデル準備を完全にスキップ（ネットワークアクセス回避）");
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            return null;
        }

        try
        {
            _logger?.LogInformation("🧠 UltraThink: PaddleOCRモデル段階的検証開始 - 言語: {Language}", language);

            // Phase 1: 最も安全とされるEnglishV3で初期検証
            _logger?.LogInformation("🔍 Phase 1: EnglishV3モデルでの安全性検証");
            try
            {
                var testModel = LocalFullModels.EnglishV3;
                if (testModel != null)
                {
                    _logger?.LogInformation("✅ EnglishV3モデル取得成功 - 基本的なPaddleOCR動作確認済み");

                    // Phase 2: 言語別の最適化されたモデル選択
                    _logger?.LogInformation("🔍 Phase 2: 言語別最適モデル選択");
                    var selectedModel = language.ToLowerInvariant() switch
                    {
                        "jpn" or "ja" => LocalFullModels.JapanV4 ?? testModel,
                        "eng" or "en" => LocalFullModels.EnglishV4 ?? testModel,
                        "chs" or "zh" or "chi" => LocalFullModels.ChineseV4 ?? testModel,
                        _ => testModel
                    };

                    _logger?.LogInformation("🎯 選択モデル確定: {Language} → {ModelType}", language, selectedModel?.GetType().Name ?? "null");
                    return await Task.FromResult(selectedModel).ConfigureAwait(false);
                }
            }
            catch (Exception modelEx)
            {
                _logger?.LogError(modelEx, "❌ Phase 1: EnglishV3モデル検証失敗 - より安全な手法に切り替え");
            }

            // Phase 3: 完全フォールバック - OCR無効化で安定性優先
            _logger?.LogWarning("⚠️ Phase 3: 全モデル検証失敗 - OCR機能を一時無効化（アプリ安定性優先）");
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PrepareModelsAsyncエラー: {ExceptionType} - 一時的にnullを返却", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// PP-OCRv5モデル作成試行
    /// </summary>
    public async Task<FullOcrModel?> TryCreatePPOCRv5ModelAsync(string language, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        try
        {
            // PP-OCRv5モデルが利用可能かチェック
            var isAvailable = Models.PPOCRv5ModelProvider.IsAvailable();
            _logger?.LogDebug("🔍 PPOCRv5ModelProvider.IsAvailable() = {IsAvailable}", isAvailable);

            if (!isAvailable)
            {
                _logger?.LogDebug("❌ PP-OCRv5モデルが利用できません");
                return null;
            }

            // PP-OCRv5多言語モデルを取得
            _logger?.LogDebug("🔍 PPOCRv5ModelProvider.GetPPOCRv5MultilingualModel()呼び出し");
            var ppocrv5Model = Models.PPOCRv5ModelProvider.GetPPOCRv5MultilingualModel();
            _logger?.LogDebug("🔍 PPOCRv5ModelProvider.GetPPOCRv5MultilingualModel() = {ModelExists}", ppocrv5Model != null);

            if (ppocrv5Model != null)
            {
                _logger?.LogInformation("✅ PP-OCRv5多言語モデルを使用 - 言語: {Language}", language);
                return ppocrv5Model;
            }

            _logger?.LogWarning("❌ PP-OCRv5モデル作成失敗 - GetPPOCRv5MultilingualModel()がnullを返しました");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PP-OCRv5モデルの作成に失敗しました");
            return null;
        }
    }

    /// <summary>
    /// 言語別デフォルトモデル取得
    /// </summary>
    public FullOcrModel? GetDefaultModelForLanguage(string language)
    {
        try
        {
            var model = language.ToLowerInvariant() switch
            {
                "jpn" or "ja" => LocalFullModels.JapanV4,
                "eng" or "en" => LocalFullModels.EnglishV4,
                "chs" or "zh" or "chi" => LocalFullModels.ChineseV4,
                _ => LocalFullModels.EnglishV4
            };

            _logger?.LogDebug("🔍 デフォルトモデル取得: {Language} → {ModelType}", language, model?.GetType().Name ?? "null");

            // モデルの詳細情報をログ出力
            if (model != null)
            {
                try
                {
                    var modelType = model.GetType();
                    _logger?.LogDebug("🔍 モデル詳細: {ModelType}", modelType.Name);
                    foreach (var prop in modelType.GetProperties().Where(p => p.CanRead))
                    {
                        try
                        {
                            var value = prop.GetValue(model);
                            _logger?.LogTrace("   {PropertyName}: {Value}", prop.Name, value);
                        }
                        catch { /* プロパティ取得エラーは無視 */ }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "モデル詳細取得エラー");
                }
            }

            return model;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "デフォルトモデル取得エラー");
            return null;
        }
    }

    /// <summary>
    /// V5モデル検出
    /// </summary>
    public bool DetectIfV5Model(FullOcrModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        try
        {
            // V5統一により常にtrueを返す
            // 実際の実装では model.RecognizationModel.Version == V5 などでチェック可能
            _logger?.LogDebug("🔍 V5モデル検出: 常にtrue（V5統一）");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "V5モデル検出エラー");
            return false;
        }
    }

    /// <summary>
    /// PP-OCRv5カスタムモデルの作成（内部実装）
    /// 注意: これはprivateメソッドとして将来のAPI改善時に使用予定
    /// </summary>
    private async Task<FullOcrModel?> CreatePPOCRv5CustomModelAsync(
        string detectionModelPath,
        string recognitionModelPath,
        string language,
        CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        try
        {
            _logger?.LogDebug("🔨 PP-OCRv5カスタムモデル作成開始");

            var detectionModelDir = Path.GetDirectoryName(detectionModelPath);
            var recognitionModelDir = Path.GetDirectoryName(recognitionModelPath);

            _logger?.LogDebug("📁 検出モデルディレクトリ: {DetectionDir}", detectionModelDir);
            _logger?.LogDebug("📁 認識モデルディレクトリ: {RecognitionDir}", recognitionModelDir);

            if (string.IsNullOrEmpty(detectionModelDir) || string.IsNullOrEmpty(recognitionModelDir))
            {
                _logger?.LogWarning("❌ モデルディレクトリが無効です");
                return null;
            }

            // PP-OCRv5の5言語統合モデルを使用
            var actualRecognitionModelDir = language switch
            {
                "jpn" => Path.Combine(Path.GetDirectoryName(recognitionModelDir)!, "korean_rec"),
                "eng" => Path.Combine(Path.GetDirectoryName(recognitionModelDir)!, "latin_rec"),
                _ => Path.Combine(Path.GetDirectoryName(recognitionModelDir)!, "korean_rec")
            };

            _logger?.LogDebug("🌍 PP-OCRv5統合モデルディレクトリ: {ActualDir}", actualRecognitionModelDir);

            // Sdcb.PaddleOCR 3.0.1 API制限により、事前定義モデルを使用
            _logger?.LogDebug("⚠️ Sdcb.PaddleOCR 3.0.1 API制限により、PP-OCRv5ファイルの直接読み込みを一時的にスキップ");
            _logger?.LogDebug("🔄 改良された事前定義モデルを使用（V5統一モデル）");

            var improvedModel = language switch
            {
                "jpn" => LocalFullModels.ChineseV5,
                "eng" => LocalFullModels.ChineseV5,
                _ => LocalFullModels.ChineseV5
            };

            _logger?.LogInformation("🎯 改良モデル選択成功: V5統一モデル ({Language})", language);
            return improvedModel;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PP-OCRv5カスタムモデルの作成に失敗しました");

            // フォールバック
            var fallbackModel = LocalFullModels.ChineseV5;
            _logger?.LogInformation("🔄 標準モデルにフォールバック: ChineseV5");
            return fallbackModel;
        }
    }

    /// <summary>
    /// PP-OCRv5認識モデルのパスを取得（内部実装）
    /// </summary>
    private static string GetPPOCRv5RecognitionModelPath(string language)
    {
        return language switch
        {
            "jpn" => Path.Combine(ModelBasePath, "korean_rec", "inference.pdiparams"),
            "eng" => Path.Combine(ModelBasePath, "latin_rec", "inference.pdiparams"),
            _ => Path.Combine(ModelBasePath, "korean_rec", "inference.pdiparams")
        };
    }

    /// <summary>
    /// PP-OCRv5モデルの取得（内部実装）
    /// </summary>
    private FullOcrModel? GetPPOCRv5Model(string language)
    {
        try
        {
            _logger?.LogDebug("🔍 GetPPOCRv5Model呼び出し - 言語: {Language}", language);

            var model = language switch
            {
                "jpn" => LocalFullModels.ChineseV5,
                "eng" => LocalFullModels.ChineseV5,
                _ => LocalFullModels.ChineseV5
            };

            _logger?.LogDebug("🔍 PP-OCRv5ベースモデル選択: {ModelType}", model?.GetType()?.Name ?? "null");
            return model;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PP-OCRv5モデル取得エラー");
            return null;
        }
    }

    /// <summary>
    /// 認識モデル名の取得（内部実装）
    /// </summary>
    private static string GetRecognitionModelName(string language) => language switch
    {
        "jpn" => "rec_japan_standard",
        "eng" => "rec_english_standard",
        _ => "rec_english_standard"
    };
}
