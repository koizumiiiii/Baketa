# 🔬 UltraThink調査: NativeWindowsCaptureWrapper画像破損問題 - Phase 1

**調査日時**: 2025-11-03
**問題**: fullImageキャプチャは成功（3840x2160）だが、切り出されたROI画像10個がすべて真っ黒
**調査者**: Claude Code
**方法論**: UltraThink段階的調査

---

## 🎯 Phase 1完了: コード構造と処理フロー理解

### 📊 判明した事実

#### ✅ 正常動作している部分

1. **fullImageキャプチャ**:
   - `NativeWindowsCaptureWrapper.CaptureFrameAsync(5000)` 実行成功
   - サイズ: **3840x2160** （ログ証拠あり）
   - `fullImage != null` チェック通過

2. **座標検出**:
   - 10個のROI領域検出成功
   - 座標は正常範囲内（例: X=268, Y=747, Width=262, Height=87）
   - ログ: `🎯 [OPTION_D_NO_TRANSFORM] 座標変換スキップ: textRegionsは既に高解像度 {X=268,Y=747,Width=262,Height=87}, 画像サイズ: 3840x2160`

3. **CropImage実行**:
   - `_imageFactory.CropImage(fullImage, highResRegion)` 実行
   - 例外発生なし
   - null返却なし（CropImage自体は成功）

#### ❌ 失敗している部分

1. **OCR結果**:
   - 全10個のROI画像で**検出領域数=0**
   - ログ: `✅ [P1-B-FIX] QueuedOCR完了: 検出領域数=0`
   - ログ: `OCRサマリー: テキストが検出されませんでした`

2. **Debug画像確認**:
   - `prevention_odd_20251102_222425_727_262x88.png`: **真っ黒＋上部ノイズ**
   - 本来は「一時停止」テキストが含まれるべき画像

3. **翻訳未実行**:
   - OCR結果が空のため、チャンク作成されず
   - 翻訳処理に到達せず

---

## 🔬 Phase 1調査プロセス

### Step 1: NativeWindowsCaptureWrapper構造理解

**ファイル**: `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCaptureWrapper.cs`

**CaptureFrameAsync実装** (Line 230-311):
```csharp
// Line 257: ネイティブDLL呼び出し
int result = NativeWindowsCapture.BaketaCapture_CaptureFrame(_sessionId, out frame, timeoutMs);

// Line 267: BGRA→Bitmap変換
var bitmap = CreateBitmapFromBGRA(frame);

// Line 270-273: SafeImageAdapter作成
var safeImage = _safeImageFactory.CreateFromBitmap(bitmap, frame.width, frame.height);
var safeImageAdapter = new SafeImageAdapter(safeImage, _safeImageFactory);

return safeImageAdapter;
```

**CreateBitmapFromBGRA実装** (Line 318-391):
```csharp
// Line 337-349: ネイティブメモリから一時Bitmap作成
using var tempBitmap = new Bitmap(
    width: frame.width,
    height: frame.height,
    stride: frame.stride,
    format: PixelFormat.Format32bppArgb,
    scan0: frame.bgraData);

// Line 351-356: Clone()で管理メモリにコピー（メモリ安全性確保）
var bitmap = tempBitmap.Clone(
    new Rectangle(0, 0, tempBitmap.Width, tempBitmap.Height),
    tempBitmap.PixelFormat);

// Line 365-395: 品質検証（中央10x10ピクセルサンプリング）
// ⚠️ LogDebugレベル → ログに出力されていない可能性
_logger?.LogDebug("🎨 安全化品質検証: 黒ピクセル={BlackPixels}/100 ({Percentage:F1}%)",
    blackPixels, blackPixels / 100.0 * 100);
```

**重要な発見**:
- 品質検証コードは存在するが、**LogDebugレベル**のため、ログ設定により出力されない可能性
- 品質検証が実行されていれば、黒ピクセル率が分かるはず

### Step 2: ROIBasedCaptureStrategy処理フロー理解

**ファイル**: `Baketa.Infrastructure.Platform/Windows/Capture/Strategies/ROIBasedCaptureStrategy.cs`

**CaptureHighResRegionsAsync実装** (Line 439-578):
```csharp
// Line 479: fullImageキャプチャ
var fullImage = await _nativeWrapper.CaptureFrameAsync(5000).ConfigureAwait(false);

// Line 489-537: 並列処理でROI領域切り出し
var cropTasks = textRegions.Select(async region =>
{
    // Line 518: CropImage実行
    var croppedImage = _imageFactory.CropImage(fullImage, highResRegion);

    // Line 521-528: 成功/失敗ログ（LogDebugレベル）
    if (croppedImage != null)
    {
        _logger.LogDebug("🎯 [CROP_SUCCESS] 領域キャプチャ完了: ...");
    }
    else
    {
        _logger.LogWarning("🚫 [CROP_FAILED] クロップ失敗: ...");
    }
});
```

**重要な発見**:
- CROP_SUCCESS/CROP_FAILEDログも**LogDebugレベル**
- 実際のログに出力されていない → ログレベル設定がInfo以上の可能性

### Step 3: WindowsImageFactory.CropImage実装確認

**ファイル**: `Baketa.Infrastructure.Platform/Windows/WindowsImageFactory.cs` (Line 237-340)

**実装内容**:
- GDI+ Graphics.DrawImage()を使用した標準的な切り出し処理
- エラーハンドリングも適切
- **実装自体に問題なし**

### Step 4: ログ分析

**検索結果**:
- `安全化品質検証`、`🎨`、`SAFEIMAGE_FIX`、`フレームキャプチャ`: **ログに一切出力なし**
- `OPTION_D_NO_TRANSFORM`: **ログ出力あり**（座標変換スキップ確認済み）
- `CROP_SUCCESS`、`CROP_FAILED`: **ログに一切出力なし**

**結論**: ログレベルがDebugより高い設定になっている可能性が高い

---

## 🎯 Phase 1結論

### 問題の本質（仮説）

1. **fullImageのピクセルデータが破損している可能性が高い**
   - fullImage自体はnullでない（3840x2160サイズ）
   - CropImage実行は成功（例外なし、null返却なし）
   - しかし、切り出されたROI画像がすべて真っ黒
   - → fullImageのピクセルデータが最初から真っ黒の可能性

2. **NativeWindowsCaptureの問題可能性**
   - `BaketaCapture_CaptureFrame()` (C++側)
   - BGRA→RGB変換
   - メモリ転送（ネイティブ→管理）

3. **ログレベル設定問題**
   - CreateBitmapFromBGRAの品質検証ログが出ていない
   - CropImage成功/失敗ログも出ていない
   - → appsettings.jsonでLogLevelをDebugに設定する必要

---

## 🔜 Phase 2計画: ログレベル変更と詳細ログ確認

### 実施項目

1. **ログレベル変更**
   - `appsettings.json`でログレベルをDebugに設定
   - 品質検証ログの出力確認
   - 黒ピクセル率の実測値取得

2. **追加ログ挿入**
   - CreateBitmapFromBGRAにConsole.WriteLineで強制ログ追加
   - fullImage.Width/Heightの実測値確認
   - tempBitmapとbitmapの品質比較

3. **ネイティブDLL側調査**
   - BaketaCaptureNative.dllのソースコード確認（可能であれば）
   - BaketaCapture_CaptureFrame実装の確認
   - BGRAデータの初期化状態確認

### 期待される発見

- 品質検証ログで黒ピクセル率が90%以上であれば、fullImage段階で破損確定
- 黒ピクセル率が低ければ、CropImage以降の問題
- ネイティブDLL側でのメモリ初期化問題の可能性

---

## 📋 Phase 1で特定した調査必要箇所

| コンポーネント | ファイル | Line範囲 | 調査内容 |
|--------------|---------|---------|---------|
| NativeWindowsCapture | NativeWindowsCapture.cs | 76-89 | P/Invoke宣言、BaketaCaptureFrame構造体 |
| CreateBitmapFromBGRA | NativeWindowsCaptureWrapper.cs | 318-391 | 品質検証ログ、黒ピクセル率 |
| CaptureFrameAsync | NativeWindowsCaptureWrapper.cs | 230-311 | fullImage作成プロセス |
| CaptureHighResRegionsAsync | ROIBasedCaptureStrategy.cs | 439-578 | CropImage実行、並列処理 |
| CropImage | WindowsImageFactory.cs | 237-340 | GDI+切り出し処理（問題なし） |

---

**Phase 1ステータス**: ✅ 完了
**Phase 2開始時期**: ログレベル変更後
**推定問題箇所**: NativeWindowsCaptureWrapper.CreateBitmapFromBGRA（ネイティブメモリ→Bitmap変換）
