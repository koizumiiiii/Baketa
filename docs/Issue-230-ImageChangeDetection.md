# Issue #230: 画像変化検知の問題と対応経緯

## 概要

画像変化検知機能において、テキストのみが変更された場合に変化を検出できない問題が発生。

---

## 1. 問題の発見経緯

### 初期症状
- Live翻訳モードで画面のテキストが変わっても翻訳が更新されない
- ログで `Similarity: 1.0000, HasChange: False` と表示される
- 変化がないと判定され、OCRがスキップされる

### 調査で判明した問題

#### 問題1: ConvertToBitmapが空のBitmapを返していた

**原因:**
```csharp
// 修正前: IImageConvertibleインターフェースが未実装
private Bitmap ConvertToBitmap(IImage image)
{
    if (image is IImageConvertible convertible)
    {
        return convertible.ToBitmap();  // ← ここに到達しない
    }
    return new Bitmap(1, 1);  // ← 常にこれが返される
}
```

**結果:**
- ハッシュ値が常に `FFFFFFFF` または固定値
- 画像の内容に関係なく同じハッシュ

**修正:**
```csharp
// 修正後: LockPixelData()でピクセルデータを正しくコピー
private Bitmap ConvertToBitmap(IImage image)
{
    using var pixelLock = image.LockPixelData();
    var pixelData = pixelLock.Data;
    // ... ピクセルデータをBitmapにコピー
}
```

#### 問題2: 8x8ハッシュの解像度限界

**原因:**
DifferenceHashアルゴリズムは画像を8x8（または9x8）に縮小してハッシュを計算する。

```
1280x720の画面 → 8x8に縮小
= 1ハッシュピクセルが 160x90 の領域を代表
```

**結果:**
- テキスト変更（数文字〜数行）は8x8では検出不能
- 縮小後の画像がほぼ同一になる
- `Similarity: 1.0000` と判定される

**図解:**
```
[1280x720 画面]
┌─────────────────────────────────┐
│  ゲーム背景（大部分）            │
│                                 │
│  ┌─────────────────┐            │
│  │テキストボックス  │ ← ここだけ変化 │
│  │「こんにちは」    │            │
│  └─────────────────┘            │
└─────────────────────────────────┘
         ↓ 8x8に縮小
┌─┬─┬─┬─┬─┬─┬─┬─┐
│■│■│■│■│■│■│■│■│ ← テキスト部分は
├─┼─┼─┼─┼─┼─┼─┼─┤    1ピクセル以下に
│■│■│■│■│■│■│■│■│
├─┼─┼─┼─┼─┼─┼─┼─┤
│■│■│■│■│■│■│■│■│
└─┴─┴─┴─┴─┴─┴─┴─┘
```

#### 問題3: 3段階検知パイプラインの設計問題

```
Stage 1 (Quick Filter)
  ├─ 8x8 DifferenceHash比較
  ├─ Similarity < 0.95 → 変化あり → Stage 2へ
  └─ Similarity >= 0.95 → 変化なし → OCRスキップ ← ここで止まる

Stage 2 (Medium Precision)
  ├─ 別のハッシュアルゴリズムで比較
  └─ 同様に8x8ベースなので同じ問題

Stage 3 (High Precision)
  ├─ SSIM（構造的類似性）計算
  └─ 1280x720で22万ウィンドウ → 処理時間が長すぎ
```

---

## 2. これまでの対応

### 対応1: ConvertToBitmap修正 ✅ 成功

**変更ファイル:** `OptimizedPerceptualHashService.cs`

- `LockPixelData()` でピクセルデータを取得
- 正しくBitmapにコピー
- Gray8パレット設定、RGBA→BGRAチャンネルスワップ対応

**結果:** ハッシュ値が正しく計算されるようになった（`37747571...` など）

### 対応2: 閾値調整 ⚠️ 効果なし

**変更ファイル:** `appsettings.json`

```json
"Stage1SimilarityThreshold": 0.95  // 0.60 → 0.95に変更
```

**結果:** ハッシュ自体が同一（Similarity=1.0）なので閾値を変えても効果なし

### 対応3: アルゴリズム変更 ⚠️ 効果なし

**変更ファイル:** `EnhancedImageChangeDetectionService.cs`

- AverageHash → DifferenceHash に変更

**結果:** どちらも8x8ベースなので同じ問題

### 対応4: Stage 2スキップ ⚠️ 効果なし

**変更内容:**
- Similarity >= 0.98 の場合、Stage 2をスキップしてStage 3へ

**結果:** Stage 3のSSIM計算が遅すぎて実用的でない

### 対応5: 高類似度時の強制通過 ❌ 副作用あり

**変更ファイル:** `EnhancedImageChangeDetectionService.cs`

```csharp
// Similarity >= 0.995 の場合、「変化あり」として強制通過
if (similarity >= 0.995f)
{
    return ImageChangeResult.CreateChanged(...);
}
```

**結果:**
- テキスト変更時にOCRが実行されるようになった ✅
- しかし、テキストが変更されていない場合も毎回OCR実行 ❌
- オーバーレイが毎回クリア→再作成される ❌
- **翻訳結果が消えてしまう問題が発生** ❌

---

## 3. 残っている問題

### 現状
- 「強制通過」ロジックにより、静止画でも毎回OCRが実行される
- OCR実行時にオーバーレイがクリアされ、翻訳結果が消える
- ユーザー体験が著しく悪化

### 根本原因
**8x8ハッシュではテキスト変更を検出できない**という根本的な限界

---

## 4. 検討中の解決策

### 案A: ハッシュ解像度向上（推奨）

```csharp
// 8x8 → 16x16 または 32x32 に変更
const int size = 16;  // 現在は 8
```

**メリット:**
- テキスト変更を検出できる可能性が高まる
- 変化なしの場合はOCRスキップ（現状維持）

**デメリット:**
- ハッシュ計算コストが4〜16倍
- 64bit → 256bit/1024bit でメモリ増加

### 案B: テキストベース変化検知

```csharp
// OCR結果のテキストを比較
if (previousOcrText == currentOcrText)
{
    // オーバーレイを維持、更新しない
}
```

**メリット:**
- 最も正確な変化検知
- テキストが同じならオーバーレイ維持

**デメリット:**
- OCRを毎回実行する必要がある
- パフォーマンスへの影響

### 案C: ROI（関心領域）限定比較

```csharp
// 前回のOCR検出領域のみを高精度で比較
var textRegions = previousOcrResult.TextRegions;
foreach (var region in textRegions)
{
    var regionHash = ComputeHash(image, region);
    // 領域ごとに比較
}
```

**メリット:**
- テキスト領域のみを高精度で比較
- 背景の変化を無視できる

**デメリット:**
- 前回のOCR結果が必要（初回は不可）
- 実装が複雑

### 案D: オーバーレイ管理の改善

```csharp
// OCR実行中もオーバーレイを維持
// 新しい結果が来てから差し替え
if (newOcrResult.TextChanged(previousOcrResult))
{
    UpdateOverlays(newOcrResult);
}
else
{
    KeepCurrentOverlays();
}
```

**メリット:**
- 翻訳結果が消える問題を解決
- ユーザー体験向上

**デメリット:**
- OCRは毎回実行（パフォーマンス）

---

## 5. 実装完了（2025-12-22）

### Phase 1（完了）✅
1. **強制通過ロジックをリバート** - 翻訳が消える問題を解消
2. **元の動作に戻す** - 変化なし判定でOCRスキップ

### Phase 2（完了）✅
3. **ハッシュ解像度を32x32に向上** - テキスト変更検出の精度向上
   - 720p画面で1ブロック約40x22ピクセル
   - テキストボックス単位の変化を検出可能
4. **閾値を0.98に調整** - 32x32ハッシュでの最適値
5. **ArrayPool導入** - GC圧力軽減（Geminiレビュー反映）
6. **BitOperations.PopCount()使用** - POPCNT命令でビットカウント高速化

### Phase 3（完了）✅
7. **Stage 3スキップロジック実装** - Stage 2が変化検出時にStage 3をスキップ
   - **問題**: Stage 3のSSIMは全画像ベースのため、テキスト変更を検出不可
   - **症状**: Stage 2で`HasChanged=true`→Stage 3でSSIM=0.999→`HasChanged=false`に上書き
   - **解決**: Stage 2が変化を検出した場合、Stage 3をスキップしてStage 2結果を返す
   - **副次効果**: Stage 3のSSIM計算（75秒）をスキップ → 処理時間大幅短縮

### Phase 4（将来的な改善）
8. **オーバーレイ管理改善** - テキスト変更時のみ更新
9. **テキストベース変化検知の検討** - OCR結果の差分比較

---

## 6. 関連ファイル

| ファイル | 役割 |
|----------|------|
| `Baketa.Infrastructure/Imaging/ChangeDetection/EnhancedImageChangeDetectionService.cs` | 3段階変化検知サービス |
| `Baketa.Infrastructure/Imaging/ChangeDetection/OptimizedPerceptualHashService.cs` | ハッシュ計算サービス |
| `Baketa.UI/appsettings.json` | 閾値設定 |
| `Baketa.Core/Models/ImageProcessing/ImageChangeModels.cs` | 変化検知結果モデル |

---

## 7. 参考情報

### Perceptual Hash の限界
- 8x8ハッシュは「画像全体が似ているか」を判定する設計
- 局所的なテキスト変更の検出には不向き
- 元々は重複画像検出や類似画像検索用のアルゴリズム

### 代替アルゴリズム候補
- **SSIM（Structural Similarity）**: 高精度だが計算コスト高
- **Feature-based（SIFT/ORB）**: 特徴点マッチング、テキストには不向き
- **OCR差分比較**: 最も正確だがOCR実行が必要

---

*最終更新: 2025-12-22*
*関連Issue: #230*
