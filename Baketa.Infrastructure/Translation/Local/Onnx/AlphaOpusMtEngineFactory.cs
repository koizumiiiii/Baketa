using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// αテスト向けOPUS-MT翻訳エンジンファクトリー
/// 日英・英日の2言語ペアのみサポート
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="configuration">設定</param>
/// <param name="loggerFactory">ロガーファクトリー</param>
public class AlphaOpusMtEngineFactory(
    AlphaOpusMtConfiguration configuration,
    ILoggerFactory loggerFactory)
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly AlphaOpusMtConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>
    /// サポートされている言語ペアを取得
    /// </summary>
    /// <returns>サポートされている言語ペアのリスト</returns>
    public Task<IReadOnlyList<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        return Task.FromResult<IReadOnlyList<LanguagePair>>(
        [
            new() { SourceLanguage = Language.Japanese, TargetLanguage = Language.English },
            new() { SourceLanguage = Language.English, TargetLanguage = Language.Japanese }
        ]);
    }

    /// <summary>
    /// 指定された言語ペア用の翻訳エンジンを作成
    /// </summary>
    /// <param name="languagePair">言語ペア</param>
    /// <returns>翻訳エンジン</returns>
    public async Task<AlphaOpusMtTranslationEngine> CreateEngineAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair);

        // サポートされている言語ペアかチェック
        var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        var isSupported = false;

        foreach (var pair in supportedPairs)
        {
            if (string.Equals(pair.SourceLanguage.Code, languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.TargetLanguage.Code, languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase))
            {
                isSupported = true;
                break;
            }
        }

        if (!isSupported)
        {
            throw new ArgumentException($"言語ペアがサポートされていません: {languagePair.SourceLanguage.Code} -> {languagePair.TargetLanguage.Code}");
        }

        // モデルファイルパスの取得
        var modelPath = GetModelPath(languagePair);
        var tokenizerPath = GetTokenizerPath(languagePair);

        // ファイルの存在確認
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"ONNXモデルファイルが見つかりません: {modelPath}");
        }

        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException($"SentencePieceモデルファイルが見つかりません: {tokenizerPath}");
        }

        // エンジンの作成
        var options = new AlphaOpusMtOptions
        {
            MaxSequenceLength = _configuration.MaxSequenceLength,
            MemoryLimitMb = _configuration.MemoryLimitMb,
            ThreadCount = _configuration.ThreadCount
        };

        var engineLogger = _loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();
        var engine = new AlphaOpusMtTranslationEngine(
            modelPath,
            tokenizerPath, // ソーストークナイザーパス
            languagePair,
            options,
            engineLogger);

        var logger = _loggerFactory.CreateLogger<AlphaOpusMtEngineFactory>();
        logger.LogInformation("OPUS-MT αエンジンを作成しました: {SourceLang} -> {TargetLang}",
            languagePair.SourceLanguage.Code, languagePair.TargetLanguage.Code);

        return engine;
    }

    /// <summary>
    /// 日英翻訳エンジンを作成（同期版、DIで使用）
    /// </summary>
    /// <param name="options">オプション</param>
    /// <param name="logger">ロガー</param>
    /// <returns>日英翻訳エンジン</returns>
    public AlphaOpusMtTranslationEngine CreateJapaneseToEnglishEngine(AlphaOpusMtOptions options, ILogger<AlphaOpusMtTranslationEngine> logger)
    {
        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        // モデルファイルパスの取得（存在チェックなし、フォールバック翻訳を使用）
        var modelPath = GetModelPath(languagePair);
        var tokenizerPath = GetTokenizerPath(languagePair);

        var engine = new AlphaOpusMtTranslationEngine(
            modelPath,
            tokenizerPath, // ソーストークナイザーパス
            languagePair,
            options,
            logger);
        
        return engine;
    }

    /// <summary>
    /// 英日翻訳エンジンを作成（同期版、DIで使用）
    /// </summary>
    /// <param name="options">オプション</param>
    /// <param name="logger">ロガー</param>
    /// <returns>英日翻訳エンジン</returns>
    public AlphaOpusMtTranslationEngine CreateEnglishToJapaneseEngine(AlphaOpusMtOptions options, ILogger<AlphaOpusMtTranslationEngine> logger)
    {
        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.English,
            TargetLanguage = Language.Japanese
        };

        // モデルファイルパスの取得（存在チェックなし、フォールバック翻訳を使用）
        var modelPath = GetModelPath(languagePair);
        var tokenizerPath = GetTokenizerPath(languagePair);

        return new AlphaOpusMtTranslationEngine(
            modelPath,
            tokenizerPath, // ソーストークナイザーパス
            languagePair,
            options,
            logger);
    }

    /// <summary>
    /// 利用可能なエンジンの確認
    /// </summary>
    /// <returns>利用可能なエンジンの情報</returns>
    public async Task<AlphaOpusMtAvailabilityInfo> CheckAvailabilityAsync()
    {
        var availableEngines = new List<LanguagePair>();
        var missingFiles = new List<string>();

        var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);

        foreach (var pair in supportedPairs)
        {
            var modelPath = GetModelPath(pair);
            var tokenizerPath = GetTokenizerPath(pair);

            var modelExists = File.Exists(modelPath);
            var tokenizerExists = File.Exists(tokenizerPath);

            if (modelExists && tokenizerExists)
            {
                availableEngines.Add(pair);
            }
            else
            {
                if (!modelExists)
                {
                    missingFiles.Add(modelPath);
                }
                if (!tokenizerExists)
                {
                    missingFiles.Add(tokenizerPath);
                }
            }
        }

        return new AlphaOpusMtAvailabilityInfo
        {
            AvailableLanguagePairs = availableEngines,
            MissingFiles = missingFiles,
            IsFullyAvailable = availableEngines.Count == supportedPairs.Count
        };
    }

    /// <summary>
    /// 言語ペアに対応するモデルファイルパスを取得
    /// </summary>
    private string GetModelPath(LanguagePair languagePair)
    {
        var modelName = GetModelName(languagePair);
        // HuggingFaceモデルからONNXファイルを使用（今はTensorFlowモデルのみ存在）
        // 一時的にフォールバック用のダミーパスを返す
        var modelDirectory = Path.Combine(Path.GetDirectoryName(_configuration.ModelsDirectory) ?? "", modelName);
        return Path.Combine(modelDirectory, "model.onnx"); // 存在しないがフォールバック処理で対応
    }

    /// <summary>
    /// 言語ペアに対応するトークナイザーファイルパスを取得
    /// </summary>
    private string GetTokenizerPath(LanguagePair languagePair)
    {
        var modelName = GetModelName(languagePair);
        // HuggingFaceモデルのSentencePieceファイルを使用
        var modelDirectory = Path.Combine(Path.GetDirectoryName(_configuration.ModelsDirectory) ?? "", modelName);
        return Path.Combine(modelDirectory, "source.spm"); // source.spmファイルを使用
    }

    /// <summary>
    /// 言語ペアに対応するモデル名を取得
    /// </summary>
    private string GetModelName(LanguagePair languagePair)
    {
        var sourceCode = languagePair.SourceLanguage.Code.ToLowerInvariant();
        var targetCode = languagePair.TargetLanguage.Code.ToLowerInvariant();
        
        return sourceCode switch
        {
            "ja" when string.Equals(targetCode, "en", StringComparison.OrdinalIgnoreCase) => "opus-mt-ja-en",
            "en" when string.Equals(targetCode, "ja", StringComparison.OrdinalIgnoreCase) => "opus-mt-en-jap",
            _ => throw new ArgumentException($"モデル名が定義されていません: {languagePair.SourceLanguage.Code} -> {languagePair.TargetLanguage.Code}")
        };
    }
}

/// <summary>
/// αテスト向けOPUS-MT設定
/// </summary>
public class AlphaOpusMtConfiguration
{
    /// <summary>
    /// モデルディレクトリのパス（SentencePieceモデル用）
    /// </summary>
    public string ModelsDirectory { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

    /// <summary>
    /// 最大シーケンス長
    /// </summary>
    public int MaxSequenceLength { get; set; } = 256;

    /// <summary>
    /// メモリ制限（MB）
    /// </summary>
    public int MemoryLimitMb { get; set; } = 300;

    /// <summary>
    /// スレッド数
    /// </summary>
    public int ThreadCount { get; set; } = 2;

    /// <summary>
    /// αテスト機能が有効かどうか
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// OPUS-MT αエンジンの利用可能性情報
/// </summary>
public class AlphaOpusMtAvailabilityInfo
{
    /// <summary>
    /// 利用可能な言語ペア
    /// </summary>
    public List<LanguagePair> AvailableLanguagePairs { get; set; } = [];

    /// <summary>
    /// 不足しているファイルのリスト
    /// </summary>
    public List<string> MissingFiles { get; set; } = [];

    /// <summary>
    /// 完全に利用可能かどうか
    /// </summary>
    public bool IsFullyAvailable { get; set; }

    /// <summary>
    /// 部分的に利用可能かどうか
    /// </summary>
    public bool IsPartiallyAvailable => AvailableLanguagePairs.Count > 0;
}
