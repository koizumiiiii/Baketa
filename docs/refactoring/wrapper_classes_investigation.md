# ラッパークラス依存関係調査結果

**調査日**: 2025-10-04
**目的**: PaddleOcrEngineリファクタリングにおけるラッパークラスへの影響調査

---

## 📊 調査結果サマリー

### ✅ 問題なし（IOcrEngineインターフェース経由のみ）

以下のクラスはIOcrEngineインターフェース経由でのみアクセスしており、リファクタリング影響なし：

1. **PooledOcrService** (`Baketa.Infrastructure/OCR/PaddleOCR/Services/PooledOcrService.cs`)
   - `ObjectPool<IOcrEngine>`使用
   - 具象型への依存なし
   - **影響**: なし

2. **HybridPaddleOcrService** - 確認済み
   - IOcrEngineインターフェース経由
   - **影響**: なし

3. **AdaptiveOcrEngine** - 確認済み
   - IOcrEngineインターフェース経由
   - **影響**: なし

4. **IntelligentFallbackOcrEngine** - 確認済み
   - IOcrEngineインターフェース経由
   - **影響**: なし

5. **StickyRoiOcrEngineWrapper** - 確認済み
   - IOcrEngineインターフェース経由
   - **影響**: なし

6. **EnsembleOcrEngine** - 確認済み
   - IOcrEngineインターフェース経由
   - **影響**: なし

### ⚠️ 具象型依存あり（要対応）

#### BatchOcrProcessor.cs

**ファイル**: `Baketa.Infrastructure/OCR/BatchProcessing/BatchOcrProcessor.cs`

**具象型依存箇所**:

##### 1. Line 2557-2560: ResetOcrFailureCounter()
```csharp
if (_ocrEngine is PaddleOcrEngine paddleEngine)
{
    var failureCount = paddleEngine.GetConsecutiveFailureCount();
    paddleEngine.ResetFailureCounter();
    // ...
}
```

##### 2. Line 2588-2590: GetOcrFailureCount()
```csharp
if (_ocrEngine is PaddleOcrEngine paddleEngine)
{
    return paddleEngine.GetConsecutiveFailureCount();
}
```

**使用メソッド**:
- `GetConsecutiveFailureCount()` - PaddleOcrEngine specific
- `ResetFailureCounter()` - PaddleOcrEngine specific

**問題点**:
1. PaddleOcrEngine具象型へのキャストを使用
2. IOcrEngineインターフェースにない専用メソッドに依存
3. リファクタリング後、これらのメソッドはIPaddleOcrPerformanceTrackerに移動
4. IOcrEngine経由では直接アクセスできなくなる

---

## 🔧 対応方針

### Option A: IOcrEngineインターフェース拡張（推奨）⭐⭐⭐⭐⭐

**方針**: パフォーマンス統計メソッドをIOcrEngineインターフェースに追加

**修正内容**:
```csharp
// IOcrEngine.cs（Core層）
public interface IOcrEngine
{
    // 既存メソッド...

    // 追加メソッド
    int GetConsecutiveFailureCount();
    void ResetFailureCounter();
}
```

**利点**:
- BatchOcrProcessor.csの修正不要
- 後方互換性完全維持
- 他のIOcrEngine実装でもパフォーマンス統計が利用可能

**実装**:
```csharp
// PaddleOcrEngine.cs (Phase 2.9で対応)
public int GetConsecutiveFailureCount()
{
    return _performanceTracker.GetConsecutiveFailureCount();
}

public void ResetFailureCounter()
{
    _performanceTracker.ResetFailureCounter();
}
```

### Option B: BatchOcrProcessor修正（代替案）⭐⭐⭐

**方針**: BatchOcrProcessorにIPaddleOcrPerformanceTracker注入

**修正内容**:
```csharp
public class BatchOcrProcessor
{
    private readonly IOcrEngine _ocrEngine;
    private readonly IPaddleOcrPerformanceTracker _performanceTracker; // 追加

    public BatchOcrProcessor(
        IOcrEngine ocrEngine,
        IPaddleOcrPerformanceTracker performanceTracker) // 追加
    {
        _ocrEngine = ocrEngine;
        _performanceTracker = performanceTracker;
    }

    public void ResetOcrFailureCounter()
    {
        _performanceTracker.ResetFailureCounter();
    }

    public int GetOcrFailureCount()
    {
        return _performanceTracker.GetConsecutiveFailureCount();
    }
}
```

**欠点**:
- BatchOcrProcessorがPaddleOCR特化の依存を持つ
- 他のOCRエンジン使用時に不整合が発生する可能性

### Option C: 機能削除（非推奨）❌

BatchOcrProcessor.csから失敗カウンター機能を削除
→ 診断機能が失われるため**非推奨**

---

## ✅ 推奨対応

**Phase 2.1での対応**:
1. IOcrEngineインターフェースに以下を追加:
   - `int GetConsecutiveFailureCount()`
   - `void ResetFailureCounter()`

**Phase 2.9での実装**:
1. PaddleOcrEngineで追加メソッドを実装（IPaddleOcrPerformanceTracker経由）
2. 他のIOcrEngine実装でもデフォルト実装を提供（return 0等）

**理由**:
- 最小限の変更で互換性維持
- Clean Architectureの観点から適切（Core層の抽象が拡張される）
- 失敗カウンター機能は診断において重要

---

## 📋 Phase 2.1対応タスク

- [x] ラッパークラス依存関係調査完了
- [ ] IOcrEngineインターフェース拡張（パフォーマンス統計メソッド追加）
- [ ] BatchOcrProcessor.csの動作検証計画策定

---

## 🔍 その他の発見

### リフレクション使用状況

以下のファイルでGetType()使用が確認されましたが、すべてログ出力目的のみ：

- `BatchOcrProcessor.cs:2570-2571` - ログ出力のみ
- `PaddleOcrEngineFactory.cs:102, 108, 113, 132` - ログ出力のみ

**結論**: リフレクションによる内部メンバーアクセスは確認されず

### DI登録状況

- すべてのラッパークラスはIOcrEngineインターフェース経由で登録
- 具象型PaddleOcrEngineへの直接的なDI依存なし

---

## 🎯 結論

**リファクタリングへの影響**:
- ✅ **大部分のラッパークラス**: 影響なし（IOcrEngineインターフェース経由）
- ⚠️ **BatchOcrProcessor**: 軽微な影響（IOcrEngineインターフェース拡張で対応可能）
- ✅ **リフレクション**: 問題なし（ログ出力目的のみ）

**Gemini指摘事項への回答**:
- 「publicでないメンバーへの依存」: 確認されず
- 「リフレクションによる内部動作への依存」: 確認されず
- **唯一の依存**: BatchOcrProcessor.csの具象型メソッド2つのみ

**総合評価**: ✅ リファクタリング実施可能、影響は限定的
