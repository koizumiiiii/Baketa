namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// ONNX モデル設定の抽象化
/// テンソル名の外部設定化とモデル汎用化対応
/// Issue #143 Week 2 Phase 2: モデル出力テンソル名外部化
/// </summary>
public interface IOnnxModelConfiguration
{
    /// <summary>
    /// テキスト検出モデル設定を取得
    /// </summary>
    /// <returns>検出モデル設定</returns>
    OnnxModelInfo GetDetectionModelInfo();
    
    /// <summary>
    /// テキスト認識モデル設定を取得
    /// </summary>
    /// <returns>認識モデル設定</returns>
    OnnxModelInfo GetRecognitionModelInfo();
    
    /// <summary>
    /// 言語識別モデル設定を取得
    /// </summary>
    /// <returns>言語識別モデル設定</returns>
    OnnxModelInfo GetLanguageIdentificationModelInfo();
    
    /// <summary>
    /// カスタムモデル設定を取得
    /// </summary>
    /// <param name="modelName">モデル名</param>
    /// <returns>カスタムモデル設定</returns>
    OnnxModelInfo? GetCustomModelInfo(string modelName);
    
    /// <summary>
    /// 利用可能なモデル一覧を取得
    /// </summary>
    /// <returns>モデル名一覧</returns>
    IReadOnlyList<string> GetAvailableModels();
    
    /// <summary>
    /// モデル設定を動的に更新
    /// </summary>
    /// <param name="modelName">モデル名</param>
    /// <param name="modelInfo">新しいモデル設定</param>
    void UpdateModelInfo(string modelName, OnnxModelInfo modelInfo);
    
    /// <summary>
    /// 設定の妥当性を検証
    /// </summary>
    /// <param name="modelName">検証対象モデル名</param>
    /// <returns>検証結果</returns>
    ModelValidationResult ValidateModel(string modelName);
}

/// <summary>
/// ONNX モデル情報
/// </summary>
public class OnnxModelInfo
{
    /// <summary>
    /// モデルファイルパス
    /// </summary>
    public string ModelPath { get; init; } = string.Empty;
    
    /// <summary>
    /// 入力テンソル名一覧
    /// </summary>
    public List<string> InputTensorNames { get; init; } = [];
    
    /// <summary>
    /// 出力テンソル名一覧
    /// </summary>
    public List<string> OutputTensorNames { get; init; } = [];
    
    /// <summary>
    /// 入力テンソル形状定義
    /// </summary>
    public Dictionary<string, int[]> InputShapes { get; init; } = new();
    
    /// <summary>
    /// 出力テンソル形状定義
    /// </summary>
    public Dictionary<string, int[]> OutputShapes { get; init; } = new();
    
    /// <summary>
    /// モデルメタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    /// <summary>
    /// 推奨バッチサイズ
    /// </summary>
    public int RecommendedBatchSize { get; init; } = 1;
    
    /// <summary>
    /// 推定メモリ使用量（MB）
    /// </summary>
    public long EstimatedMemoryUsageMB { get; init; } = 100;
    
    /// <summary>
    /// モデル形式バージョン
    /// </summary>
    public string ModelVersion { get; init; } = "1.0";
    
    /// <summary>
    /// 前処理設定
    /// </summary>
    public PreprocessingConfig? PreprocessingConfig { get; init; }
    
    /// <summary>
    /// 後処理設定
    /// </summary>
    public PostprocessingConfig? PostprocessingConfig { get; init; }
}

/// <summary>
/// 前処理設定
/// </summary>
public class PreprocessingConfig
{
    /// <summary>
    /// 正規化の有効化
    /// </summary>
    public bool EnableNormalization { get; init; } = true;
    
    /// <summary>
    /// 正規化平均値
    /// </summary>
    public float[] NormalizationMean { get; init; } = [0.485f, 0.456f, 0.406f];
    
    /// <summary>
    /// 正規化標準偏差
    /// </summary>
    public float[] NormalizationStd { get; init; } = [0.229f, 0.224f, 0.225f];
    
    /// <summary>
    /// リサイズ設定
    /// </summary>
    public ResizeConfig? ResizeConfig { get; init; }
    
    /// <summary>
    /// カラー空間変換
    /// </summary>
    public string ColorSpace { get; init; } = "RGB";
}

/// <summary>
/// リサイズ設定
/// </summary>
public class ResizeConfig
{
    /// <summary>
    /// 目標幅
    /// </summary>
    public int TargetWidth { get; init; } = 224;
    
    /// <summary>
    /// 目標高さ
    /// </summary>
    public int TargetHeight { get; init; } = 224;
    
    /// <summary>
    /// アスペクト比維持
    /// </summary>
    public bool MaintainAspectRatio { get; init; } = true;
    
    /// <summary>
    /// 補間方法
    /// </summary>
    public string InterpolationMethod { get; init; } = "Bilinear";
}

/// <summary>
/// 後処理設定
/// </summary>
public class PostprocessingConfig
{
    /// <summary>
    /// 信頼度閾値
    /// </summary>
    public float ConfidenceThreshold { get; init; } = 0.5f;
    
    /// <summary>
    /// NMS（Non-Maximum Suppression）閾値
    /// </summary>
    public float NmsThreshold { get; init; } = 0.4f;
    
    /// <summary>
    /// 最大検出数
    /// </summary>
    public int MaxDetections { get; init; } = 100;
    
    /// <summary>
    /// カスタム後処理パラメータ
    /// </summary>
    public Dictionary<string, object> CustomParameters { get; init; } = new();
}

/// <summary>
/// モデル検証結果
/// </summary>
public class ModelValidationResult
{
    /// <summary>
    /// 検証成功かどうか
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// 検証エラー一覧
    /// </summary>
    public List<string> ValidationErrors { get; init; } = [];
    
    /// <summary>
    /// 検証警告一覧
    /// </summary>
    public List<string> ValidationWarnings { get; init; } = [];
    
    /// <summary>
    /// 検証詳細情報
    /// </summary>
    public Dictionary<string, object> ValidationDetails { get; init; } = new();
    
    /// <summary>
    /// 検証実行時刻
    /// </summary>
    public DateTime ValidationTimestamp { get; init; } = DateTime.UtcNow;
}