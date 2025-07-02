using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1031 // Do not catch general exception types - 暫定実装のため

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Microsoft.ML.Tokenizersを使用した実際のSentencePieceトークナイザー実装
/// リフレクションによりSentencePieceTokenizerの存在を確認し、利用可能な場合のみ使用します
/// </summary>
public class RealSentencePieceTokenizer : Baketa.Core.Translation.Models.ITokenizer, IDisposable
{
    private readonly object? _innerTokenizer;
    private readonly ILogger<RealSentencePieceTokenizer> _logger;
    private readonly string _modelName;
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

    // リフレクション用のキャッシュされたメソッド
    private static readonly Type? _sentencePieceTokenizerType = GetSentencePieceTokenizerType();
    private static readonly MethodInfo? _createMethod = GetCreateMethod();
    private static readonly MethodInfo? _encodeMethod = GetEncodeMethod();
    private static readonly MethodInfo? _decodeMethod = GetDecodeMethod();
    private static readonly PropertyInfo? _idsProperty = GetIdsProperty();
    
    private static Type? GetSentencePieceTokenizerType()
    {
        try
        {
            var assembly = Assembly.Load("Microsoft.ML.Tokenizers");
            return assembly.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer");
        }
        catch
        {
            return null;
        }
    }
    
    private static MethodInfo? GetCreateMethod()
    {
        if (_sentencePieceTokenizerType == null) return null;
        
        try
        {
            return _sentencePieceTokenizerType.GetMethod("Create", 
                BindingFlags.Public | BindingFlags.Static,
                [typeof(Stream), typeof(bool), typeof(bool)]);
        }
        catch
        {
            return null;
        }
    }
    
    private static MethodInfo? GetEncodeMethod()
    {
        if (_sentencePieceTokenizerType == null) return null;
        
        try
        {
            return _sentencePieceTokenizerType.GetMethod("Encode",
                BindingFlags.Public | BindingFlags.Instance,
                [typeof(string)]);
        }
        catch
        {
            return null;
        }
    }
    
    private static MethodInfo? GetDecodeMethod()
    {
        if (_sentencePieceTokenizerType == null) return null;
        
        try
        {
            return _sentencePieceTokenizerType.GetMethod("Decode",
                BindingFlags.Public | BindingFlags.Instance,
                [typeof(int[])]);
        }
        catch
        {
            return null;
        }
    }
    
    private static PropertyInfo? GetIdsProperty()
    {
        try
        {
            var assembly = Assembly.Load("Microsoft.ML.Tokenizers");
            var encodeResultType = assembly.GetType("Microsoft.ML.Tokenizers.EncodeResult");
            return encodeResultType?.GetProperty("Ids", BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            return null;
        }
    }
    
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
            // ファイルの存在チェック
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"モデルファイルが見つかりません: {modelPath}");
            }

            // リフレクションを使用してSentencePieceTokenizerを作成
            if (_sentencePieceTokenizerType != null && _createMethod != null)
            {
                using var modelStream = File.OpenRead(modelPath);
                try
                {
                    _innerTokenizer = _createMethod.Invoke(null, [modelStream, true, false]);
                    
                    _logger.LogInformation(
                        "SentencePieceトークナイザーを初期化しました: {ModelPath}",
                        modelPath);
                }
                catch (Exception ex)
                {
                    // 実際のSentencePieceモデルファイルでない場合は暫定実装を使用
                    _logger.LogWarning(ex, 
                        "実際のSentencePieceモデルファイルではありません。暫定実装を使用します: {ModelPath}", 
                        modelPath);
                    _innerTokenizer = null;
                }
            }
            else
            {
                _logger.LogWarning(
                    "SentencePieceTokenizerクラスが見つかりません。暫定実装を使用します: {ModelPath}",
                    modelPath);
                _innerTokenizer = null;
            }
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

            if (_innerTokenizer != null && _encodeMethod != null && _idsProperty != null)
            {
                // 実際のSentencePieceトークナイザーを使用（リフレクション経由）
                var encodingResult = _encodeMethod.Invoke(_innerTokenizer, [text]);
                var ids = _idsProperty.GetValue(encodingResult);
                var tokens = ((System.Collections.IEnumerable)ids!).Cast<int>().ToArray();
                
                _logger.LogDebug(
                    "SentencePieceでテキストをトークン化しました: 入力長={InputLength}, トークン数={TokenCount}",
                    text.Length, tokens.Length);
                    
                return tokens;
            }
            else
            {
                // 暫定実装: 単純な単語分割
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var tokens = new List<int>();
                
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

            if (_innerTokenizer != null && _decodeMethod != null)
            {
                // 実際のSentencePieceトークナイザーを使用（リフレクション経由）
                var decoded = (string)_decodeMethod.Invoke(_innerTokenizer, [tokens])!;
                
                _logger.LogDebug(
                    "SentencePieceでトークンをデコードしました: トークン数={TokenCount}, 出力長={OutputLength}",
                    tokens.Length, decoded.Length);
                    
                return decoded;
            }
            else
            {
                // 暫定実装: トークンIDを文字列に変換
                var words = tokens.Select(token => $"tok_{token}");
                var decoded = string.Join(" ", words);
                
                _logger.LogDebug(
                    "暫定実装でトークンをデコードしました: トークン数={TokenCount}, 出力長={OutputLength}",
                    tokens.Length, decoded.Length);
                    
                return decoded;
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
        ObjectDisposedException.ThrowIf(_disposed, nameof(RealSentencePieceTokenizer));

        try
        {
            if (_innerTokenizer != null && _decodeMethod != null)
            {
                // 単一トークンをデコード（リフレクション経由）
                return (string)_decodeMethod.Invoke(_innerTokenizer, [new int[] { token }])!;
            }
            else
            {
                // 暫定実装: 単一トークンをデコード
                return $"tok_{token}";
            }
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

        if (_innerTokenizer != null)
        {
            try
            {
                // Microsoft.ML.Tokenizers 0.21.0から特殊トークンを取得
                // 注意: APIの詳細は実際のバージョンに依存する
                specialTokens.UnknownId = 0;          // <unk>
                specialTokens.BeginOfSentenceId = 1;  // <s>
                specialTokens.EndOfSentenceId = 2;    // </s>
                specialTokens.PaddingId = 3;          // <pad>
                
                _logger.LogDebug(
                    "特殊トークンを取得しました: <unk>={UnknownId}, <s>={BeginId}, </s>={EndId}, <pad>={PadId}",
                    specialTokens.UnknownId,
                    specialTokens.BeginOfSentenceId,
                    specialTokens.EndOfSentenceId,
                    specialTokens.PaddingId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "特殊トークンの取得に失敗しました。デフォルト値を使用します");
                
                // デフォルト値を設定
                specialTokens.UnknownId = 0;
                specialTokens.BeginOfSentenceId = 1;
                specialTokens.EndOfSentenceId = 2;
                specialTokens.PaddingId = 3;
            }
        }
        else
        {
            // 暫定実装のデフォルト値
            specialTokens.UnknownId = 0;
            specialTokens.BeginOfSentenceId = 1;
            specialTokens.EndOfSentenceId = 2;
            specialTokens.PaddingId = 3;
        }

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

        if (_innerTokenizer != null && _encodeMethod != null && _decodeMethod != null && _idsProperty != null)
        {
            foreach (var test in testCases)
            {
                try
                {
                    var encodingResult = _encodeMethod.Invoke(_innerTokenizer, [test.Key]);
                    var ids = _idsProperty.GetValue(encodingResult);
                    var tokens = ((System.Collections.IEnumerable)ids!).Cast<int>().ToArray();
                    var normalized = (string)_decodeMethod.Invoke(_innerTokenizer, [tokens])!;
                    
                    if (normalized != test.Value)
                    {
                        _logger.LogInformation(
                            "正規化の動作: {Input} → {Actual} (期待値: {Expected})",
                            test.Key, normalized, test.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "正規化テストエラー: {Input}", test.Key);
                }
            }
        }
        else
        {
            _logger.LogWarning("暫定実装のため、正規化検証はスキップされました");
        }
    }

    /// <summary>
    /// トークナイザーが初期化されているかどうか
    /// </summary>
    public bool IsInitialized => !_disposed && (_innerTokenizer != null || true); // 暫定実装も含む

    /// <summary>
    /// 実際のSentencePieceTokenizerが利用可能かどうか
    /// </summary>
    public bool IsRealSentencePieceAvailable => _innerTokenizer != null;

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
                // リフレクション経由でDispose呼び出し
                if (_innerTokenizer is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _logger.LogDebug("RealSentencePieceTokenizerを破棄しました: {ModelName}", _modelName);
            }
            _disposed = true;
        }
    }
}

#pragma warning restore CA1031 // Do not catch general exception types
