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

**結論**: Gemini専門家フィードバックにより技術的妥当性が確認された自前実装アプローチにより、外部ライブラリ依存を完全に排除し、OPUS-MT専用に最適化されたトークナイザーを実現する。