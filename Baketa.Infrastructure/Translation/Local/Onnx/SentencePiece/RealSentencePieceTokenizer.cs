using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Microsoft.ML.Tokenizersを使用した実際のSentencePieceトークナイザー実装
/// </summary>
public class RealSentencePieceTokenizer : ITokenizer, IDisposable
{
    private readonly SentencePieceTokenizer _innerTokenizer;
    private readonly SentencePieceNormalizer _normalizer;
    private readonly ILogger<RealSentencePieceTokenizer> _logger;
    private readonly string _modelName;
    private readonly int _maxInputLength;
    private bool _disposed;

    /// <inheritdoc/>
    public string TokenizerId { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int VocabularySize => _innerTokenizer.Vocab.Count;

    /// <summary>
    /// モデルファイルのパス
    /// </summary>
    public string ModelPath { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">SentencePiece モデルファイルのパス</param>
    /// <param name="logger">ロガー</param>
    /// <param name="maxInputLength">最大入力長（デフォルト: 10000文字）</param>
    public RealSentencePieceTokenizer(
        string modelPath,
        ILogger<RealSentencePieceTokenizer> logger,
        int maxInputLength = 10000)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath, nameof(modelPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        ModelPath = modelPath;
        _modelName = Path.GetFileNameWithoutExtension(modelPath);
        _maxInputLength = maxInputLength;
        
        TokenizerId = $"SentencePiece_{_modelName}";
        Name = $"SentencePiece Tokenizer ({_modelName})";
        
        try
        {
            using var stream = File.OpenRead(modelPath);
            _innerTokenizer = SentencePieceTokenizer.Create(
                stream,
                addBeginOfSentence: true,
                addEndOfSentence: false
            );
            
            _normalizer = new SentencePieceNormalizer();
            
            _logger.LogInformation(
                "SentencePieceトークナイザーを初期化しました: {ModelPath}, 語彙サイズ: {VocabSize}",
                modelPath, VocabularySize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SentencePieceトークナイザーの初期化に失敗しました: {ModelPath}",
                modelPath);
            throw new InvalidOperationException(
                $"SentencePieceトークナイザーの初期化に失敗しました: {modelPath}", ex);
        }
    }

    /// <inheritdoc/>
    public int[] Tokenize(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));
        ArgumentNullException.ThrowIfNull(text, nameof(text));

        try
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<int>();
            }
            
            // 入力検証
            if (text.Length > _maxInputLength)
            {
                throw new TokenizationException(
                    $"入力テキストが最大長({_maxInputLength}文字)を超えています",
                    text,
                    _modelName);
            }
            
            // 正規化（NFKC: 互換性のある正規化形式）
            var normalized = _normalizer.Normalize(text);
            
            // トークン化
            var result = _innerTokenizer.Encode(normalized);
            var tokens = result.Ids.ToArray();
            
            _logger.LogDebug(
                "テキストをトークン化しました: 入力長={InputLength}, トークン数={TokenCount}",
                text.Length, tokens.Length);
                
            return tokens;
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "メモリ不足: テキスト長={Length}", text.Length);
            throw new TokenizationException(
                "トークン化中にメモリ不足が発生しました",
                text,
                _modelName,
                ex);
        }
        catch (Exception ex) when (ex is not TokenizationException)
        {
            _logger.LogError(ex, "トークン化エラー: テキスト長={Length}", text.Length);
            throw new TokenizationException(
                $"テキストのトークン化に失敗しました: {ex.Message}",
                text,
                _modelName,
                ex);
        }
    }

    /// <inheritdoc/>
    public string Decode(int[] tokens)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));
        ArgumentNullException.ThrowIfNull(tokens, nameof(tokens));

        try
        {
            if (tokens.Length == 0)
            {
                return string.Empty;
            }
            
            var decoded = _innerTokenizer.Decode(tokens);
            
            _logger.LogDebug(
                "トークンをデコードしました: トークン数={TokenCount}, 出力長={OutputLength}",
                tokens.Length, decoded.Length);
                
            return decoded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "デコードエラー: トークン数={TokenCount}", tokens.Length);
            throw new InvalidOperationException(
                $"トークンのデコードに失敗しました: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public string DecodeToken(int token)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));

        try
        {
            // 単一トークンを配列として渡してデコード
            var decoded = _innerTokenizer.Decode(new[] { token });
            return decoded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "単一トークンのデコードエラー: Token={Token}", token);
            throw new InvalidOperationException(
                $"単一トークンのデコードに失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 特殊トークンの情報を取得
    /// </summary>
    /// <returns>特殊トークンの情報</returns>
    public SpecialTokens GetSpecialTokens()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));

        var specialTokens = new SpecialTokens();

        // モデルから特殊トークンIDを取得
        if (_innerTokenizer.UnknownTokenId == null)
        {
            throw new InvalidOperationException("モデルに<unk>トークンが定義されていません");
        }
        specialTokens.UnknownId = _innerTokenizer.UnknownTokenId.Value;

        if (_innerTokenizer.BeginningOfSentenceTokenId == null)
        {
            throw new InvalidOperationException("モデルに<s>トークンが定義されていません");
        }
        specialTokens.BeginOfSentenceId = _innerTokenizer.BeginningOfSentenceTokenId.Value;

        if (_innerTokenizer.EndOfSentenceTokenId == null)
        {
            throw new InvalidOperationException("モデルに</s>トークンが定義されていません");
        }
        specialTokens.EndOfSentenceId = _innerTokenizer.EndOfSentenceTokenId.Value;

        // パディングトークンはオプショナル
        specialTokens.PaddingId = _innerTokenizer.PaddingTokenId ?? -1;

        _logger.LogDebug(
            "特殊トークン: <unk>={UnknownId}, <s>={BeginId}, </s>={EndId}, <pad>={PadId}",
            specialTokens.UnknownId,
            specialTokens.BeginOfSentenceId,
            specialTokens.EndOfSentenceId,
            specialTokens.PaddingId);

        return specialTokens;
    }

    /// <summary>
    /// 正規化設定の検証
    /// </summary>
    public void ValidateNormalization()
    {
        var testCases = new Dictionary<string, string>
        {
            { "①②③", "123" },      // 数字の正規化
            { "ｱｲｳ", "アイウ" },    // カタカナの正規化
            { "Ａ", "A" },          // 全角英字の正規化
            { "　", " " },          // 全角スペースの正規化
        };

        foreach (var test in testCases)
        {
            var normalized = _normalizer.Normalize(test.Key);
            if (normalized != test.Value)
            {
                _logger.LogWarning(
                    "正規化の不一致: {Input} → {Actual} (期待値: {Expected})",
                    test.Key, normalized, test.Value);
            }
            else
            {
                _logger.LogDebug(
                    "正規化OK: {Input} → {Output}",
                    test.Key, normalized);
            }
        }
    }

    /// <summary>
    /// トークナイザーが初期化されているかどうか
    /// </summary>
    public bool IsInitialized => !_disposed && _innerTokenizer != null;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの破棄
    /// </summary>
    /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Microsoft.ML.Tokenizersのオブジェクトは明示的なDisposeが不要
                _logger.LogDebug("RealSentencePieceTokenizerを破棄しました: {ModelName}", _modelName);
            }
            _disposed = true;
        }
    }
}
