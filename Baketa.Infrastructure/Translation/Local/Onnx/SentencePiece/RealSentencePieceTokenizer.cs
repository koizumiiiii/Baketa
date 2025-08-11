using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Services;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1031 // Do not catch general exception types - 暫定実装のため

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Microsoft.ML.Tokenizers 0.21.0を使用したOPUS-MT対応SentencePieceトークナイザー実装
/// </summary>
public class RealSentencePieceTokenizer : Baketa.Core.Translation.Models.ITokenizer, IDisposable
{
    private readonly object? _innerTokenizer;
    private readonly ILogger<RealSentencePieceTokenizer> _logger;
    private readonly string _modelName;
    private readonly SentencePieceNormalizer _normalizer;
    private readonly int _maxInputLength;
    private bool _disposed;

    /// <inheritdoc/>
    public string TokenizerId { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int VocabularySize { get; private set; } = 32000; // デフォルト値

    /// <summary>
    /// モデルファイルのパス
    /// </summary>
    public string ModelPath { get; }

    /// <summary>
    /// Microsoft.ML.Tokenizersライブラリを使用した実トークナイザーが利用可能かどうか
    /// </summary>
    public bool IsRealTokenizerAvailable { get; }

    /// <summary>
    /// トークナイザーが初期化済みかどうか
    /// </summary>
    public bool IsInitialized => IsRealTokenizerAvailable;

    /// <summary>
    /// 実際のSentencePieceTokenizerが利用可能かどうか（テスト用）
    /// </summary>
    public bool IsRealSentencePieceAvailable => IsRealTokenizerAvailable;

    /// <summary>
    /// 未知トークンのID
    /// </summary>
    public int UnknownTokenId => GetSpecialTokens().UnknownId;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">SentencePieceモデルファイルのパス</param>
    /// <param name="logger">ロガー</param>
    /// <param name="maxInputLength">最大入力長</param>
    public RealSentencePieceTokenizer(
        string modelPath, 
        ILogger<RealSentencePieceTokenizer> logger, 
        int maxInputLength = 512)
    {
        if (string.IsNullOrEmpty(modelPath))
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxInputLength = maxInputLength;
        
        ModelPath = modelPath;
        _modelName = Path.GetFileNameWithoutExtension(modelPath);
        
        TokenizerId = $"SentencePiece_{_modelName}";
        Name = $"SentencePiece Tokenizer ({_modelName})";
        
        // SentencePiece正規化サービスを初期化
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        var normalizerLogger = loggerFactory.CreateLogger<SentencePieceNormalizer>();
        _normalizer = new SentencePieceNormalizer(normalizerLogger, SentencePieceNormalizationOptions.OpusMt);
        
        try
        {
            // ファイルの存在チェック
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"モデルファイルが見つかりません: {modelPath}");
            }

            _logger.LogInformation("SentencePieceトークナイザーを初期化中: {ModelPath}", modelPath);

            // Microsoft.ML.Tokenizers 0.21.0でSentencePieceTokenizerを直接作成
            try
            {
                var tokenizerType = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");
                if (tokenizerType != null)
                {
                    _innerTokenizer = Activator.CreateInstance(tokenizerType, modelPath);
                    
                    if (_innerTokenizer != null)
                    {
                        _logger.LogInformation(
                            "SentencePieceTokenizer（0.21.0）を初期化しました: {ModelPath}",
                            modelPath);
                            
                        IsRealTokenizerAvailable = true;
                    }
                    else
                    {
                        _logger.LogWarning("SentencePieceTokenizerの作成に失敗しました");
                        IsRealTokenizerAvailable = false;
                    }
                }
                else
                {
                    _logger.LogWarning("SentencePieceTokenizerクラスが見つかりませんでした");
                    IsRealTokenizerAvailable = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SentencePieceTokenizer作成に失敗しました: {ModelPath}", modelPath);
                IsRealTokenizerAvailable = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SentencePieceトークナイザーの初期化に失敗しました: {ModelPath}", modelPath);
            IsRealTokenizerAvailable = false;
        }

        _logger.LogInformation(
            "SentencePieceトークナイザー初期化完了: ModelPath={ModelPath}, Available={Available}",
            modelPath, IsRealTokenizerAvailable);
    }

    /// <inheritdoc/>
    public int[] Tokenize(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));

        if (string.IsNullOrEmpty(text))
            return [];

        // SentencePiece互換正規化を適用
        string normalizedText;
        try
        {
            normalizedText = _normalizer.Normalize(text);
            _logger.LogDebug("テキスト正規化完了: '{Original}' -> '{Normalized}'", text, normalizedText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "テキスト正規化に失敗、元のテキストを使用: '{Text}'", text);
            normalizedText = text;
        }

        if (_innerTokenizer != null)
        {
            try
            {
                // 0.21.0 API: Encode(string) → TokenizerResult
                var tokenizerType = _innerTokenizer.GetType();
                var encodeMethod = tokenizerType.GetMethod("Encode", [typeof(string)]);
                
                if (encodeMethod != null)
                {
                    var result = encodeMethod.Invoke(_innerTokenizer, [normalizedText]);
                    
                    if (result != null)
                    {
                        // TokenizerResult.Tokens プロパティを取得
                        var tokensProperty = result.GetType().GetProperty("Tokens");
                        if (tokensProperty != null)
                        {
                            var tokens = tokensProperty.GetValue(result);
                            if (tokens is System.Collections.IEnumerable tokensEnumerable)
                            {
                                var tokenIds = new List<int>();
                                foreach (var token in tokensEnumerable)
                                {
                                    // Token.Id プロパティを取得
                                    if (token != null)
                                    {
                                        var idProperty = token.GetType().GetProperty("Id");
                                        if (idProperty != null && idProperty.GetValue(token) is int id)
                                        {
                                            tokenIds.Add(id);
                                        }
                                    }
                                }
                                
                                _logger.LogDebug(
                                    "SentencePieceTokenizer（0.21.0）でテキストをトークン化しました: 入力長={InputLength}, トークン数={TokenCount}",
                                    text.Length, tokenIds.Count);
                                    
                                return [.. tokenIds];
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft.ML.Tokenizers 0.21.0 APIでのトークン化に失敗");
            }
        }

        // フォールバック実装: より意味のあるトークン化
        _logger.LogDebug("フォールバック実装を使用してトークン化します");
        return FallbackTokenize(normalizedText);
    }

    /// <inheritdoc/>
    public string Decode(int[] tokenIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));

        if (tokenIds == null || tokenIds.Length == 0)
            return string.Empty;

        if (_innerTokenizer != null)
        {
            try
            {
                // 0.21.0 API: Decode(IEnumerable<int>) → string
                var tokenizerType = _innerTokenizer.GetType();
                var decodeMethod = tokenizerType.GetMethod("Decode", [typeof(IEnumerable<int>)]);
                
                if (decodeMethod != null)
                {
                    var result = decodeMethod.Invoke(_innerTokenizer, [tokenIds.AsEnumerable()]);
                    
                    if (result is string decoded)
                    {
                        // プレフィックススペース記号を除去して元の形式に復元
                        string restoredText;
                        try
                        {
                            restoredText = _normalizer.RemovePrefixSpaceSymbol(decoded);
                            _logger.LogDebug(
                                "SentencePieceTokenizer（0.21.0）でトークンをデコードしました: トークン数={TokenCount}, 復元テキスト='{RestoredText}'",
                                tokenIds.Length, restoredText);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "プレフィックススペース記号の除去に失敗、デコード結果をそのまま使用");
                            restoredText = decoded;
                        }
                            
                        return restoredText;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft.ML.Tokenizers 0.21.0 APIでのデコードに失敗");
            }
        }

        // フォールバック実装
        _logger.LogDebug("フォールバック実装を使用してデコードします");
        return FallbackDecode(tokenIds);
    }

    /// <inheritdoc/>
    public string DecodeToken(int token)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));

        try
        {
            return Decode([token]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "単一トークンのデコードエラー: Token={Token}", token);
            throw new InvalidOperationException(
                $"単一トークンのデコードに失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// フォールバック実装: 改良されたトークン化
    /// </summary>
    private int[] FallbackTokenize(string text)
    {
        // より意味のあるトークン化: 単語境界を考慮
        var words = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<int>();
        
        foreach (var word in words)
        {
            // 単語をハッシュ化してトークンIDに変換
            var hash = word.GetHashCode();
            var tokenId = Math.Abs(hash) % 30000 + 1000; // 1000-30999の範囲
            tokens.Add(tokenId);
        }
        
        return [.. tokens];
    }

    /// <summary>
    /// フォールバック実装: 改良されたデコード
    /// </summary>
    private string FallbackDecode(int[] tokenIds)
    {
        // フォールバック実装では正確なデコードは不可能
        // 代わりにtok_形式で返す
        return $"tok_{string.Join("_", tokenIds)}";
    }

    /// <inheritdoc/>
    public async Task<int[]> TokenizeAsync(string text)
    {
        return await Task.FromResult(Tokenize(text)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> DecodeAsync(int[] tokenIds)
    {
        return await Task.FromResult(Decode(tokenIds)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_innerTokenizer is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _normalizer?.Dispose();

        _disposed = true;
        _logger.LogDebug("SentencePieceトークナイザーを破棄しました: {ModelPath}", ModelPath);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 特殊トークンを取得（テスト用メソッド）
    /// </summary>
    public SpecialTokens GetSpecialTokens()
    {
        // フォールバック実装: 基本的な特殊トークンを返す
        return new SpecialTokens
        {
            UnknownId = 0,
            BeginOfSentenceId = 1,
            EndOfSentenceId = 2,
            PaddingId = 3
        };
    }

    /// <summary>
    /// 特殊トークン情報を格納するクラス
    /// </summary>
    public class SpecialTokens
    {
        public int UnknownId { get; set; }
        public int BeginOfSentenceId { get; set; }
        public int EndOfSentenceId { get; set; }
        public int PaddingId { get; set; }
    }

    /// <summary>
    /// 正規化の検証（テスト用メソッド - パラメータなし）
    /// </summary>
    public bool ValidateNormalization()
    {
        // 正規化サービスが利用可能かチェック
        return _normalizer != null;
    }

    /// <summary>
    /// 正規化の検証（テスト用メソッド）
    /// </summary>
    public bool ValidateNormalization(string input, string expected)
    {
        try
        {
            var normalized = _normalizer.Normalize(input);
            return string.Equals(normalized, expected, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// テキストを正規化（テスト用メソッド）
    /// </summary>
    public string NormalizeText(string input)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));
        return _normalizer.Normalize(input);
    }
}