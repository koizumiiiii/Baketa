using System;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// OPUS-MT専用の自前実装SentencePieceトークナイザー
/// 外部ライブラリ依存を排除し、高性能なトークン化を提供
/// </summary>
public sealed partial class OpusMtNativeTokenizer : ITokenizer, IDisposable
{
    private readonly BpeTokenizer _bpeTokenizer;
    private readonly SentencePieceModel _model;
    private readonly ILogger<OpusMtNativeTokenizer> _logger;
    private bool _disposed;
    private bool _isInitialized;

    /// <inheritdoc/>
    public string TokenizerId { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int VocabularySize => _model?.VocabularySize ?? 0;

    /// <summary>
    /// 初期化が完了しているかどうか
    /// </summary>
    public bool IsInitialized => _isInitialized && !_disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">SentencePieceモデルファイルのパス</param>
    /// <param name="name">トークナイザー名</param>
    /// <param name="logger">ロガー</param>
    public OpusMtNativeTokenizer(
        string modelPath, 
        string name = "OPUS-MT Native Tokenizer",
        ILogger<OpusMtNativeTokenizer>? logger = null)
    {
        if (string.IsNullOrEmpty(modelPath))
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));

        Name = name;
        TokenizerId = $"opus-mt-native-{Guid.NewGuid():N}";
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OpusMtNativeTokenizer>.Instance;
        
        try
        {
            _logger.LogInformation("Native SentencePieceトークナイザーの作成開始: {ModelPath}", modelPath);
            
            // モデルファイルの解析
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
            var parserLogger = loggerFactory.CreateLogger<SentencePieceModelParser>();
            using var parser = new SentencePieceModelParser(parserLogger);
            _model = parser.ParseModelAsync(modelPath).GetAwaiter().GetResult();
            
            // BPEトークナイザーの初期化
            _bpeTokenizer = new BpeTokenizer(_model, 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<BpeTokenizer>.Instance);
            
            _isInitialized = true;
            
            _logger.LogInformation("Native SentencePieceトークナイザーの作成完了: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Native SentencePieceトークナイザーの初期化に失敗しました");
            _model = new SentencePieceModel(); // フォールバック
            _bpeTokenizer = new BpeTokenizer(_model, 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<BpeTokenizer>.Instance);
            _isInitialized = false;
        }
    }

    /// <summary>
    /// 内部実装用コンストラクタ（既に構築済みのモデルを使用）
    /// </summary>
    internal OpusMtNativeTokenizer(
        SentencePieceModel model,
        string name,
        ILogger<OpusMtNativeTokenizer> logger)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        Name = name;
        TokenizerId = $"opus-mt-native-{Guid.NewGuid():N}";
        _logger = logger;
        
        using var bpeLoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
            .Create(builder => builder.AddConsole());
        var bpeLogger = bpeLoggerFactory.CreateLogger<BpeTokenizer>();
        _bpeTokenizer = new BpeTokenizer(_model, bpeLogger);
        _isInitialized = true;
    }

    /// <summary>
    /// 非同期初期化（ファクトリで使用）
    /// </summary>
    /// <param name="modelPath">モデルファイルパス</param>
    /// <returns>初期化済みトークナイザー</returns>
    public static async Task<OpusMtNativeTokenizer> CreateAsync(string modelPath)
    {
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        var parserLogger = loggerFactory.CreateLogger<SentencePieceModelParser>();
        var tokenizerLogger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();
        
        using var parser = new SentencePieceModelParser(parserLogger);
        var model = await parser.ParseModelAsync(modelPath).ConfigureAwait(false);
        
        return new OpusMtNativeTokenizer(model, "OPUS-MT Native Tokenizer", tokenizerLogger);
    }

    /// <inheritdoc/>
    public int[] Tokenize(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized)
        {
            _logger.LogWarning("Tokenizer not initialized, returning empty result");
            return [];
        }

        if (string.IsNullOrEmpty(text))
            return [];

        try
        {
            _logger.LogDebug("トークン化開始: 入力長={Length}, 原文='{Text}'", text.Length, text);
            
            // 前処理でテキストを正規化
            var normalizedText = NormalizeInputText(text);
            _logger.LogDebug("テキスト正規化後: '{NormalizedText}'", normalizedText);
            
            var tokens = _bpeTokenizer.TokenizeBpe(normalizedText.AsSpan());
            
            // BOS/EOSトークンを追加
            var result = AddSpecialTokens(tokens);
            
            _logger.LogDebug("トークン化完了: 出力トークン数={Count}, トークン=[{Tokens}]", 
                result.Length, string.Join(", ", result));
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "トークン化処理でエラーが発生しました: {Text}", text);
            throw new InvalidOperationException($"Tokenization failed for text: {text}", ex);
        }
    }

    /// <inheritdoc/>
    public string Decode(int[] tokens)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized)
        {
            _logger.LogWarning("Tokenizer not initialized, returning empty result");
            return string.Empty;
        }

        if (tokens.Length == 0)
            return string.Empty;

        try
        {
            _logger.LogDebug("デコード開始: 入力トークン数={Count}", tokens.Length);
            
            // 特殊トークンをフィルタリング
            var filteredTokens = FilterSpecialTokens(tokens);
            _logger.LogDebug("特殊トークンフィルタリング後: 有効トークン数={Count}", filteredTokens.Length);
            
            if (filteredTokens.Length == 0)
            {
                _logger.LogWarning("特殊トークンフィルタリング後、有効なトークンがありません");
                return string.Empty;
            }
            
            var result = _bpeTokenizer.Decode(filteredTokens);
            
            // 後処理でクリーンアップ
            result = CleanDecodedText(result);
            
            _logger.LogDebug("デコード完了: 出力長={Length}, 結果='{Result}'", result.Length, result);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "デコード処理でエラーが発生しました");
            throw new InvalidOperationException("Decoding failed", ex);
        }
    }

    /// <inheritdoc/>
    public string DecodeToken(int token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized)
        {
            _logger.LogWarning("Tokenizer not initialized, returning unknown token");
            return "<unk>";
        }

        return _model.ReverseVocabulary.GetValueOrDefault(token, "<unk>");
    }

    /// <summary>
    /// 特殊トークンIDを取得
    /// </summary>
    /// <param name="tokenType">特殊トークンタイプ ("BOS", "EOS", "UNK", "PAD")</param>
    /// <returns>特殊トークンのID</returns>
    public long GetSpecialTokenId(string tokenType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized)
            return tokenType switch
            {
                "BOS" => 0L,
                "EOS" => 0L, // Helsinki-NLP OPUS-MT: BOS=EOS=0
                "UNK" => 1L,
                "PAD" => 60715L, // Helsinki-NLP: VocabSize-1
                _ => throw new ArgumentException($"Unknown special token type: {tokenType}")
            };

        return tokenType.ToUpperInvariant() switch
        {
            // Helsinki-NLP OPUS-MTでは強制的にBOS=EOS=0を使用
            "BOS" => 0, // _model.SpecialTokens.BosId から強制変更
            "EOS" => 0, // _model.SpecialTokens.EosId から強制変更
            "UNK" => _model.SpecialTokens.UnkId,
            "PAD" => _model.SpecialTokens.PadId,
            _ => throw new ArgumentException($"Unknown special token type: {tokenType}")
        };
    }

    /// <summary>
    /// BOS/EOSトークンを追加
    /// </summary>
    private int[] AddSpecialTokens(int[] tokens)
    {
        var result = new int[tokens.Length + 2];
        result[0] = _model.SpecialTokens.BosId;
        Array.Copy(tokens, 0, result, 1, tokens.Length);
        result[^1] = _model.SpecialTokens.EosId;
        return result;
    }

    /// <summary>
    /// 入力テキストの正規化
    /// </summary>
    private static string NormalizeInputText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 省略記号の正規化
        text = text.Replace("……", "...");
        text = text.Replace("。。。", "...");
        
        // 全角文字の半角への統一（必要に応じて）
        text = text.Replace("！", "!");
        text = text.Replace("？", "?");
        
        // 前後の空白を除去
        text = text.Trim();
        
        // 連続する空白を単一の空白に変換
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        
        return text;
    }

    /// <summary>
    /// 特殊トークンをフィルタリング
    /// </summary>
    private int[] FilterSpecialTokens(int[] tokens)
    {
        if (!_isInitialized)
            return tokens;

        var bosId = _model.SpecialTokens.BosId;
        var eosId = _model.SpecialTokens.EosId;
        var padId = _model.SpecialTokens.PadId;
        var unkId = _model.SpecialTokens.UnkId;

        var filteredList = new List<int>();
        
        foreach (var token in tokens)
        {
            // BOS, EOS, PADトークンは除外
            if (token == bosId || token == eosId || token == padId)
            {
                _logger.LogDebug("特殊トークンをスキップ: {TokenId} ({Type})", 
                    token, 
                    token == bosId ? "BOS" : token == eosId ? "EOS" : "PAD");
                continue;
            }
            
            // UNKトークンは警告付きで含める
            if (token == unkId)
            {
                _logger.LogWarning("UNKトークンが含まれています: {TokenId}", token);
            }
            
            filteredList.Add(token);
        }

        return [.. filteredList];
    }

    /// <summary>
    /// デコードされたテキストのクリーンアップ
    /// </summary>
    private static string CleanDecodedText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 不正な特殊トークン文字列を除去
        text = text.Replace("</s>", "");
        text = text.Replace("<s>", "");
        text = text.Replace("<pad>", "");
        text = text.Replace("<unk>", "");
        
        // 前後の空白文字を除去
        text = text.Trim();
        
        // 連続する空白を単一の空白に変換
        text = MyRegex().Replace(text, " ");
        
        return text;
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _bpeTokenizer?.Dispose();
        _disposed = true;
        _isInitialized = false;
        
        _logger.LogDebug("OpusMtNativeTokenizer disposed: {TokenizerId}", TokenizerId);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
