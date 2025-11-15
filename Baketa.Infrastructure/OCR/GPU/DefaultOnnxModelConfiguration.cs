using System.Collections.Concurrent;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// デフォルト ONNX モデル設定実装
/// テンソル名外部化と動的モデル管理
/// Issue #143 Week 2 Phase 2: モデル汎用化対応
/// </summary>
public sealed class DefaultOnnxModelConfiguration : IOnnxModelConfiguration
{
    private readonly ILogger<DefaultOnnxModelConfiguration> _logger;
    private readonly OcrSettings _ocrSettings;
    private readonly ConcurrentDictionary<string, OnnxModelInfo> _modelCache = new();
    private readonly object _configLock = new();

    public DefaultOnnxModelConfiguration(
        ILogger<DefaultOnnxModelConfiguration> logger,
        IOptions<OcrSettings> ocrSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrSettings = ocrSettings?.Value ?? throw new ArgumentNullException(nameof(ocrSettings));

        InitializeDefaultModels();
        _logger.LogInformation("🔧 DefaultOnnxModelConfiguration初期化完了 - モデル外部化対応");
    }

    public OnnxModelInfo GetDetectionModelInfo()
    {
        return GetOrCreateModelInfo("TextDetection", () => CreateDetectionModelInfo());
    }

    public OnnxModelInfo GetRecognitionModelInfo()
    {
        return GetOrCreateModelInfo("TextRecognition", () => CreateRecognitionModelInfo());
    }

    public OnnxModelInfo GetLanguageIdentificationModelInfo()
    {
        return GetOrCreateModelInfo("LanguageIdentification", () => CreateLanguageIdentificationModelInfo());
    }

    public OnnxModelInfo? GetCustomModelInfo(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            _logger.LogWarning("モデル名が指定されていません");
            return null;
        }

        return _modelCache.GetValueOrDefault(modelName);
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        return _modelCache.Keys.ToList().AsReadOnly();
    }

    public void UpdateModelInfo(string modelName, OnnxModelInfo modelInfo)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            throw new ArgumentException("モデル名が指定されていません", nameof(modelName));
        }

        ArgumentNullException.ThrowIfNull(modelInfo);

        lock (_configLock)
        {
            _modelCache.AddOrUpdate(modelName, modelInfo, (_, _) => modelInfo);
            _logger.LogInformation("📝 モデル設定更新: {ModelName}", modelName);
        }
    }

    public ModelValidationResult ValidateModel(string modelName)
    {
        try
        {
            _logger.LogDebug("🔍 モデル検証開始: {ModelName}", modelName);

            var modelInfo = GetCustomModelInfo(modelName);
            if (modelInfo == null)
            {
                return new ModelValidationResult
                {
                    IsValid = false,
                    ValidationErrors = [$"モデル '{modelName}' が見つかりません"]
                };
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var details = new Dictionary<string, object>();

            // モデルファイル存在チェック
            if (!System.IO.File.Exists(modelInfo.ModelPath))
            {
                errors.Add($"モデルファイルが存在しません: {modelInfo.ModelPath}");
            }
            else
            {
                details["ModelFileSize"] = new System.IO.FileInfo(modelInfo.ModelPath).Length;
            }

            // テンソル名検証
            if (modelInfo.InputTensorNames.Count == 0)
            {
                warnings.Add("入力テンソル名が定義されていません");
            }

            if (modelInfo.OutputTensorNames.Count == 0)
            {
                warnings.Add("出力テンソル名が定義されていません");
            }

            // 形状定義検証
            foreach (var inputName in modelInfo.InputTensorNames)
            {
                if (!modelInfo.InputShapes.ContainsKey(inputName))
                {
                    warnings.Add($"入力テンソル '{inputName}' の形状が定義されていません");
                }
            }

            foreach (var outputName in modelInfo.OutputTensorNames)
            {
                if (!modelInfo.OutputShapes.ContainsKey(outputName))
                {
                    warnings.Add($"出力テンソル '{outputName}' の形状が定義されていません");
                }
            }

            // メモリ使用量チェック
            if (modelInfo.EstimatedMemoryUsageMB > 16384) // 16GB以上
            {
                warnings.Add($"推定メモリ使用量が大きすぎます: {modelInfo.EstimatedMemoryUsageMB}MB");
            }

            details["InputTensorCount"] = modelInfo.InputTensorNames.Count;
            details["OutputTensorCount"] = modelInfo.OutputTensorNames.Count;
            details["ModelVersion"] = modelInfo.ModelVersion;

            var result = new ModelValidationResult
            {
                IsValid = errors.Count == 0,
                ValidationErrors = errors,
                ValidationWarnings = warnings,
                ValidationDetails = details
            };

            _logger.LogInformation("✅ モデル検証完了: {ModelName} - 有効: {IsValid}, エラー: {ErrorCount}, 警告: {WarningCount}",
                modelName, result.IsValid, errors.Count, warnings.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ モデル検証中にエラーが発生: {ModelName}", modelName);
            return new ModelValidationResult
            {
                IsValid = false,
                ValidationErrors = [$"検証中にエラーが発生: {ex.Message}"]
            };
        }
    }

    private void InitializeDefaultModels()
    {
        try
        {
            _logger.LogDebug("🏗️ デフォルトモデル設定初期化開始");

            // PaddleOCR PP-OCRv4 検出モデル
            var detectionModel = CreateDetectionModelInfo();
            _modelCache.TryAdd("TextDetection", detectionModel);

            // PaddleOCR PP-OCRv4 認識モデル
            var recognitionModel = CreateRecognitionModelInfo();
            _modelCache.TryAdd("TextRecognition", recognitionModel);

            // 言語識別モデル
            var languageIdModel = CreateLanguageIdentificationModelInfo();
            _modelCache.TryAdd("LanguageIdentification", languageIdModel);

            _logger.LogInformation("✅ デフォルトモデル設定初期化完了 - 登録モデル数: {Count}", _modelCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ デフォルトモデル設定初期化失敗");
        }
    }

    private OnnxModelInfo CreateDetectionModelInfo()
    {
        return new OnnxModelInfo
        {
            ModelPath = _ocrSettings.GpuSettings.DetectionModelPath,
            InputTensorNames = ["x"], // PaddleOCR 標準入力名
            OutputTensorNames = ["save_infer_model/scale_0.tmp_1"], // PaddleOCR 検出出力
            InputShapes = new Dictionary<string, int[]>
            {
                ["x"] = [1, 3, 960, 960] // [batch, channels, height, width]
            },
            OutputShapes = new Dictionary<string, int[]>
            {
                ["save_infer_model/scale_0.tmp_1"] = [1, 1, 960, 960] // 検出マップ
            },
            RecommendedBatchSize = 1,
            EstimatedMemoryUsageMB = 512,
            ModelVersion = "PP-OCRv4",
            PreprocessingConfig = new PreprocessingConfig
            {
                EnableNormalization = true,
                NormalizationMean = [0.485f, 0.456f, 0.406f],
                NormalizationStd = [0.229f, 0.224f, 0.225f],
                ResizeConfig = new ResizeConfig
                {
                    TargetWidth = 960,
                    TargetHeight = 960,
                    MaintainAspectRatio = true,
                    InterpolationMethod = "Bilinear"
                },
                ColorSpace = "RGB"
            },
            PostprocessingConfig = new PostprocessingConfig
            {
                ConfidenceThreshold = 0.3f,
                NmsThreshold = 0.4f,
                MaxDetections = 1000,
                CustomParameters = new Dictionary<string, object>
                {
                    ["unclip_ratio"] = 1.5f,
                    ["min_size"] = 3
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["ModelType"] = "TextDetection",
                ["Framework"] = "PaddleOCR",
                ["InputFormat"] = "CHW",
                ["OutputFormat"] = "DetectionMap"
            }
        };
    }

    private OnnxModelInfo CreateRecognitionModelInfo()
    {
        return new OnnxModelInfo
        {
            ModelPath = _ocrSettings.GpuSettings.RecognitionModelPath,
            InputTensorNames = ["x"], // PaddleOCR 標準入力名
            OutputTensorNames = ["save_infer_model/scale_0.tmp_1"], // PaddleOCR 認識出力
            InputShapes = new Dictionary<string, int[]>
            {
                ["x"] = [1, 3, 48, 320] // [batch, channels, height, width]
            },
            OutputShapes = new Dictionary<string, int[]>
            {
                ["save_infer_model/scale_0.tmp_1"] = [1, 6625, 40] // [batch, sequence, vocab]
            },
            RecommendedBatchSize = 8, // 認識は複数画像のバッチ処理が効率的
            EstimatedMemoryUsageMB = 256,
            ModelVersion = "PP-OCRv4",
            PreprocessingConfig = new PreprocessingConfig
            {
                EnableNormalization = true,
                NormalizationMean = [0.5f, 0.5f, 0.5f],
                NormalizationStd = [0.5f, 0.5f, 0.5f],
                ResizeConfig = new ResizeConfig
                {
                    TargetWidth = 320,
                    TargetHeight = 48,
                    MaintainAspectRatio = true,
                    InterpolationMethod = "Bilinear"
                },
                ColorSpace = "RGB"
            },
            PostprocessingConfig = new PostprocessingConfig
            {
                ConfidenceThreshold = 0.5f,
                CustomParameters = new Dictionary<string, object>
                {
                    ["character_dict_path"] = "ppocr/utils/ppocr_keys_v1.txt",
                    ["use_space_char"] = true
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["ModelType"] = "TextRecognition",
                ["Framework"] = "PaddleOCR",
                ["InputFormat"] = "CHW",
                ["OutputFormat"] = "CTC",
                ["Language"] = "multi"
            }
        };
    }

    private OnnxModelInfo CreateLanguageIdentificationModelInfo()
    {
        return new OnnxModelInfo
        {
            ModelPath = _ocrSettings.GpuSettings.LanguageIdentificationModelPath,
            InputTensorNames = ["x"],
            OutputTensorNames = ["save_infer_model/scale_0.tmp_1"],
            InputShapes = new Dictionary<string, int[]>
            {
                ["x"] = [1, 3, 48, 192] // 言語識別用の小さいサイズ
            },
            OutputShapes = new Dictionary<string, int[]>
            {
                ["save_infer_model/scale_0.tmp_1"] = [1, 18] // 言語数
            },
            RecommendedBatchSize = 16, // 軽量なので大きなバッチサイズ
            EstimatedMemoryUsageMB = 64,
            ModelVersion = "PP-OCRv2",
            PreprocessingConfig = new PreprocessingConfig
            {
                EnableNormalization = true,
                NormalizationMean = [0.5f, 0.5f, 0.5f],
                NormalizationStd = [0.5f, 0.5f, 0.5f],
                ResizeConfig = new ResizeConfig
                {
                    TargetWidth = 192,
                    TargetHeight = 48,
                    MaintainAspectRatio = false, // 言語識別は固定サイズ
                    InterpolationMethod = "Bilinear"
                },
                ColorSpace = "RGB"
            },
            PostprocessingConfig = new PostprocessingConfig
            {
                ConfidenceThreshold = 0.9f, // 高い信頼度が必要
                CustomParameters = new Dictionary<string, object>
                {
                    ["language_list"] = new[] { "ch", "en", "japan", "korean", "it", "xi", "pu", "ru", "ar", "ta", "ug", "fa", "ur", "rs", "oc", "rsc", "bg", "uk" }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["ModelType"] = "LanguageIdentification",
                ["Framework"] = "PaddleOCR",
                ["InputFormat"] = "CHW",
                ["OutputFormat"] = "Classification"
            }
        };
    }

    private OnnxModelInfo GetOrCreateModelInfo(string modelName, Func<OnnxModelInfo> factory)
    {
        return _modelCache.GetOrAdd(modelName, _ =>
        {
            _logger.LogDebug("🏗️ モデル情報作成: {ModelName}", modelName);
            return factory();
        });
    }
}
