using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Optimizations;

/// <summary>
/// メモリ使用量を最適化したOPUS-MT Nativeトークナイザー
/// オブジェクトプーリング、バッファ再利用、メモリ効率化を実装
/// </summary>
public sealed class MemoryOptimizedOpusMtTokenizer : ITokenizer, IDisposable
{
    private readonly MemoryOptimizedSentencePieceModel _model;
    private readonly MemoryOptimizedBpeTokenizer _bpeTokenizer;
    private readonly ILogger<MemoryOptimizedOpusMtTokenizer> _logger;
    private readonly string _tokenizerId;
    
    // バッファプール（メモリ再利用）
    private readonly ArrayPool<int> _tokenPool = ArrayPool<int>.Shared;
    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;
    
    // 文字列処理バッファ（再利用）
    private readonly StringBuilder _stringBuilder = new(256);
    
    private bool _disposed;
    private bool _isInitialized;

    /// <inheritdoc/>
    public string TokenizerId => _tokenizerId;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int VocabularySize => _model?.VocabularySize ?? 0;

    /// <summary>
    /// 初期化状態
    /// </summary>
    public bool IsInitialized => _isInitialized && !_disposed;
    
    /// <summary>
    /// 現在のメモリ使用量統計
    /// </summary>
    public MemoryStatistics MemoryStatistics => _model?.GetMemoryStatistics() ?? default;

    /// <summary>
    /// コンストラクタ（内部使用）
    /// </summary>
    internal MemoryOptimizedOpusMtTokenizer(
        MemoryOptimizedSentencePieceModel model,
        string name,
        ILogger<MemoryOptimizedOpusMtTokenizer> logger)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        Name = name;
        _tokenizerId = GenerateOptimizedTokenizerId();
        _logger = logger;
        
        _bpeTokenizer = new MemoryOptimizedBpeTokenizer(_model, logger);
        _isInitialized = true;
    }

    /// <summary>
    /// メモリ最適化済みトークナイザーの作成
    /// </summary>
    public static async Task<MemoryOptimizedOpusMtTokenizer> CreateOptimizedAsync(string modelPath)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning)); // ログレベル最適化
            
        var parserLogger = loggerFactory.CreateLogger<SentencePieceModelParser>();
        var tokenizerLogger = loggerFactory.CreateLogger<MemoryOptimizedOpusMtTokenizer>();
        
        // 元モデルのパース
        using var parser = new SentencePieceModelParser(parserLogger);
        var originalModel = await parser.ParseModelAsync(modelPath).ConfigureAwait(false);
        
        // メモリ最適化モデルの構築
        var optimizedModel = new MemoryOptimizedSentencePieceModel(originalModel);
        
        try
        {
            return new MemoryOptimizedOpusMtTokenizer(
                optimizedModel, 
                "OPUS-MT Memory-Optimized Tokenizer", 
                tokenizerLogger);
        }
        catch
        {
            optimizedModel.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public int[] Tokenize(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized)
        {
            _logger.LogWarning("Tokenizer not initialized");
            return [];
        }

        if (string.IsNullOrEmpty(text))
            return [];

        try
        {
            // BPEトークン化（メモリ最適化版）
            var tokens = _bpeTokenizer.TokenizeBpeOptimized(text.AsSpan());
            
            // 特殊トークン追加（インプレース最適化）
            return AddSpecialTokensOptimized(tokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Optimized tokenization failed");
            throw new InvalidOperationException($"Tokenization failed: {text[..Math.Min(50, text.Length)]}", ex);
        }
    }

    /// <inheritdoc/>
    public string Decode(int[] tokens)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized || tokens.Length == 0)
            return string.Empty;

        try
        {
            return _bpeTokenizer.DecodeOptimized(tokens, _stringBuilder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Optimized decoding failed");
            throw new InvalidOperationException("Decoding failed", ex);
        }
    }

    /// <inheritdoc/>
    public string DecodeToken(int token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_isInitialized)
            return "<unk>";

        return _model.ReverseVocabulary.GetValueOrDefault(token, "<unk>");
    }

    /// <summary>
    /// 特殊トークンの最適化追加
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int[] AddSpecialTokensOptimized(int[] tokens)
    {
        // 配列プールからバッファを借用
        var buffer = _tokenPool.Rent(tokens.Length + 2);
        
        try
        {
            buffer[0] = _model.SpecialTokens.BosId;
            tokens.CopyTo(buffer.AsSpan(1));
            buffer[tokens.Length + 1] = _model.SpecialTokens.EosId;
            
            // 結果配列を作成
            var result = new int[tokens.Length + 2];
            buffer.AsSpan(0, result.Length).CopyTo(result);
            
            return result;
        }
        finally
        {
            // バッファを返却
            _tokenPool.Return(buffer);
        }
    }

    /// <summary>
    /// メモリ使用量の最適化実行
    /// </summary>
    public void OptimizeMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _model.OptimizeMemory();
        _bpeTokenizer.OptimizeMemory();
        
        // StringBuilder容量の最適化
        if (_stringBuilder.Capacity > 1024)
        {
            _stringBuilder.Clear();
            _stringBuilder.Capacity = 256; // 初期容量にリセット
        }
    }

    /// <summary>
    /// 最適化されたTokenizerIDの生成
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GenerateOptimizedTokenizerId()
    {
        // GUIDの代わりに軽量なIDを生成
        return $"opus-mt-opt-{Environment.TickCount64:X}";
    }

    /// <summary>
    /// メモリ使用量のレポート
    /// </summary>
    public string GetMemoryReport()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var stats = MemoryStatistics;
        return $"MemoryOptimizedOpusMtTokenizer: {stats}";
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _bpeTokenizer?.Dispose();
        _model?.Dispose();
        
        _stringBuilder.Clear();
        
        _disposed = true;
        _isInitialized = false;
        
        _logger.LogDebug("MemoryOptimizedOpusMtTokenizer disposed: {TokenizerId}", TokenizerId);
    }
}

/// <summary>
/// メモリ最適化されたBPEトークナイザー
/// </summary>
public sealed class MemoryOptimizedBpeTokenizer : IDisposable
{
    private readonly MemoryOptimizedSentencePieceModel _model;
    private readonly ILogger _logger;
    private readonly ArrayPool<int> _tokenPool = ArrayPool<int>.Shared;
    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;
    
    private const char SpaceSymbol = '\u2581';
    private bool _disposed;

    public MemoryOptimizedBpeTokenizer(MemoryOptimizedSentencePieceModel model, ILogger logger)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// メモリ最適化されたBPEトークン化
    /// </summary>
    public int[] TokenizeBpeOptimized(ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (text.IsEmpty)
            return [];

        // 正規化バッファをプールから借用
        var normalizedBuffer = _charPool.Rent(text.Length * 2); // 余裕を持ったサイズ
        
        try
        {
            // インプレース正規化
            var normalizedLength = NormalizeSentencePieceOptimized(text, normalizedBuffer);
            var normalizedSpan = normalizedBuffer.AsSpan(0, normalizedLength);
            
            // トークン化バッファをプールから借用
            var tokenBuffer = _tokenPool.Rent(normalizedLength); // 最大ケース
            var tokenCount = 0;
            
            try
            {
                // 最長一致トークン化
                var pos = 0;
                while (pos < normalizedLength)
                {
                    var (tokenId, length) = _model.SearchTrie.FindLongestMatch(
                        normalizedSpan[pos..], _model.SpecialTokens.UnkId);
                    
                    tokenBuffer[tokenCount++] = tokenId;
                    pos += Math.Max(1, length);
                }
                
                // 結果配列の作成
                var result = new int[tokenCount];
                tokenBuffer.AsSpan(0, tokenCount).CopyTo(result);
                return result;
            }
            finally
            {
                _tokenPool.Return(tokenBuffer);
            }
        }
        finally
        {
            _charPool.Return(normalizedBuffer);
        }
    }

    /// <summary>
    /// メモリ最適化されたデコード
    /// </summary>
    public string DecodeOptimized(int[] tokenIds, StringBuilder stringBuilder)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (tokenIds.Length == 0)
            return string.Empty;

        // StringBuilderをクリアして再利用
        stringBuilder.Clear();
        
        foreach (int tokenId in tokenIds)
        {
            // 特殊トークンはスキップ
            if (IsSpecialToken(tokenId))
                continue;
                
            if (_model.ReverseVocabulary.TryGetValue(tokenId, out string? token))
            {
                // インプレース文字置換（StringBuilderを活用）
                foreach (char c in token)
                {
                    stringBuilder.Append(c == SpaceSymbol ? ' ' : c);
                }
            }
            else
            {
                var unkToken = _model.ReverseVocabulary.GetValueOrDefault(_model.SpecialTokens.UnkId, "<unk>");
                stringBuilder.Append(unkToken);
            }
        }
        
        // 前後の空白を除去
        while (stringBuilder.Length > 0 && char.IsWhiteSpace(stringBuilder[0]))
        {
            stringBuilder.Remove(0, 1);
        }
        
        while (stringBuilder.Length > 0 && char.IsWhiteSpace(stringBuilder[^1]))
        {
            stringBuilder.Length--;
        }
        
        return stringBuilder.ToString();
    }

    /// <summary>
    /// メモリ最適化された正規化処理
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NormalizeSentencePieceOptimized(ReadOnlySpan<char> input, Span<char> output)
    {
        if (input.IsEmpty)
            return 0;

        var writePos = 0;
        var addedSpacePrefix = false;
        
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            
            // 先頭または空白後の▁記号追加
            if (i == 0 || (i > 0 && char.IsWhiteSpace(input[i - 1])))
            {
                if (!addedSpacePrefix || i > 0)
                {
                    output[writePos++] = SpaceSymbol;
                    addedSpacePrefix = true;
                }
            }
            
            // 空白以外の文字をコピー
            if (!char.IsWhiteSpace(c))
            {
                // NFKC正規化の簡易版（主要文字のみ）
                output[writePos++] = NormalizeCharacter(c);
            }
        }
        
        return writePos;
    }

    /// <summary>
    /// 文字単位の正規化（最適化版）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char NormalizeCharacter(char c)
    {
        // 高頻度文字の高速正規化
        return c switch
        {
            // 全角英数字 → 半角
            '０' => '0', '１' => '1', '２' => '2', '３' => '3', '４' => '4',
            '５' => '5', '６' => '6', '７' => '7', '８' => '8', '９' => '9',
            'Ａ' => 'A', 'Ｂ' => 'B', 'Ｃ' => 'C', 'Ｄ' => 'D', 'Ｅ' => 'E',
            'Ｆ' => 'F', 'Ｇ' => 'G', 'Ｈ' => 'H', 'Ｉ' => 'I', 'Ｊ' => 'J',
            'Ｋ' => 'K', 'Ｌ' => 'L', 'Ｍ' => 'M', 'Ｎ' => 'N', 'Ｏ' => 'O',
            'Ｐ' => 'P', 'Ｑ' => 'Q', 'Ｒ' => 'R', 'Ｓ' => 'S', 'Ｔ' => 'T',
            'Ｕ' => 'U', 'Ｖ' => 'V', 'Ｗ' => 'W', 'Ｘ' => 'X', 'Ｙ' => 'Y', 'Ｚ' => 'Z',
            'ａ' => 'a', 'ｂ' => 'b', 'ｃ' => 'c', 'ｄ' => 'd', 'ｅ' => 'e',
            'ｆ' => 'f', 'ｇ' => 'g', 'ｈ' => 'h', 'ｉ' => 'i', 'ｊ' => 'j',
            'ｋ' => 'k', 'ｌ' => 'l', 'ｍ' => 'm', 'ｎ' => 'n', 'ｏ' => 'o',
            'ｐ' => 'p', 'ｑ' => 'q', 'ｒ' => 'r', 'ｓ' => 's', 'ｔ' => 't',
            'ｕ' => 'u', 'ｖ' => 'v', 'ｗ' => 'w', 'ｘ' => 'x', 'ｙ' => 'y', 'ｚ' => 'z',
            
            // その他は元の文字を維持
            _ => c
        };
    }

    /// <summary>
    /// 特殊トークンの判定
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsSpecialToken(int tokenId)
    {
        return tokenId == _model.SpecialTokens.BosId ||
               tokenId == _model.SpecialTokens.EosId ||
               tokenId == _model.SpecialTokens.PadId;
    }

    /// <summary>
    /// メモリ最適化の実行
    /// </summary>
    public void OptimizeMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // 現在の実装では追加の最適化なし
        // 将来的にバッファキャッシュの最適化等を追加予定
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
    }
}