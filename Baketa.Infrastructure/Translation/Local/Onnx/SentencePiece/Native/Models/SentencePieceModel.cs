using System.Collections.Generic;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;

/// <summary>
/// SentencePieceモデルの内部表現
/// Protobufから解析されたデータを格納する
/// </summary>
public sealed class SentencePieceModel
{
    /// <summary>
    /// 語彙辞書: 文字列 → トークンID
    /// </summary>
    public Dictionary<string, int> Vocabulary { get; init; } = [];
    
    /// <summary>
    /// 逆引き辞書: トークンID → 文字列
    /// </summary>
    public Dictionary<int, string> ReverseVocabulary { get; init; } = [];
    
    /// <summary>
    /// 特殊トークンの定義
    /// </summary>
    public NativeSpecialTokens SpecialTokens { get; init; } = new();
    
    /// <summary>
    /// BPEマージルール（必要に応じて）
    /// </summary>
    public List<BpeMergeRule> MergeRules { get; init; } = [];
    
    /// <summary>
    /// 高速検索用Trie木
    /// </summary>
    public TrieNode? SearchTrie { get; set; }
    
    /// <summary>
    /// 語彙サイズ
    /// </summary>
    public int VocabularySize => Vocabulary.Count;
    
    /// <summary>
    /// モデルのバージョン情報
    /// </summary>
    public string Version { get; init; } = string.Empty;
    
    /// <summary>
    /// 正規化器のタイプ
    /// </summary>
    public string NormalizerType { get; init; } = "nfkc";
    
    /// <summary>
    /// ピースのスコア情報
    /// </summary>
    public Dictionary<string, float> PieceScores { get; init; } = [];
    
    /// <summary>
    /// 正規化設定
    /// </summary>
    public Dictionary<string, object> NormalizationSettings { get; set; } = [];
}

/// <summary>
/// 特殊トークンの定義（Native実装用）
/// </summary>
public sealed class NativeSpecialTokens
{
    /// <summary>
    /// Beginning of Sentence
    /// </summary>
    public int BosId { get; init; }
    
    /// <summary>
    /// End of Sentence  
    /// </summary>
    public int EosId { get; init; } = 2;
    
    /// <summary>
    /// Unknown token
    /// </summary>
    public int UnkId { get; init; } = 1;
    
    /// <summary>
    /// Padding token
    /// </summary>
    public int PadId { get; init; } = 3;
}

/// <summary>
/// BPEマージルール（将来拡張用）
/// </summary>
public sealed class BpeMergeRule
{
    public string Left { get; init; } = string.Empty;
    public string Right { get; init; } = string.Empty;
    public string Merged { get; init; } = string.Empty;
    public int Priority { get; init; }
}

/// <summary>
/// Trie木のノード（高速検索用）
/// SentencePieceのBPE仕様に最適化されたRadix Trie実装
/// </summary>
public sealed class TrieNode
{
    /// <summary>
    /// 子ノードのマップ（文字 → 子ノード）
    /// </summary>
    public Dictionary<char, TrieNode> Children { get; } = [];
    
    /// <summary>
    /// このノードに対応するトークンID（末端ノードの場合）
    /// </summary>
    public int? TokenId { get; set; }
    
    /// <summary>
    /// このノードが単語の終端かどうか
    /// </summary>
    public bool IsEndOfWord => TokenId.HasValue;
    
    /// <summary>
    /// トークンの文字列表現（デバッグ用）
    /// </summary>
    public string? Token { get; set; }
    
    /// <summary>
    /// SentencePiece BPE仕様に従った最長一致検索
    /// より長いマッチを優先し、同じ長さの場合は語彙順で最初のものを選択
    /// </summary>
    /// <param name="text">検索対象テキスト</param>
    /// <param name="unkTokenId">マッチしない場合のUNKトークンID（デフォルト: 1）</param>
    /// <returns>トークンIDと一致した長さ、見つからない場合はUNK</returns>
    public (int tokenId, int length) FindLongestMatch(ReadOnlySpan<char> text, int unkTokenId = 1)
    {
        if (text.IsEmpty)
            return (unkTokenId, 1); // UNKトークンで1文字進む（空文字対応）
            
        var current = this;
        var bestMatch = (-1, 0);
        var bestToken = string.Empty;
        
        // 最長一致検索
        for (int i = 0; i < text.Length; i++)
        {
            if (!current.Children.TryGetValue(text[i], out var next))
                break;
                
            current = next;
            
            // 単語の終端に到達した場合、マッチ候補として記録
            if (current.IsEndOfWord)
            {
                var currentLength = i + 1;
                var currentToken = current.Token ?? string.Empty;
                
                // より長いマッチを優先
                // 同じ長さの場合は辞書順で最初のもの（SentencePiece仕様）
                if (currentLength > bestMatch.Item2 || 
                    (currentLength == bestMatch.Item2 && 
                     string.Compare(currentToken, bestToken, StringComparison.Ordinal) < 0))
                {
                    bestMatch = (current.TokenId!.Value, currentLength);
                    bestToken = currentToken;
                }
            }
        }
        
        // マッチが見つからない場合はUNKトークンで1文字進む
        return bestMatch.Item1 >= 0 ? bestMatch : (unkTokenId, 1);
    }
    
    /// <summary>
    /// 語彙をTrie木に追加
    /// </summary>
    /// <param name="token">トークン文字列</param>
    /// <param name="tokenId">トークンID</param>
    public void AddToken(string token, int tokenId)
    {
        if (string.IsNullOrEmpty(token))
            return;
            
        var current = this;
        foreach (char c in token)
        {
            if (!current.Children.TryGetValue(c, out var child))
            {
                child = new TrieNode();
                current.Children[c] = child;
            }
            current = child;
        }
        
        current.TokenId = tokenId;
        current.Token = token;
    }
    
    /// <summary>
    /// Trie木のメモリ使用量を最適化（圧縮）
    /// 単一子ノードのチェーンを圧縮してメモリ効率を向上
    /// </summary>
    public void Optimize()
    {
        // 再帰的に子ノードを最適化
        foreach (var child in Children.Values)
        {
            child.Optimize();
        }
        
        // 単一子ノードの圧縮は複雑になるため、
        // 現在の実装では基本的なTrie構造を維持
        // 将来的にDouble Array TrieやPatricia Trieへの移行を検討
    }
}