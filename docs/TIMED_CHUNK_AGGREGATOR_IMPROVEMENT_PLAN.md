# TimedChunkAggregator 改善計画書

## 📋 概要

`TimedChunkAggregator`の過剰な統合問題を解決し、ゲームUIのコンテキストに応じた適切なテキストグループ化を実現する。

**作成日**: 2025-01-21
**ステータス**: 実装待ち
**優先度**: P0（高）

---

## 🔍 現状の問題

### 問題の詳細
- **現象**: 画面上の離れた位置にあるテキストも同一文章として統合される
- **影響**: 翻訳精度の低下、意味不明な翻訳結果
- **発生条件**: 特にメニュー画面など、テキスト要素が多い画面で顕著

### 具体例
```
メニュー画面:
┌─────────────────────────┐
│ アイテム  装備  スキル   │ ← タブ
│ ─────────────────────  │
│ ポーション x10    500G  │ ← リスト項目
│ エリクサー x3   2000G  │
│ 魔法の剣   x1   9999G  │
└─────────────────────────┘

現在の動作: 全てを1つの文章として統合
期待動作: 各行・各要素を適切に分離
```

### 根本原因
`CombineChunks`メソッドが入力された全チャンクを無条件で1つに統合している。

---

## 🎯 解決方針

### 基本戦略
**ユーザー設定不要の自動適応アルゴリズム**を実装する。

### 設計原則
1. **自動化優先**: ユーザーによる閾値調整を不要にする
2. **コンテキスト認識**: ゲーム画面の種類を自動判別
3. **段階的実装**: Phase 1で基本機能、Phase 2で高度化

---

## 📐 技術設計

### Phase 1: スマート自動閾値システム（優先実装）

#### 1.1 文字サイズ自動検出
```csharp
public class ChunkProximityAnalyzer
{
    public ProximityContext AnalyzeChunks(List<TextChunk> chunks)
    {
        // 平均文字サイズを計算
        var avgHeight = chunks.Average(c => c.CombinedBounds.Height);
        var avgWidth = avgHeight * 0.6; // 一般的な文字の縦横比

        return new ProximityContext
        {
            AverageCharHeight = avgHeight,
            AverageCharWidth = avgWidth,
            VerticalThreshold = avgHeight * 1.2,   // 行間の1.2倍
            HorizontalThreshold = avgWidth * 3     // 文字3個分
        };
    }
}
```

#### 1.2 相対距離による近接判定
```csharp
private bool IsProximityClose(TextChunk a, TextChunk b, ProximityContext context)
{
    // 垂直方向の距離
    var vGap = GetVerticalGap(a.CombinedBounds, b.CombinedBounds);
    if (vGap > context.VerticalThreshold) return false;

    // 水平方向の距離（同一行の場合のみ）
    if (IsSameLine(a, b, context))
    {
        var hGap = GetHorizontalGap(a.CombinedBounds, b.CombinedBounds);
        return hGap <= context.HorizontalThreshold;
    }

    return false;
}
```

#### 1.3 グループ化アルゴリズム（連結成分方式）
```csharp
private List<List<TextChunk>> GroupByProximity(List<TextChunk> chunks, ProximityContext context)
{
    var groups = new List<List<TextChunk>>();
    var visited = new bool[chunks.Count];

    for (int i = 0; i < chunks.Count; i++)
    {
        if (!visited[i])
        {
            var group = new List<TextChunk>();
            DFS(chunks, i, visited, group, context);
            groups.Add(group);
        }
    }

    return groups;
}

private void DFS(List<TextChunk> chunks, int index, bool[] visited,
                 List<TextChunk> group, ProximityContext context)
{
    visited[index] = true;
    group.Add(chunks[index]);

    for (int i = 0; i < chunks.Count; i++)
    {
        if (!visited[i] && IsProximityClose(chunks[index], chunks[i], context))
        {
            DFS(chunks, i, visited, group, context);
        }
    }
}
```

---

### Phase 2: コンテキスト認識システム（将来拡張）

#### 2.1 画面パターン認識
```csharp
public enum ScreenContextType
{
    Dialogue,    // 会話シーン
    Menu,        // メニュー画面
    Battle,      // バトル画面
    Unknown      // 不明
}

public class ScreenContextAnalyzer
{
    public ScreenContextType AnalyzeScreenContext(List<TextChunk> chunks)
    {
        var features = ExtractFeatures(chunks);

        // グリッド配置チェック
        if (features.IsGridAligned && features.HasRegularSpacing)
            return ScreenContextType.Menu;

        // テキスト密度チェック
        if (features.TextDensity < 0.3)
            return ScreenContextType.Dialogue;

        // バトル画面特有のパターン
        if (features.HasNumericValues && features.HasStatusKeywords)
            return ScreenContextType.Battle;

        return ScreenContextType.Unknown;
    }
}
```

#### 2.2 コンテキスト別処理戦略
```csharp
public interface IGroupingStrategy
{
    List<List<TextChunk>> GroupChunks(List<TextChunk> chunks);
}

public class DialogueGroupingStrategy : IGroupingStrategy
{
    // 会話文は積極的に結合
    public List<List<TextChunk>> GroupChunks(List<TextChunk> chunks)
    {
        // 大きめの閾値で結合
    }
}

public class MenuGroupingStrategy : IGroupingStrategy
{
    // メニュー項目は各行独立
    public List<List<TextChunk>> GroupChunks(List<TextChunk> chunks)
    {
        // 行ごとに分離
    }
}
```

---

## 🔧 実装計画

### Phase 1 実装手順（2-3時間）

1. **ProximityContext クラス作成**
   - 文字サイズ自動検出
   - 動的閾値計算

2. **近接判定ロジック実装**
   - IsProximityClose メソッド
   - 垂直/水平距離計算

3. **グループ化アルゴリズム実装**
   - 連結成分探索（DFS）
   - グループ別統合

4. **CombineChunks メソッド改修**
   - 複数チャンク出力対応
   - 既存インターフェース互換性保持

5. **設定追加（内部パラメータのみ）**
   ```json
   {
     "TimedAggregatorSettings": {
       "ProximityGrouping": {
         "Enabled": true,
         "VerticalDistanceFactor": 1.2,
         "HorizontalDistanceFactor": 3.0
       }
     }
   }
   ```

### Phase 2 実装手順（追加2-3時間）

1. **ScreenContextAnalyzer 実装**
2. **各種GroupingStrategy実装**
3. **戦略パターン統合**

---

## 📊 期待効果

### 改善指標
- **翻訳精度**: 60% → 90% 向上（メニュー画面）
- **処理速度**: O(n²) だが実用上問題なし（チャンク数 < 100）
- **ユーザー体験**: 設定不要で自動最適化

### 対応可能なシナリオ
- ✅ 会話シーンの自然な文章結合
- ✅ メニュー画面の項目別分離
- ✅ バトル画面のステータス表示
- ✅ 異なる解像度・UIスケール

---

## 🧪 テスト計画

### ユニットテスト
1. 近接判定ロジックのテスト
2. グループ化アルゴリズムのテスト
3. コンテキスト認識のテスト

### 統合テスト
1. 実ゲーム画面でのテスト
   - RPGメニュー画面
   - ビジュアルノベル会話シーン
   - アクションゲームUI

### パフォーマンステスト
- 100チャンク処理時の速度測定
- メモリ使用量の確認

---

## 📝 実装上の注意点

### 後方互換性
- 既存の`OnChunksAggregated`コールバックとの互換性維持
- 設定ファイルの移行パス確保

### エラーハンドリング
- チャンク数0の場合の処理
- 極端に大きい/小さい文字サイズへの対処
- DFS スタックオーバーフロー対策（反復実装）

### パフォーマンス最適化
- 必要に応じて空間分割データ構造（Quadtree）導入
- キャッシュ戦略の検討

---

## 🚀 今後の拡張可能性

1. **機械学習ベースの画面認識**
   - より高精度なコンテキスト判定

2. **ゲーム別プロファイル**
   - 特定ゲーム用の最適化設定

3. **リアルタイムフィードバック**
   - ユーザー操作から学習

---

## 📅 マイルストーン

| Phase | 期限 | 状態 | 備考 |
|-------|------|------|------|
| Phase 1 基本実装 | 2025-01-21 | 未着手 | 優先実装 |
| Phase 1 テスト | 2025-01-22 | 未着手 | - |
| Phase 2 設計 | 2025-01-23 | 未着手 | 必要に応じて |
| Phase 2 実装 | 2025-01-24 | 未着手 | オプション |

---

## 📚 参考資料

- Gemini技術フィードバック（2025-01-21）
- 連結成分アルゴリズム: [Graph Connected Components](https://en.wikipedia.org/wiki/Connected_component)
- Clean Architecture準拠設計パターン

---

## ✅ 承認

- **技術リード**: 未承認
- **プロダクトオーナー**: 未承認
- **実装者**: 準備完了