using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// SentencePiece互換のBPE（Byte Pair Encoding）トークナイザー
/// Trie木ベースの高速最長一致検索を実装
/// </summary>
public sealed class BpeTokenizer(SentencePieceModel model, ILogger<BpeTokenizer> logger) : IDisposable
{
    private readonly SentencePieceModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly ILogger<BpeTokenizer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private bool _disposed;

    /// <summary>
    /// SentencePiece特有の先頭スペース記号（U+2581 Lower Half Block）
    /// </summary>
    private const char SpaceSymbol = '\u2581';

    /// <summary>
    /// テキストをSentencePiece互換の方法でトークン化
    /// </summary>
    /// <param name="text">入力テキスト</param>
    /// <returns>トークンID配列</returns>
    public int[] TokenizeBpe(ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (text.IsEmpty)
            return [];

        try
        {
            // 1. SentencePiece互換の前処理
            var normalized = NormalizeSentencePieceCompatible(text);
            
            // 2. Trie木による最長一致検索
            var tokens = new List<int>();
            var pos = 0;
            
            while (pos < normalized.Length)
            {
                if (_model.SearchTrie == null)
                {
                    _logger.LogWarning("Search trie not initialized, using fallback tokenization");
                    return FallbackTokenization(normalized);
                }
                
                // Trie木による最長一致検索（UNKトークンIDを動的に指定）
                var (tokenId, length) = _model.SearchTrie.FindLongestMatch(
                    normalized.AsSpan(pos), _model.SpecialTokens.UnkId);
                
                tokens.Add(tokenId);
                pos += Math.Max(1, length); // 最低1文字は進む
            }
            
            return [.. tokens];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BPEトークン化処理でエラーが発生しました");
            throw new InvalidOperationException("BPE tokenization failed", ex);
        }
    }

    /// <summary>
    /// トークンIDを文字列にデコード
    /// </summary>
    /// <param name="tokenIds">トークンID配列</param>
    /// <returns>デコードされた文字列</returns>
    public string Decode(int[] tokenIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (tokenIds.Length == 0)
            return string.Empty;

        try
        {
            var result = new StringBuilder();
            
            foreach (int tokenId in tokenIds)
            {
                // 特殊トークンはスキップ
                if (IsSpecialToken(tokenId))
                    continue;
                    
                if (_model.ReverseVocabulary.TryGetValue(tokenId, out string? token))
                {
                    // SentencePiece特有の先頭スペース記号を通常のスペースに変換
                    var processedToken = token.Replace(SpaceSymbol, ' ');
                    result.Append(processedToken);
                }
                else
                {
                    _logger.LogWarning("Unknown token ID encountered: {TokenId}", tokenId);
                    result.Append(_model.ReverseVocabulary.GetValueOrDefault(_model.SpecialTokens.UnkId, "<unk>"));
                }
            }
            
            return result.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "トークンデコード処理でエラーが発生しました");
            throw new InvalidOperationException("Token decoding failed", ex);
        }
    }

    /// <summary>
    /// SentencePiece互換の正規化処理
    /// </summary>
    private static string NormalizeSentencePieceCompatible(ReadOnlySpan<char> input)
    {
        if (input.IsEmpty)
            return string.Empty;

        // 1. NFKC正規化（.NET標準）
        var normalized = input.ToString().Normalize(NormalizationForm.FormKC);
        
        // 2. SentencePiece特有の前処理
        var result = new StringBuilder();
        var addedSpacePrefix = false;
        
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            
            // 先頭または空白の後の文字の前に▁記号を追加
            if (i == 0 || (i > 0 && char.IsWhiteSpace(normalized[i - 1])))
            {
                if (!addedSpacePrefix || i > 0)
                {
                    result.Append(SpaceSymbol);
                    addedSpacePrefix = true;
                }
            }
            
            // 空白文字はスキップ（▁記号で置換済み）
            if (!char.IsWhiteSpace(c))
            {
                result.Append(c);
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// 特殊トークンかどうかを判定
    /// </summary>
    private bool IsSpecialToken(int tokenId)
    {
        return tokenId == _model.SpecialTokens.BosId ||
               tokenId == _model.SpecialTokens.EosId ||
               tokenId == _model.SpecialTokens.PadId;
        // UNKトークンは出力に含める
    }

    /// <summary>
    /// Trie木が利用できない場合のフォールバック実装
    /// </summary>
    private int[] FallbackTokenization(string text)
    {
        var result = new List<int>();
        
        foreach (char c in text)
        {
            var charStr = c.ToString();
            if (_model.Vocabulary.TryGetValue(charStr, out int tokenId))
            {
                result.Add(tokenId);
            }
            else
            {
                result.Add(_model.SpecialTokens.UnkId);
            }
        }
        
        return [.. result];
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        _logger.LogDebug("BpeTokenizer disposed");
    }
}
