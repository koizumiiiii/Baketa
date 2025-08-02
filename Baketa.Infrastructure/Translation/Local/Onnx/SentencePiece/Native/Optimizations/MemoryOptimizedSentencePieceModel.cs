using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Optimizations;

/// <summary>
/// メモリ最適化されたSentencePieceモデル
/// インターン化とプーリングによりメモリ使用量を削減
/// </summary>
public sealed class MemoryOptimizedSentencePieceModel : IDisposable
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly Dictionary<int, string> _reverseVocabulary;
    private readonly OptimizedTrieNode _searchTrie;
    private readonly NativeSpecialTokens _specialTokens;
    private readonly StringInternPool _internPool;
    private bool _disposed;

    /// <summary>
    /// 語彙辞書（最適化版）
    /// </summary>
    public IReadOnlyDictionary<string, int> Vocabulary => _vocabulary;
    
    /// <summary>
    /// 逆引き辞書（最適化版）
    /// </summary>
    public IReadOnlyDictionary<int, string> ReverseVocabulary => _reverseVocabulary;
    
    /// <summary>
    /// 特殊トークン
    /// </summary>
    public NativeSpecialTokens SpecialTokens => _specialTokens;
    
    /// <summary>
    /// 最適化されたTrie木
    /// </summary>
    public OptimizedTrieNode SearchTrie => _searchTrie;
    
    /// <summary>
    /// 語彙サイズ
    /// </summary>
    public int VocabularySize => _vocabulary.Count;
    
    /// <summary>
    /// メモリ使用量推定値（バイト）
    /// </summary>
    public long EstimatedMemoryUsage => CalculateMemoryUsage();

    public MemoryOptimizedSentencePieceModel(SentencePieceModel originalModel)
    {
        ArgumentNullException.ThrowIfNull(originalModel);
        
        _internPool = new StringInternPool();
        _specialTokens = originalModel.SpecialTokens;
        
        // 語彙のインターン化とメモリ効率的な格納
        _vocabulary = new Dictionary<string, int>(originalModel.Vocabulary.Count);
        _reverseVocabulary = new Dictionary<int, string>(originalModel.ReverseVocabulary.Count);
        
        OptimizeVocabulary(originalModel);
        
        // Trie木の最適化構築
        _searchTrie = new OptimizedTrieNode(_internPool);
        BuildOptimizedTrie(originalModel);
    }

    /// <summary>
    /// 語彙の最適化とインターン化
    /// </summary>
    private void OptimizeVocabulary(SentencePieceModel originalModel)
    {
        foreach (var kvp in originalModel.Vocabulary)
        {
            // 文字列のインターン化でメモリ共有
            var internedKey = _internPool.Intern(kvp.Key);
            _vocabulary[internedKey] = kvp.Value;
        }
        
        foreach (var kvp in originalModel.ReverseVocabulary)
        {
            // 逆引き辞書でも同じインターン化文字列を使用
            if (_vocabulary.ContainsKey(kvp.Value))
            {
                var internedValue = _internPool.GetInternedString(kvp.Value);
                _reverseVocabulary[kvp.Key] = internedValue ?? kvp.Value;
            }
            else
            {
                _reverseVocabulary[kvp.Key] = _internPool.Intern(kvp.Value);
            }
        }
    }

    /// <summary>
    /// 最適化されたTrie木の構築
    /// </summary>
    private void BuildOptimizedTrie(SentencePieceModel originalModel)
    {
        foreach (var kvp in originalModel.Vocabulary)
        {
            var internedToken = _internPool.GetInternedString(kvp.Key);
            if (internedToken != null)
            {
                _searchTrie.AddToken(internedToken, kvp.Value);
            }
        }
        
        // Trie木の圧縮最適化
        _searchTrie.Optimize();
    }

    /// <summary>
    /// メモリ使用量の推定計算
    /// </summary>
    private long CalculateMemoryUsage()
    {
        long usage = 0;
        
        // 語彙辞書のメモリ使用量
        usage += _vocabulary.Count * (sizeof(int) + IntPtr.Size); // Key参照 + Value
        usage += _reverseVocabulary.Count * (sizeof(int) + IntPtr.Size);
        
        // インターンプールのメモリ使用量
        usage += _internPool.EstimatedMemoryUsage;
        
        // Trie木のメモリ使用量
        usage += _searchTrie.EstimatedMemoryUsage;
        
        return usage;
    }

    /// <summary>
    /// メモリ統計の取得
    /// </summary>
    public MemoryStatistics GetMemoryStatistics()
    {
        return new MemoryStatistics
        {
            VocabularyMemory = _vocabulary.Count * (sizeof(int) + IntPtr.Size) * 2,
            TrieMemory = _searchTrie.EstimatedMemoryUsage,
            InternPoolMemory = _internPool.EstimatedMemoryUsage,
            TotalMemory = EstimatedMemoryUsage,
            VocabularyCount = VocabularySize,
            InternedStringCount = _internPool.Count,
            TrieNodeCount = _searchTrie.NodeCount
        };
    }

    /// <summary>
    /// メモリ使用量の最適化実行
    /// </summary>
    public void OptimizeMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // インターンプールの最適化
        _internPool.Optimize();
        
        // Trie木の再最適化
        _searchTrie.Optimize();
        
        // .NETガベージコレクションの最適化
        GC.Collect(2, GCCollectionMode.Optimized);
        GC.WaitForPendingFinalizers();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _searchTrie?.Dispose();
        _internPool?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// 文字列インターンプール（メモリ共有最適化）
/// </summary>
public sealed class StringInternPool : IDisposable
{
    private readonly Dictionary<string, string> _internMap = [];
    private bool _disposed;

    /// <summary>
    /// インターン化された文字列数
    /// </summary>
    public int Count => _internMap.Count;
    
    /// <summary>
    /// 推定メモリ使用量
    /// </summary>
    public long EstimatedMemoryUsage
    {
        get
        {
            long usage = 0;
            foreach (var kvp in _internMap)
            {
                usage += (kvp.Key.Length + kvp.Value.Length) * sizeof(char);
                usage += IntPtr.Size * 2; // 参照のサイズ
            }
            return usage;
        }
    }

    /// <summary>
    /// 文字列をインターン化（メモリ共有）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Intern(string str)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(str))
            return str;
            
        if (_internMap.TryGetValue(str, out var internedStr))
            return internedStr;
            
        // .NET組み込みインターン化も活用
        var systemInterned = string.IsInterned(str);
        if (systemInterned != null)
        {
            _internMap[str] = systemInterned;
            return systemInterned;
        }
        
        _internMap[str] = str;
        return str;
    }

    /// <summary>
    /// インターン化済み文字列の取得
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? GetInternedString(string str)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _internMap.GetValueOrDefault(str);
    }

    /// <summary>
    /// インターンプールの最適化
    /// </summary>
    public void Optimize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // 使用頻度の低い文字列を除去（LRU的最適化）
        // 現在の実装では基本的な重複除去のみ
        
        // var keysToRemove = new List<string>(); // 将来の最適化で使用予定
        foreach (var kvp in _internMap)
        {
            if (ReferenceEquals(kvp.Key, kvp.Value))
            {
                // 同一参照の場合は最適化済み
                continue;
            }
            
            // システムインターン化を再確認
            var systemInterned = string.IsInterned(kvp.Key);
            if (systemInterned != null)
            {
                _internMap[kvp.Key] = systemInterned;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _internMap.Clear();
        _disposed = true;
    }
}

/// <summary>
/// メモリ最適化されたTrieノード
/// </summary>
public sealed class OptimizedTrieNode : IDisposable
{
    private readonly Dictionary<char, OptimizedTrieNode> _children;
    private readonly StringInternPool _internPool;
    private string? _token;
    private int? _tokenId;
    private bool _disposed;

    /// <summary>
    /// 子ノード数
    /// </summary>
    public int ChildCount => _children.Count;
    
    /// <summary>
    /// ノード総数（再帰計算）
    /// </summary>
    public int NodeCount
    {
        get
        {
            int count = 1;
            foreach (var child in _children.Values)
                count += child.NodeCount;
            return count;
        }
    }
    
    /// <summary>
    /// 推定メモリ使用量
    /// </summary>
    public long EstimatedMemoryUsage
    {
        get
        {
            long usage = IntPtr.Size * 3; // 基本フィールド
            usage += _children.Count * (sizeof(char) + IntPtr.Size); // 子ノードマップ
            
            if (_token != null)
                usage += _token.Length * sizeof(char);
                
            foreach (var child in _children.Values)
                usage += child.EstimatedMemoryUsage;
                
            return usage;
        }
    }

    public OptimizedTrieNode(StringInternPool internPool)
    {
        _internPool = internPool ?? throw new ArgumentNullException(nameof(internPool));
        _children = [];
    }

    /// <summary>
    /// 最長一致検索（最適化版）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int tokenId, int length) FindLongestMatch(ReadOnlySpan<char> text, int unkTokenId = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (text.IsEmpty)
            return (unkTokenId, 1);
            
        var current = this;
        var bestMatch = (-1, 0);
        
        for (int i = 0; i < text.Length; i++)
        {
            if (!current._children.TryGetValue(text[i], out var next))
                break;
                
            current = next;
            
            if (current._tokenId.HasValue)
            {
                bestMatch = (current._tokenId.Value, i + 1);
            }
        }
        
        return bestMatch.Item1 >= 0 ? bestMatch : (unkTokenId, 1);
    }

    /// <summary>
    /// トークンの追加（メモリ最適化版）
    /// </summary>
    public void AddToken(string token, int tokenId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (string.IsNullOrEmpty(token))
            return;
            
        var current = this;
        foreach (char c in token)
        {
            if (!current._children.TryGetValue(c, out var child))
            {
                child = new OptimizedTrieNode(_internPool);
                current._children[c] = child;
            }
            current = child;
        }
        
        current._tokenId = tokenId;
        current._token = _internPool.Intern(token); // インターン化でメモリ共有
    }

    /// <summary>
    /// Trie木の最適化
    /// </summary>
    public void Optimize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // 子ノードを再帰的に最適化
        foreach (var child in _children.Values)
        {
            child.Optimize();
        }
        
        // トークン文字列の再インターン化
        if (_token != null)
        {
            _token = _internPool.Intern(_token);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
            
        foreach (var child in _children.Values)
        {
            child.Dispose();
        }
        _children.Clear();
        _disposed = true;
    }
}

/// <summary>
/// メモリ統計情報
/// </summary>
public readonly struct MemoryStatistics
{
    public long VocabularyMemory { get; init; }
    public long TrieMemory { get; init; }
    public long InternPoolMemory { get; init; }
    public long TotalMemory { get; init; }
    public int VocabularyCount { get; init; }
    public int InternedStringCount { get; init; }
    public int TrieNodeCount { get; init; }
    
    public override string ToString()
    {
        return $"Memory: {TotalMemory / 1024 / 1024:F2}MB " +
               $"(Vocab: {VocabularyMemory / 1024:F1}KB, " +
               $"Trie: {TrieMemory / 1024:F1}KB, " +
               $"Intern: {InternPoolMemory / 1024:F1}KB) " +
               $"Nodes: {TrieNodeCount}, Vocab: {VocabularyCount}, Interned: {InternedStringCount}";
    }
}