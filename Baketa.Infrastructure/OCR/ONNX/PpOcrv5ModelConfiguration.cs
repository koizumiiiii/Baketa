using System.IO;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.ONNX;

/// <summary>
/// PP-OCRv5 ONNX モデル設定実装
/// Issue #181: PP-OCRv5 ONNX モデルパス管理
/// モデルソース: https://huggingface.co/monkt/paddleocr-onnx
/// </summary>
public sealed class PpOcrv5ModelConfiguration : IPpOcrv5ModelConfiguration
{
    private readonly ILogger<PpOcrv5ModelConfiguration>? _logger;

    /// <summary>
    /// モデルのルートディレクトリ
    /// デフォルト: アプリケーションディレクトリ/models/ppocrv5-onnx
    /// </summary>
    public string ModelsRootDirectory { get; }

    #region ファイル名定数

    /// <summary>
    /// 検出モデルファイル名
    /// </summary>
    private const string DetectionModelFileName = "det.onnx";

    /// <summary>
    /// 認識モデルファイル名
    /// </summary>
    private const string RecognitionModelFileName = "rec.onnx";

    /// <summary>
    /// 方向分類モデルファイル名
    /// </summary>
    private const string ClassifierModelFileName = "cls.onnx";

    /// <summary>
    /// 辞書ファイル名（HuggingFace形式）
    /// </summary>
    private const string DictionaryFileName = "dict.txt";

    #endregion

    #region ディレクトリ名定数（HuggingFace構造準拠）

    /// <summary>
    /// 検出モデルディレクトリ
    /// </summary>
    private const string DetectionDirectory = "detection";

    /// <summary>
    /// 言語別モデルディレクトリ
    /// </summary>
    private const string LanguagesDirectory = "languages";

    /// <summary>
    /// 方向分類モデルディレクトリ
    /// </summary>
    private const string ClassifierDirectory = "classification";

    #endregion

    // 言語コードとディレクトリ名のマッピング（HuggingFace構造準拠）
    // 注意: PP-OCRv5では日本語・中国語は同一モデル（chinese）で対応
    private static readonly Dictionary<string, string> LanguageDirectoryMap = new()
    {
        ["jpn"] = "chinese",        // 日本語（PP-OCRv5: chineseモデルが日本語もサポート）
        ["chi_sim"] = "chinese",    // 簡体字中国語
        ["chi_tra"] = "chinese",    // 繁体字中国語
        ["eng"] = "latin",          // 英語（ラテン文字）
        ["latin"] = "latin",        // ラテン文字全般
        ["kor"] = "korean",         // 韓国語
        ["ara"] = "arabic",         // アラビア語
        ["cyr"] = "cyrillic",       // キリル文字
    };

    public PpOcrv5ModelConfiguration(ILogger<PpOcrv5ModelConfiguration>? logger = null)
        : this(GetDefaultModelsDirectory(), logger)
    {
    }

    public PpOcrv5ModelConfiguration(string modelsRootDirectory, ILogger<PpOcrv5ModelConfiguration>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsRootDirectory);

        ModelsRootDirectory = modelsRootDirectory;
        _logger = logger;

        _logger?.LogInformation("ONNX モデル設定初期化: {RootDirectory}", ModelsRootDirectory);
    }

    public string GetDetectionModelPath()
    {
        // HuggingFace構造: detection/det.onnx
        return Path.Combine(ModelsRootDirectory, DetectionDirectory, DetectionModelFileName);
    }

    public string GetRecognitionModelPath(string language)
    {
        // HuggingFace構造: languages/{language}/rec.onnx
        var languageDir = GetLanguageDirectory(language);
        return Path.Combine(ModelsRootDirectory, LanguagesDirectory, languageDir, RecognitionModelFileName);
    }

    public string GetClassifierModelPath()
    {
        // HuggingFace構造: classification/cls.onnx
        return Path.Combine(ModelsRootDirectory, ClassifierDirectory, ClassifierModelFileName);
    }

    public string GetDictionaryPath(string language)
    {
        // HuggingFace構造: languages/{language}/dict.txt
        var languageDir = GetLanguageDirectory(language);
        return Path.Combine(ModelsRootDirectory, LanguagesDirectory, languageDir, DictionaryFileName);
    }

    public bool IsModelsAvailable()
    {
        var detPath = GetDetectionModelPath();
        if (!File.Exists(detPath))
        {
            _logger?.LogDebug("検出モデルが見つかりません: {Path}", detPath);
            return false;
        }

        return true;
    }

    public bool IsLanguageAvailable(string language)
    {
        var recPath = GetRecognitionModelPath(language);
        if (!File.Exists(recPath))
        {
            _logger?.LogDebug("認識モデルが見つかりません: {Language} -> {Path}", language, recPath);
            return false;
        }

        var dictPath = GetDictionaryPath(language);
        if (!File.Exists(dictPath))
        {
            _logger?.LogDebug("辞書ファイルが見つかりません: {Language} -> {Path}", language, dictPath);
            return false;
        }

        return true;
    }

    private static string GetLanguageDirectory(string language)
    {
        if (LanguageDirectoryMap.TryGetValue(language.ToLowerInvariant(), out var dir))
        {
            return dir;
        }

        // デフォルトは日本語・中国語共通モデル
        return "chinese_japanese";
    }

    private static string GetDefaultModelsDirectory()
    {
        // 1. 環境変数からの取得を試行
        var envPath = Environment.GetEnvironmentVariable("BAKETA_ONNX_MODELS_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        // 2. 実行ディレクトリ/models/ppocrv5-onnx
        var baseDir = AppContext.BaseDirectory;
        var modelsDir = Path.Combine(baseDir, "models", "ppocrv5-onnx");

        // 3. 開発時の相対パス
        if (!Directory.Exists(modelsDir))
        {
            // E:\dev\Baketa\models\ppocrv5-onnx
            var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "ppocrv5-onnx"));
            if (Directory.Exists(devPath))
            {
                return devPath;
            }
        }

        return modelsDir;
    }
}
