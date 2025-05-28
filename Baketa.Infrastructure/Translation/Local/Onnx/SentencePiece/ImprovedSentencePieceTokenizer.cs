using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Microsoft.ML.Tokenizers v0.21.0を使用した改良版SentencePieceトークナイザー実装
/// </summary>
public class ImprovedSentencePieceTokenizer : Baketa.Core.Translation.Models.ITokenizer, IDisposable
{
    private readonly object? _innerTokenizer;
    private readonly ILogger<ImprovedSentencePieceTokenizer> _logger;
    private readonly string _modelName;
    private readonly int _maxInputLength;
    private bool _disposed;

    /// <inheritdoc/>
    public string TokenizerId { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int VocabularySize { get; private set; } = 32000;

    /// <summary>
    /// モデルファイルのパス
    /// </summary>
    public string ModelPath { get; }

    /// <summary>
    /// 実際のSentencePieceTokenizerが利用可能かどうか
    /// </summary>
    public bool IsRealSentencePieceAvailable { get; private set; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">SentencePiece モデルファイルのパス</param>
    /// <param name="logger">ロガー</param>
    /// <param name="maxInputLength">最大入力長（デフォルト: 10000文字）</param>
    public ImprovedSentencePieceTokenizer(
        string modelPath,
        ILogger<ImprovedSentencePieceTokenizer> logger,
        int maxInputLength = 10000)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath, nameof(modelPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        ModelPath = modelPath;
        _modelName = Path.GetFileNameWithoutExtension(modelPath);
        _maxInputLength = maxInputLength;
        
        TokenizerId = $"ImprovedSentencePiece_{_modelName}";
        Name = $"Improved SentencePiece Tokenizer ({_modelName})";
        
        try
        {
            // ファイルの存在チェック
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"モデルファイルが見つかりません: {modelPath}");
            }

            // Microsoft.ML.Tokenizers v0.21.0を使用してSentencePieceTokenizerを作成
            (_innerTokenizer, IsRealSentencePieceAvailable) = CreateSentencePieceTokenizer(modelPath);
            
            if (IsRealSentencePieceAvailable)
            {
                _logger.LogInformation(
                    "実際のSentencePieceTokenizerを初期化しました: {ModelPath}",
                    modelPath);
                    
                // 語彙サイズを取得（可能な場合）
                VocabularySize = TryGetVocabularySize(_innerTokenizer) ?? 32000;
            }
            else
            {
                _logger.LogWarning(
                    "実際のSentencePieceTokenizerの作成に失敗しました。暫定実装を使用します: {ModelPath}", 
                    modelPath);
            }
        }
        catch (Exception ex)
        {
            // Dispose時にIsInitializedをfalseに設定（コンストラクタではIsInitializedは変更しない）
            _logger.LogError(ex,
                "SentencePieceTokenizerの初期化に失敗しました: {ModelPath}",
                modelPath);
            throw new InvalidOperationException(
                $"SentencePieceトークナイザーの初期化に失敗しました: {modelPath}", ex);
        }
    }

    /// <inheritdoc/>
    public int[] Tokenize(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ImprovedSentencePieceTokenizer));
        ArgumentNullException.ThrowIfNull(text, nameof(text));

        try
        {
            if (string.IsNullOrEmpty(text))
            {
                return [];
            }
            
            // 入力検証
            if (text.Length > _maxInputLength)
            {
                throw new TokenizationException(
                    $"入力テキストが最大長({_maxInputLength}文字)を超えています",
                    text,
                    _modelName);
            }

            if (IsRealSentencePieceAvailable && _innerTokenizer != null)
            {
                // 実際のSentencePieceTokenizerを使用
                return EncodeWithReflection(_innerTokenizer, text);
            }
            else
            {
                // 暫定実装: 単純な単語分割
                return FallbackTokenize(text);
            }
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
        ObjectDisposedException.ThrowIf(_disposed, nameof(ImprovedSentencePieceTokenizer));
        ArgumentNullException.ThrowIfNull(tokens, nameof(tokens));

        try
        {
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            if (IsRealSentencePieceAvailable && _innerTokenizer != null)
            {
                // 実際のSentencePieceTokenizerを使用
                return DecodeWithReflection(_innerTokenizer, tokens);
            }
            else
            {
                // 暫定実装: トークンIDを文字列に変換
                return FallbackDecode(tokens);
            }
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
        ObjectDisposedException.ThrowIf(_disposed, nameof(ImprovedSentencePieceTokenizer));

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
    /// 特殊トークンの情報を取得
    /// </summary>
    /// <returns>特殊トークンの情報</returns>
    public SpecialTokens GetSpecialTokens()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ImprovedSentencePieceTokenizer));

        SpecialTokens specialTokens = new();

        if (IsRealSentencePieceAvailable && _innerTokenizer != null)
        {
            try
            {
                // リフレクションを使用して特殊トークンを取得
                specialTokens = GetSpecialTokensWithReflection(_innerTokenizer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "特殊トークンの取得に失敗しました。デフォルト値を使用します");
                specialTokens = GetDefaultSpecialTokens();
            }
        }
        else
        {
            specialTokens = GetDefaultSpecialTokens();
        }

        return specialTokens;
    }

    /// <summary>
    /// トークナイザーが初期化されているかどうか
    /// </summary>
    public bool IsInitialized { get; private set; } = true;

    private (object? tokenizer, bool isAvailable) CreateSentencePieceTokenizer(string modelPath)
    {
        try
        {
            var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");
            if (type == null)
            {
                _logger.LogWarning("Microsoft.ML.Tokenizers.SentencePieceTokenizer型が見つかりません");
                return (null, false);
            }

            var createMethod = type.GetMethod("Create", [
                typeof(Stream),
                typeof(bool),
                typeof(bool),
                typeof(System.Collections.Generic.IReadOnlyDictionary<string, int>)
            ]);
            
            if (createMethod == null)
            {
                _logger.LogWarning("SentencePieceTokenizer.Createメソッドが見つかりません");
                return (null, false);
            }

            using var stream = File.OpenRead(modelPath);
            var tokenizer = createMethod.Invoke(null, [stream, true, false, null!]);
            
            if (tokenizer != null)
            {
                return (tokenizer, true);
            }
            else
            {
                _logger.LogWarning("SentencePieceTokenizer.Createがnullを返しました");
                return (null, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SentencePieceTokenizerの作成に失敗しました（予想される動作）");
            return (null, false);
        }
    }

    private int[] EncodeWithReflection(object tokenizer, string text)
    {
        var type = tokenizer.GetType();
        var encodeMethod = type.GetMethod("Encode", [typeof(string)]);
        
        if (encodeMethod == null)
        {
            throw new InvalidOperationException("Encodeメソッドが見つかりません");
        }

        var result = encodeMethod.Invoke(tokenizer, [text]);
        if (result == null)
        {
            throw new InvalidOperationException("Encodeメソッドがnullを返しました");
        }

        // EncodeResultからIdsを取得
        var idsProperty = result.GetType().GetProperty("Ids");
        if (idsProperty == null)
        {
            throw new InvalidOperationException("EncodeResult.Idsプロパティが見つかりません");
        }

        var ids = idsProperty.GetValue(result);
        if (ids == null)
        {
            throw new InvalidOperationException("EncodeResult.Idsプロパティがnullを返しました");
        }
        
        return ids switch
        {
            System.Collections.Generic.IReadOnlyList<int> idsList => [.. idsList],
            System.Collections.Generic.IEnumerable<int> idsEnumerable => [.. idsEnumerable],
            _ => throw new InvalidOperationException($"予期しないIds型: {ids.GetType().Name}")
        };
    }

    private string DecodeWithReflection(object tokenizer, int[] tokens)
    {
        var type = tokenizer.GetType();
        var decodeMethod = type.GetMethod("Decode", [typeof(int[])]);
        
        if (decodeMethod == null)
        {
            // IReadOnlyList<int>を受け取るオーバーロードを試す
            decodeMethod = type.GetMethod("Decode", [typeof(System.Collections.Generic.IReadOnlyList<int>)]);
        }
        
        if (decodeMethod == null)
        {
            throw new InvalidOperationException("Decodeメソッドが見つかりません");
        }

        var result = decodeMethod.Invoke(tokenizer, [tokens]);
        
        if (result is string decodedString)
        {
            return decodedString;
        }
        else
        {
            throw new InvalidOperationException($"予期しないDecodeメソッドの戻り値型: {result?.GetType().Name ?? "null"}");
        }
    }

    private SpecialTokens GetSpecialTokensWithReflection(object tokenizer)
    {
        // Microsoft.ML.Tokenizers v0.21.0での特殊トークン取得を試行
        // APIが不明な場合はデフォルト値を使用
        SpecialTokens specialTokens = GetDefaultSpecialTokens();

        try
        {
            var type = tokenizer.GetType();
            
            // 各種特殊トークンIDを取得する試み
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            foreach (var prop in properties)
            {
                var value = prop.GetValue(tokenizer);
                
                switch (prop.Name.ToUpperInvariant())
                {
                    case "UNKNOWNTOKENID" when value is int unkId:
                        specialTokens.UnknownId = unkId;
                        break;
                    case "BEGINNINGOFSENTENCETOKENID" when value is int bosId:
                        specialTokens.BeginOfSentenceId = bosId;
                        break;
                    case "ENDOFSENTENCETOKENID" when value is int eosId:
                        specialTokens.EndOfSentenceId = eosId;
                        break;
                    case "PADDINGTOKENID" when value is int padId:
                        specialTokens.PaddingId = padId;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "リフレクションによる特殊トークン取得に失敗しました");
        }

        return specialTokens;
    }

    private static SpecialTokens GetDefaultSpecialTokens() => new()
    {
        UnknownId = 0,          // <unk>
        BeginOfSentenceId = 1,  // <s>
        EndOfSentenceId = 2,    // </s>
        PaddingId = 3           // <pad>
    };

    private int? TryGetVocabularySize(object? tokenizer)
    {
        if (tokenizer == null)
            return null;

        try
        {
            var type = tokenizer.GetType();
            var vocabSizeProperty = type.GetProperty("VocabularySize") ?? 
                                  type.GetProperty("VocabSize") ?? 
                                  type.GetProperty("Size");
            
            if (vocabSizeProperty != null)
            {
                var value = vocabSizeProperty.GetValue(tokenizer);
                if (value is int vocabSize && vocabSize > 0)
                {
                    return vocabSize;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "語彙サイズの取得に失敗しました");
        }

        return null;
    }

    private int[] FallbackTokenize(string text)
    {
        // 暫定実装: 単純な単語分割
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<int> tokens = [];
        
        foreach (var word in words)
        {
#pragma warning disable CA1307 // StringComparison を指定します - GetHashCodeにはStringComparisonパラメーターがありません
            var tokenId = Math.Abs(word.GetHashCode()) % VocabularySize;
#pragma warning restore CA1307
            tokens.Add(tokenId);
        }
        
        _logger.LogDebug(
            "暫定実装でテキストをトークン化しました: 入力長={InputLength}, トークン数={TokenCount}",
            text.Length, tokens.Count);
            
        return [.. tokens];
    }

    private string FallbackDecode(int[] tokens)
    {
        // 暫定実装: トークンIDを文字列に変換
        var words = tokens.Select(token => $"tok_{token}");
        var decoded = string.Join(" ", words);
        
        _logger.LogDebug(
            "暫定実装でトークンをデコードしました: トークン数={TokenCount}, 出力長={OutputLength}",
            tokens.Length, decoded.Length);
            
        return decoded;
    }

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
                if (_innerTokenizer is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _logger.LogDebug("ImprovedSentencePieceTokenizerを破棄しました: {ModelName}", _modelName);
            }
            _disposed = true;
            IsInitialized = false;
        }
    }
}

#pragma warning restore CA1031 // Do not catch general exception types
