# OPUS-MT専用トークナイザー自前実装計画

## 作成日
2025年8月2日

## 背景

外部ライブラリ調査の結果、.NET生態系にはOPUS-MT .modelファイルと互換性のある実用的なSentencePieceライブラリが存在しないことが判明。Gemini専門家レビューにより、自前実装アプローチの技術的妥当性が確認された。

## Gemini専門家フィードバック要約

### 総評
> "調査結果と解決策は非常に的確かつ現実的であり、提案されている方針を強く支持します。これは現状で最もコントロールしやすく、かつ効果的な解決策です。"

### 技術的妥当性
- **BPE実装**: C#での再現実装は十分可能
- **翻訳精度向上**: 0%→50-80%の見積もりは現実的
- **メンテナンス性**: 依存関係排除による長期メンテナンス向上

## 実装における重要な技術課題

### 1. 前処理（Normalization）の正確な再現 ⭐⭐⭐⭐⭐

**最重要項目**

**課題**:
- OPUS-MTモデルが前提とする正規化プロセスの忠実な実装
- NFKC正規化、連続する空白の置換など

**対策**:
- Python公式実装との出力比較
- 同一前処理ステップの確実な実装
- `.vocab`ファイルとのマッチング率最大化

**実装例**:
```csharp
private string NormalizeText(string input)
{
    // 1. NFKC正規化
    var normalized = input.Normalize(NormalizationForm.FormKC);
    
    // 2. 連続する空白を単一空白に変換
    var spaceNormalized = Regex.Replace(normalized, @"\s+", " ");
    
    // 3. 先頭・末尾の空白を除去
    return spaceNormalized.Trim();
}
```

### 2. BPEアルゴリズムの高性能実装 ⭐⭐⭐⭐

**課題**:
- 貪欲な最長一致検索の正確な実装
- 大量テキスト処理時のパフォーマンス最適化

**対策**:
- `Span<T>`や`ReadOnlyMemory<char>`の活用
- 不要な文字列コピー・メモリ割り当ての回避
- 事前構築された検索用辞書（`Dictionary<string, int>`）の使用

**実装例**:
```csharp
private int[] TokenizeBPE(ReadOnlySpan<char> text)
{
    var tokens = new List<int>();
    var pos = 0;
    
    while (pos < text.Length)
    {
        var maxLength = 0;
        var tokenId = _unknownTokenId;
        
        // 最長一致検索
        for (var len = Math.Min(_maxTokenLength, text.Length - pos); len > 0; len--)
        {
            var candidate = text.Slice(pos, len);
            if (_vocabDict.TryGetValue(candidate.ToString(), out var id))
            {
                maxLength = len;
                tokenId = id;
                break;
            }
        }
        
        tokens.Add(tokenId);
        pos += Math.Max(1, maxLength); // 最低1文字進む
    }
    
    return tokens.ToArray();
}
```

### 3. 特殊トークンの正確な処理 ⭐⭐⭐

**課題**:
- BOS (`<s>`), EOS (`</s>`), UNK (`<unk>`), PAD (`<pad>`) の正確な処理
- 翻訳モデルへの入力形式の整備

**対策**:
- 特殊トークンIDの事前定義
- 文脈に応じた適切な挿入

**実装例**:
```csharp
public class SpecialTokens
{
    public const int BOS = 0;    // <s>
    public const int EOS = 2;    // </s>
    public const int UNK = 1;    // <unk>
    public const int PAD = 3;    // <pad>
}

private int[] AddSpecialTokens(int[] tokens)
{
    var result = new List<int> { SpecialTokens.BOS };
    result.AddRange(tokens);
    result.Add(SpecialTokens.EOS);
    return result.ToArray();
}
```

### 4. テストの徹底 ⭐⭐⭐⭐⭐

**最重要品質保証**

**課題**:
- Python公式実装との結果一致性の保証
- 同一入力で同一トークンIDシーケンス生成の確認

**対策**:
```csharp
[Test]
public void TestTokenizationAccuracy()
{
    // Python公式実装の期待結果
    var testCases = new[]
    {
        new { Input = "こんにちは", Expected = new[] { 0, 12345, 67890, 2 } },
        new { Input = "Hello world", Expected = new[] { 0, 54321, 98765, 2 } },
        // ... 更多测试用例
    };
    
    foreach (var testCase in testCases)
    {
        var actual = _tokenizer.Tokenize(testCase.Input);
        Assert.AreEqual(testCase.Expected, actual);
    }
}
```

## 実装フェーズ

### Phase 1: 語彙ファイル解析（1-2日）
1. `.vocab`ファイルの構造解析
2. 語彙辞書の読み込み実装
3. トークンID ↔ 文字列マッピング構築

### Phase 2: 基本アルゴリズム実装（2-3日）
1. 前処理（正規化）の実装
2. BPEアルゴリズムの実装
3. 特殊トークン処理の実装

### Phase 3: テスト・最適化（2-3日）
1. Python実装との比較テスト
2. パフォーマンス最適化
3. エラーハンドリングの充実

## 期待される成果

### 定量的効果
- **翻訳精度**: 0% → 50-80%
- **依存関係**: 外部ライブラリ数 6個削減 → 0個
- **バイナリサイズ**: 大幅削減
- **初期化時間**: 外部ライブラリロード時間削除

### 定性的効果
- **完全制御**: アルゴリズムの完全理解と制御
- **カスタマイズ性**: OPUS-MT特有の最適化実装可能
- **保守性**: 外部依存によるブレーキングチェンジなし
- **デバッグ性**: 全コードパスの可視性

## リスク管理

### 技術的リスク
- **前処理の不一致**: Python実装との詳細比較で対応
- **パフォーマンス問題**: `Span<T>`等の最適化技術で対応
- **特殊ケース**: 包括的なテストケースで対応

### スケジュールリスク
- **開発期間延長**: 段階的実装とマイルストーン管理
- **品質問題**: 継続的なテストとレビュー

## 成功指標

### 必須条件
1. **機能的正確性**: Python実装と100%一致するトークン化結果
2. **翻訳品質**: "tok_XXXX"形式から実際の翻訳文への変換
3. **パフォーマンス**: 現在のフォールバック実装以上の速度

### 理想条件
1. **翻訳精度**: 50%以上の翻訳品質向上
2. **レスポンス時間**: 100ms以下のトークン化処理
3. **メモリ使用量**: 現在の1/2以下

## 次のアクション

1. **OPUS-MT .vocabファイルの詳細分析**
2. **Python SentencePiece実装のリファレンス動作確認**
3. **Phase 1の実装開始**

---

## 【Think Mode】影響範囲調査と詳細設計（2025年8月2日）

### 現在のコードベース影響範囲分析

#### 1. 直接的影響ファイル

**メインエンジン**:
- `AlphaOpusMtTranslationEngine.cs`: RealSentencePieceTokenizerを直接インスタンス化（ライン77-79）
- `SentencePieceTokenizerFactory.cs`: ファクトリメソッドでRealSentencePieceTokenizerを生成（ライン38-39）

**インターフェース定義**:
- `Baketa.Core.Translation.Models.TokenizerModels.cs`: ITokenizerインターフェース定義
  ```csharp
  public interface ITokenizer {
      string TokenizerId { get; }
      string Name { get; }
      int VocabularySize { get; }
      int[] Tokenize(string text);
      string Decode(int[] tokens);
      string DecodeToken(int token);
  }
  ```

**DI登録**:
- `InfrastructureModule.cs`: TranslationServicesを登録（ライン98）
- 間接的にRealSentencePieceTokenizerが登録される

#### 2. 間接的影響ファイル

**関連エンジン**:
- `SimpleSentencePieceEngine.cs`: 別の翻訳エンジン実装
- `OpusMtOnnxEngine.cs`: ONNX エンジンの基盤実装
- `OnnxTranslationEngine.cs`: 汎用ONNXエンジン

**テストファイル群**:
- `RealSentencePieceTokenizerTests.cs`: 専用単体テスト
- `SentencePieceIntegrationTests.cs`: 統合テスト
- `SentencePiecePerformanceTests.cs`: パフォーマンステスト

#### 3. 重要な発見事項

**語彙ファイルの不在**:
- `E:\dev\Baketa\Models\SentencePiece\`ディレクトリには`.model`ファイルのみ存在
- **`.vocab`ファイルが存在しない** ← 自前実装の最大の技術課題
- 語彙辞書を直接読み込む代替手段の実装が必要

**モデルファイル構造**:
- `opus-mt-ja-en.model`, `opus-mt-en-ja.model`等のProtobuf形式ファイル
- バイナリ形式で語彙情報がエンコードされている
- 専用パーサーによる解析が必要

### 詳細技術設計

#### 1. アーキテクチャ戦略: 段階的移行アプローチ

**Phase 1: Protobufパーサー実装**
```csharp
public class SentencePieceModelParser
{
    public SentencePieceModel ParseModel(string modelPath)
    {
        // .modelファイル（Protobuf）を直接パース
        // 語彙情報、特殊トークン、BPEルールを抽出
    }
}

public class SentencePieceModel
{
    public Dictionary<string, int> Vocabulary { get; set; }
    public Dictionary<int, string> ReverseVocabulary { get; set; }
    public SpecialTokens SpecialTokens { get; set; }
    public List<BpeMergeRule> MergeRules { get; set; }
}
```

**Phase 2: BPEアルゴリズム実装**
```csharp
public class BpeTokenizer
{
    private readonly SentencePieceModel _model;
    
    public int[] TokenizeBpe(ReadOnlySpan<char> text)
    {
        // 1. 前処理（NFKC正規化、空白正規化）
        var normalized = NormalizeText(text);
        
        // 2. 初期文字分割
        var characters = SplitToCharacters(normalized);
        
        // 3. BPEマージ適用
        var subwords = ApplyBpeMerges(characters);
        
        // 4. 語彙辞書でトークンID変換
        return ConvertToTokenIds(subwords);
    }
}
```

**Phase 3: 互換性保持ラッパー**
```csharp
public class OpusMtNativeTokenizer : ITokenizer, IDisposable
{
    private readonly BpeTokenizer _bpeTokenizer;
    private readonly SentencePieceModel _model;
    
    // 既存のITokenizerインターフェースを完全実装
    // フォールバック実装との段階的置換を可能にする
}
```

#### 2. 実装上の技術課題と解決策

**課題1: Protobufパーシング**
- **問題**: SentencePiece .modelファイルはGoogleのProtobuf形式
- **解決策**: 
  - Google.Protobuf NuGetパッケージを使用
  - SentencePiece.proto定義を取得してC#クラス生成
  - または、バイナリ解析による直接パーシング

**課題2: Unicode正規化の一致**
- **問題**: Python実装との正規化処理の完全一致が必要
- **解決策**: 
  ```csharp
  private string NormalizeText(ReadOnlySpan<char> input)
  {
      // 1. NFKC正規化（.NET標準）
      var nfkc = input.ToString().Normalize(NormalizationForm.FormKC);
      
      // 2. SentencePiece特有の前処理
      var preprocessed = ApplySentencePiecePreprocessing(nfkc);
      
      return preprocessed;
  }
  ```

**課題3: パフォーマンス最適化**
- **問題**: 大量テキストでのメモリ効率とCPU効率
- **解決策**:
  ```csharp
  // Span<T>を活用したゼロコピー実装
  public int[] TokenizeOptimized(ReadOnlySpan<char> text)
  {
      using var tokenBuffer = ArrayPool<int>.Shared.Rent(estimatedTokenCount);
      var tokenSpan = tokenBuffer.AsSpan();
      
      var actualTokenCount = TokenizeToSpan(text, tokenSpan);
      return tokenSpan[..actualTokenCount].ToArray();
  }
  ```

#### 3. 移行戦略とリスク管理

**段階的移行パス**:
1. **Week 1**: Protobufパーサー + 基本BPE実装
2. **Week 2**: 正規化処理 + 特殊トークン処理
3. **Week 3**: パフォーマンス最適化 + 完全テスト

**互換性保証**:
```csharp
public class OpusMtNativeTokenizer : ITokenizer
{
    private readonly BpeTokenizer _nativeImplementation;
    private readonly RealSentencePieceTokenizer _fallbackImplementation;
    
    public int[] Tokenize(string text)
    {
        try
        {
            // 自前実装を優先使用
            return _nativeImplementation.Tokenize(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自前実装が失敗、フォールバックを使用");
            return _fallbackImplementation.Tokenize(text);
        }
    }
}
```

**品質保証戦略**:
```csharp
[Test]
public void TestTokenizationAccuracyAgainstPython()
{
    // Python SentencePieceの出力と100%一致することを保証
    var testCases = LoadPythonReferenceTestCases();
    
    foreach (var testCase in testCases)
    {
        var actual = _tokenizer.Tokenize(testCase.Input);
        CollectionAssert.AreEqual(testCase.ExpectedTokens, actual);
    }
}
```

#### 4. パフォーマンス目標と測定

**目標設定**:
- **トークン化速度**: 現在のフォールバック実装の2倍以上
- **メモリ使用量**: Microsoft.ML.Tokenizers相当以下
- **翻訳精度**: 50-80%向上（"tok_XXXX" → 実際の翻訳）

**測定方法**:
```csharp
[Benchmark]
public int[] TokenizeBenchmark()
{
    return _tokenizer.Tokenize(TestData.LongJapaneseText);
}

[Fact]
public void MemoryUsageTest()
{
    var beforeMemory = GC.GetTotalMemory(true);
    
    // 大量トークン化実行
    for (int i = 0; i < 1000; i++)
    {
        _tokenizer.Tokenize(TestData.SampleTexts[i % TestData.SampleTexts.Length]);
    }
    
    var afterMemory = GC.GetTotalMemory(true);
    var memoryIncrease = afterMemory - beforeMemory;
    
    Assert.That(memoryIncrease, Is.LessThan(MaxAllowedMemoryIncrease));
}
```

### 実装優先度マトリックス

| コンポーネント | 重要度 | 複雑度 | 開発順序 |
|---------------|--------|--------|----------|
| **Protobufパーサー** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | 1 |
| **BPEアルゴリズム** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | 2 |
| **前処理（正規化）** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | 3 |
| **特殊トークン処理** | ⭐⭐⭐⭐ | ⭐⭐ | 4 |
| **パフォーマンス最適化** | ⭐⭐⭐ | ⭐⭐⭐⭐ | 5 |
| **フォールバック機構** | ⭐⭐⭐ | ⭐⭐ | 6 |

---

## 【Gemini専門家レビュー第2回】技術詳細フィードバック（2025年8月2日）

### 総合評価: 非常に有望な計画 ⭐⭐⭐⭐⭐

> "このOPUS-MT専用トークナイザー自前実装計画について、技術的な観点からレビューします。全体として、主要な技術課題とフェーズ分けが適切に特定されており、**堅実な計画**だと思います。"

### 評価されたポイント
1. **主要技術課題の適切な特定**: Unicode正規化とPython実装との互換性重視が優秀
2. **論理的なフェーズ分け**: 依存関係を考慮した段階的実装アプローチ  
3. **現実的なパフォーマンス目標**: 明確で測定可能な指標設定

### 重要な追加技術課題

#### 1. 見落としていた重要課題
**語彙の効率的な検索構造** ⭐⭐⭐⭐⭐
- **課題**: BPEアルゴリズムにおける最長一致検索の高速化
- **解決策**: **Trie木、Aho-Corasickアルゴリズムの派生**データ構造導入
- **影響**: パフォーマンスに直結する最重要最適化

**スレッドセーフティ**
- **課題**: マルチスレッド環境での安全な並行使用
- **解決策**: 不変オブジェクト設計、ThreadLocalストレージ検討

**エラーハンドリングと堅牢性**
- **課題**: 不正なUnicodeシーケンス、予期しないモデルファイル形式への対応
- **解決策**: 堅牢なエラーハンドリング戦略とフォールバック機構

#### 2. Protobufパーシング: 決定的推奨事項

**Google.Protobufの使用を強く推奨** ⭐⭐⭐⭐⭐
- **安全性と堅牢性**: 手動バイナリ解析は非常にエラーを起こしやすい
- **開発効率**: `.proto`ファイルから自動生成クラスで安全アクセス
- **メンテナンス性**: コード可読性向上、メンテナンス容易
- **パフォーマンス**: `.model`パースはホットパスでないためオーバーヘッド無視可能

> "**Google.Protobufの使用を強く推奨します。** Protobufの仕様は複雑であり、手動でのバイナリ解析は非常にエラーを起こしやすく、将来のProtobufバージョンやモデルファイルのわずかな変更で容易に壊れる可能性があります。"

#### 3. BPEアルゴリズム実装の重要注意点

**SentencePiece特有の挙動** ⭐⭐⭐⭐⭐
- **先頭スペースの扱い**: 単語先頭に **アンダースコア（▁）** 付加の正確な再現
- **未知語（UNK）処理**: 語彙に存在しない文字の`<unk>`マッピング戦略
- **数字の扱い**: `123` → `1`,`2`,`3` vs `123`の分割判定ロジック

**Unicode正規化のタイミング**
- **重要性**: BPEアルゴリズム適用前にPython実装と同じNFKC正規化必須
- **検証**: 正規化結果の完全一致確認

#### 4. 追加パフォーマンス最適化手法

**Trie木/Radix木** ⭐⭐⭐⭐⭐
- **用途**: 語彙検索高速化の最も効果的なデータ構造
- **効果**: 最長一致検索において威力を発揮

**ReadOnlySpan<char> / StringSegment**
- **用途**: 文字列サブセグメント処理でのGC負荷軽減
- **効果**: 新しい文字列割り当て回避

**プロファイリング駆動最適化**
- **ツール**: `BenchmarkDotNet`による実際のボトルネック特定
- **方針**: 闇雲な最適化を避け、測定結果に基づく集中最適化

#### 5. 最適テスト戦略: ゴールデンテスト

**リファレンステスト実装** ⭐⭐⭐⭐⭐
```csharp
[Test]
public void GoldenReferenceTest()
{
    // 1. 多様な入力テキスト準備
    var testInputs = new[]
    {
        "短文テスト",
        "長文テスト：" + LongJapaneseText,
        "特殊文字：@#$%^&*()",
        "数字混在：123ABC",
        "Multi-language: こんにちはHelloمرحبا"
    };
    
    // 2. Python SentencePieceでゴールデンデータ生成済み
    var goldenResults = LoadPythonReferenceResults();
    
    // 3. .NET実装との完全一致検証
    foreach (var (input, expected) in testInputs.Zip(goldenResults))
    {
        var actual = _tokenizer.Tokenize(input);
        CollectionAssert.AreEqual(expected.TokenIds, actual);
        
        // デトークン化ラウンドトリップ検証
        var detokenized = _tokenizer.Decode(actual);
        Assert.AreEqual(expected.NormalizedText, detokenized);
    }
}
```

**テスト観点の完全網羅**
- Unicode正規化挙動の一致
- 先頭スペース（▁）処理の一致  
- 未知語処理の一致
- 特殊文字・数字・多言語混在の一致

### 修正版実装アーキテクチャ

#### Phase 1: Protobufパーサー実装（修正版）
```csharp
public class SentencePieceModelParser
{
    // Google.Protobuf使用による安全な実装
    public SentencePieceModel ParseModel(string modelPath)
    {
        using var fileStream = File.OpenRead(modelPath);
        var protoModel = SentencePieceProto.Parser.ParseFrom(fileStream);
        
        return new SentencePieceModel
        {
            Vocabulary = BuildVocabularyFromProto(protoModel),
            SpecialTokens = ExtractSpecialTokens(protoModel),
            SearchTrie = BuildTrieForFastSearch(protoModel.Pieces) // 追加
        };
    }
}
```

#### Phase 2: Trie木ベース高速検索
```csharp
public class TrieBasedTokenizer
{
    private readonly TrieNode _vocabularyTrie;
    
    public int[] TokenizeBpe(ReadOnlySpan<char> text)
    {
        var normalized = NormalizeSentencePieceCompatible(text);
        var tokens = new List<int>();
        var pos = 0;
        
        while (pos < normalized.Length)
        {
            // Trie木による最長一致検索
            var (tokenId, length) = _vocabularyTrie.FindLongestMatch(
                normalized.Slice(pos));
                
            tokens.Add(tokenId);
            pos += Math.Max(1, length);
        }
        
        return tokens.ToArray();
    }
    
    private ReadOnlySpan<char> NormalizeSentencePieceCompatible(ReadOnlySpan<char> input)
    {
        // SentencePiece互換の完全正規化実装
        // 1. NFKC正規化
        // 2. 先頭スペース → ▁ 変換
        // 3. 連続空白正規化
    }
}
```

### 実装スケジュール修正版

| フェーズ | 期間 | 主な実装内容 | 重要度 |
|---------|------|-------------|--------|
| **Phase 1** | 2-3日 | Google.Protobuf統合、基本パーサー | ⭐⭐⭐⭐⭐ |
| **Phase 2** | 3-4日 | Trie木実装、BPEアルゴリズム | ⭐⭐⭐⭐⭐ |  
| **Phase 3** | 2-3日 | SentencePiece互換正規化 | ⭐⭐⭐⭐⭐ |
| **Phase 4** | 2-3日 | ゴールデンテスト、品質保証 | ⭐⭐⭐⭐⭐ |
| **Phase 5** | 1-2日 | パフォーマンス最適化 | ⭐⭐⭐ |
| **Phase 6** | 1日 | 統合テスト、デプロイ準備 | ⭐⭐⭐ |

### パフォーマンス目標への専門家見解

**翻訳精度50-80%向上について**:
> "「50-80%向上（"tok_XXXX" → 実際の翻訳）」という目標は、トークナイザー単体で直接的に翻訳精度（BLEUスコアなど）を向上させるというよりは、**適切なトークナイザーを使用することで、翻訳モデルが本来の性能を発揮できるようになる**、という意味合いが強いと理解しています。現在の"tok_XXXX"のようなプレースホルダー的なトークン化から、SentencePieceのような適切なトークン化に移行することで、翻訳品質が劇的に向上することは十分に考えられます。"

### 即座実行すべきアクション

1. **Google.Protobuf NuGetパッケージ導入** （優先度：最高）
2. **SentencePiece.proto定義取得とC#クラス生成**
3. **Python SentencePieceによるゴールデンテストデータ作成**
4. **Trie木データ構造の設計・実装**
5. **SentencePiece互換正規化の詳細調査**

---

**最終結論**: Gemini専門家の第2回詳細レビューにより、**Google.Protobuf使用**、**Trie木ベース検索**、**ゴールデンテスト戦略**という3つの重要な技術指針が確定した。これらの専門的フィードバックにより、実装成功確率が大幅に向上し、OPUS-MT専用に最適化された高性能トークナイザーの実現が現実的となった。