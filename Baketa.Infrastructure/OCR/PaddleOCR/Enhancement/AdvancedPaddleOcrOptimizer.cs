using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Enhancement;

/// <summary>
/// PaddleOCRの高度な最適化パラメータを管理するクラス
/// </summary>
public class AdvancedPaddleOcrOptimizer(ILogger<AdvancedPaddleOcrOptimizer> logger)
{
    private readonly ILogger<AdvancedPaddleOcrOptimizer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 小さい文字に最適化されたパラメータを適用
    /// </summary>
    public void ApplySmallTextOptimization(PaddleOcrAll ocrEngine)
    {
        _logger.LogInformation("小さい文字最適化パラメータを適用中...");

        var parameters = new Dictionary<string, object>
        {
            // 検出関連パラメータ
            { "det_db_thresh", 0.2f },              // 検出閾値を上げて小さい文字を検出
            { "det_db_box_thresh", 0.4f },          // ボックス閾値を下げて小さい領域を検出
            { "det_db_unclip_ratio", 2.5f },        // アンクリップ比率を上げて小さい文字を拡張
            { "det_db_score_mode", "slow" },        // 精度重視モード
            { "det_limit_side_len", 1280 },         // 検出時の最大辺長を調整
            { "det_limit_type", "max" },            // 制限タイプ
            
            // 認識関連パラメータ
            { "rec_image_shape", "3, 48, 320" },    // 認識画像サイズを調整
            { "rec_batch_num", 1 },                 // バッチサイズを1に（精度重視）
            { "rec_char_dict_path", "" },           // 文字辞書パス
            { "max_text_length", 50 },              // 最大テキスト長
            
            // パフォーマンス関連
            { "use_gpu", false },                   // CPU使用
            { "use_tensorrt", false },              // TensorRT無効
            { "use_fp16", false },                  // FP16無効（精度重視）
            { "gpu_mem", 500 },                     // GPU メモリ
            { "cpu_math_library_num_threads", 4 },  // CPU スレッド数
        };

        ApplyParameters(ocrEngine, parameters);
    }

    /// <summary>
    /// 高精度処理に最適化されたパラメータを適用
    /// </summary>
    public void ApplyHighPrecisionOptimization(PaddleOcrAll ocrEngine)
    {
        _logger.LogInformation("高精度最適化パラメータを適用中...");

        var parameters = new Dictionary<string, object>
        {
            // 検出関連パラメータ（高精度）
            { "det_db_thresh", 0.1f },              // 検出閾値を下げて多くの候補を検出
            { "det_db_box_thresh", 0.3f },          // ボックス閾値を下げて多くの領域を検出
            { "det_db_unclip_ratio", 2.0f },        // アンクリップ比率を最適化
            { "det_db_score_mode", "slow" },        // 精度重視モード
            { "det_limit_side_len", 1600 },         // 検出時の最大辺長を拡大
            { "det_east_score_thresh", 0.8f },      // EAST スコア閾値
            { "det_east_cover_thresh", 0.1f },      // EAST カバー閾値
            { "det_east_nms_thresh", 0.2f },        // EAST NMS閾値
            
            // 認識関連パラメータ（高精度）
            { "rec_image_shape", "3, 64, 256" },    // 認識画像サイズを拡大
            { "rec_batch_num", 1 },                 // バッチサイズを1に（精度重視）
            { "use_space_char", true },             // スペース文字を使用
            { "drop_score", 0.01f },                // ドロップスコアを下げて多くの候補を保持
            
            // 後処理パラメータ
            { "use_angle_cls", false },             // 角度分類を無効（PP-OCRv5では非推奨）
            { "cls_thresh", 0.9f },                 // 分類閾値
            { "cls_batch_num", 1 },                 // 分類バッチサイズ
        };

        ApplyParameters(ocrEngine, parameters);
    }

    /// <summary>
    /// 高速処理に最適化されたパラメータを適用
    /// </summary>
    public void ApplyFastProcessingOptimization(PaddleOcrAll ocrEngine)
    {
        _logger.LogInformation("高速処理最適化パラメータを適用中...");

        var parameters = new Dictionary<string, object>
        {
            // 検出関連パラメータ（高速）
            { "det_db_thresh", 0.3f },              // 検出閾値を上げて処理を高速化
            { "det_db_box_thresh", 0.6f },          // ボックス閾値を上げて処理を高速化
            { "det_db_unclip_ratio", 1.5f },        // アンクリップ比率を下げて処理を高速化
            { "det_db_score_mode", "fast" },        // 高速モード
            { "det_limit_side_len", 960 },          // 検出時の最大辺長を縮小
            
            // 認識関連パラメータ（高速）
            { "rec_image_shape", "3, 32, 128" },    // 認識画像サイズを縮小
            { "rec_batch_num", 6 },                 // バッチサイズを増加（高速化）
            { "drop_score", 0.1f },                 // ドロップスコアを上げて処理を高速化
            
            // パフォーマンス関連
            { "use_mkldnn", true },                 // MKLDNN使用（CPU高速化）
            { "mkldnn_cache_capacity", 10 },        // MKLDNN キャッシュ容量
            { "cpu_math_library_num_threads", 8 },  // CPU スレッド数を増加
        };

        ApplyParameters(ocrEngine, parameters);
    }

    /// <summary>
    /// 日本語特化の最適化パラメータを適用
    /// </summary>
    public void ApplyJapaneseOptimization(PaddleOcrAll ocrEngine)
    {
        _logger.LogInformation("日本語特化最適化パラメータを適用中...");

        var parameters = new Dictionary<string, object>
        {
            // 日本語固有パラメータ
            { "rec_char_dict_path", "" },           // 日本語文字辞書（空文字列で多言語対応）
            { "use_space_char", true },             // スペース文字を使用（日本語でも重要）
            { "lang", "japan" },                    // 言語設定
            { "det_limit_type", "max" },            // 制限タイプ
            
            // 日本語テキスト検出に最適化
            { "det_db_thresh", 0.15f },             // 日本語文字検出に最適化
            { "det_db_box_thresh", 0.35f },         // 日本語ボックス検出に最適化
            { "det_db_unclip_ratio", 2.2f },        // 日本語文字の拡張比率
            { "det_limit_side_len", 1440 },         // 日本語テキスト用の最大辺長
            
            // 日本語認識に最適化
            { "rec_image_shape", "3, 48, 320" },    // 日本語認識に最適な画像サイズ
            { "max_text_length", 80 },              // 日本語テキストの最大長
            { "rec_batch_num", 1 },                 // 日本語認識でのバッチサイズ
            
            // 後処理設定
            { "drop_score", 0.05f },                // 日本語の信頼度閾値
            { "use_angle_cls", false },             // 角度分類無効
        };

        ApplyParameters(ocrEngine, parameters);
    }

    /// <summary>
    /// カスタムパラメータセットを適用
    /// </summary>
    public void ApplyCustomOptimization(PaddleOcrAll ocrEngine, Dictionary<string, object> customParameters)
    {
        _logger.LogInformation("カスタム最適化パラメータを適用中: {Count}個のパラメータ", customParameters.Count);
        ApplyParameters(ocrEngine, customParameters);
    }

    /// <summary>
    /// リフレクションを使用してパラメータを適用
    /// </summary>
    private void ApplyParameters(PaddleOcrAll ocrEngine, Dictionary<string, object> parameters)
    {
        var engineType = ocrEngine.GetType();
        var appliedCount = 0;

        foreach (var parameter in parameters)
        {
            try
            {
                // プロパティの検索
                var property = engineType.GetProperty(parameter.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property != null && property.CanWrite)
                {
                    // 型変換
                    var convertedValue = ConvertValue(parameter.Value, property.PropertyType);
                    property.SetValue(ocrEngine, convertedValue);
                    appliedCount++;
                    _logger.LogDebug("パラメータ適用成功: {Key} = {Value}", parameter.Key, parameter.Value);
                }
                else
                {
                    // フィールドの検索
                    var field = engineType.GetField(parameter.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (field != null)
                    {
                        var convertedValue = ConvertValue(parameter.Value, field.FieldType);
                        field.SetValue(ocrEngine, convertedValue);
                        appliedCount++;
                        _logger.LogDebug("フィールド適用成功: {Key} = {Value}", parameter.Key, parameter.Value);
                    }
                    else
                    {
                        _logger.LogWarning("パラメータが見つかりません: {Key}", parameter.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "パラメータ適用エラー: {Key} = {Value}", parameter.Key, parameter.Value);
            }
        }

        _logger.LogInformation("パラメータ適用完了: {Applied}/{Total}個のパラメータを適用", appliedCount, parameters.Count);
    }

    /// <summary>
    /// 値を指定された型に変換
    /// </summary>
    private object? ConvertValue(object value, Type targetType)
    {
        if (value == null) return null;

        if (targetType == typeof(string))
            return value.ToString();

        if (targetType == typeof(bool))
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);

        if (targetType == typeof(int))
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);

        if (targetType == typeof(float))
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);

        if (targetType == typeof(double))
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
