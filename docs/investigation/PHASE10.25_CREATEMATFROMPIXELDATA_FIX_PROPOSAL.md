# Phase 10.25: RGB画像破損の根本解決 - CreateMatFromPixelData修正方針

## 🎯 調査結果サマリー

### 真の根本原因100%特定

**問題のコールチェーン**:
```
WindowsImageFactory (Bgra32 IImage生成)
  ↓
PaddleOcrImageProcessor.ConvertToMatAsync()
  ↓
CreateMatFromPixelData() ← ✅ ここで4チャンネルMat生成
  ↓ (MatType.CV_8UC4)
ApplyPreventiveNormalization() (4チャンネルMatを処理)
  ↓
before_resize デバッグ画像保存 (4チャンネル → PNG保存 → RGB破損)
  ↓
prevention_odd デバッグ画像保存 (4チャンネル → PNG保存 → RGB破損)
```

**コード証拠** (`PaddleOcrImageProcessor.cs:906-918`):
```csharp
MatType matType = image.PixelFormat switch
{
    ImagePixelFormat.Bgra32 => MatType.CV_8UC4, // ← 4チャンネル生成
    ImagePixelFormat.Rgb24 => MatType.CV_8UC3,
    ImagePixelFormat.Bgr24 => MatType.CV_8UC3,
    ImagePixelFormat.Rgba32 => MatType.CV_8UC4, // ← 4チャンネル生成
    ImagePixelFormat.Gray8 => MatType.CV_8UC1,
    _ => throw new NotSupportedException($"Unsupported pixel format: {image.PixelFormat}")
};
```

### Phase 10.17の問題点

**Phase 10.17の修正内容**:
- `PaddleOcrEngine.cs`のデバッグ画像保存箇所で、Cv2.ImWrite()直前に4→3チャンネル変換を実装
- **問題**: デバッグコード専用の対症療法で、根本解決になっていない

**Phase 10.17が効かない理由**:
- デバッグ画像保存のみ修正しても、PaddleOCRエンジン全体で4チャンネルMatが流通している
- 他の箇所でImWrite()を使用する可能性があり、同じ問題が再発するリスク

---

## 🛠️ Phase 10.25: 根本解決策

### 修正方針: CreateMatFromPixelData内でBGR24統一

**実装場所**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrImageProcessor.cs`

**修正内容**:
```csharp
private Mat CreateMatFromPixelData(IImage image)
{
    using var pixelLock = image.LockPixelData();
    var imageData = pixelLock.Data;
    var stride = pixelLock.Stride;

    _logger?.LogDebug("🔥 [PHASE12.3] PixelDataLock取得: Width={Width}, Height={Height}, Stride={Stride}, PixelFormat={PixelFormat}",
        image.Width, image.Height, stride, image.PixelFormat);

    // PixelFormatに基づいてMatTypeを決定
    MatType matType = image.PixelFormat switch
    {
        ImagePixelFormat.Bgra32 => MatType.CV_8UC4,
        ImagePixelFormat.Rgb24 => MatType.CV_8UC3,
        ImagePixelFormat.Bgr24 => MatType.CV_8UC3,
        ImagePixelFormat.Rgba32 => MatType.CV_8UC4,
        ImagePixelFormat.Gray8 => MatType.CV_8UC1,
        _ => throw new NotSupportedException($"Unsupported pixel format: {image.PixelFormat}")
    };

    unsafe
    {
        fixed (byte* dataPtr = imageData)
        {
            var mat = Mat.FromPixelData(
                image.Height,
                image.Width,
                matType,
                (IntPtr)dataPtr,
                stride
            );

            // 🔥 [PHASE10.25] 4チャンネル → BGR24 (3チャンネル) 統一変換
            // 根本解決: PaddleOCRエンジン全体で常に3チャンネルMatを保証
            if (matType == MatType.CV_8UC4) // Bgra32 or Rgba32
            {
                var bgrMat = new Mat();

                // BGRA → BGR または RGBA → BGR 変換
                var conversionCode = image.PixelFormat == ImagePixelFormat.Bgra32
                    ? ColorConversionCodes.BGRA2BGR
                    : ColorConversionCodes.RGBA2BGR;

                Cv2.CvtColor(mat, bgrMat, conversionCode);
                mat.Dispose(); // 元の4チャンネルMatを破棄

                _logger?.LogInformation("✅ [PHASE10.25] 4チャンネル→BGR24変換完了: {PixelFormat} → BGR24",
                    image.PixelFormat);

                return bgrMat.Clone(); // PixelDataLockから独立したMatを返す
            }

            // 3チャンネルまたは1チャンネルはそのまま
            return mat.Clone();
        }
    }
}
```

### 期待効果

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| **CreateMatFromPixelData出力** | 4チャンネル (Bgra32) | **常に3チャンネル (BGR24)** |
| **PaddleOCRエンジン全体** | 4チャンネルMatが流通 | **常に3チャンネルMatのみ** |
| **デバッグ画像保存** | RGB破損 (4→3バイトズレ) | **正常保存 (BGR24 → PNG)** |
| **Cv2.ImWrite()全箇所** | 個別修正必要 | **修正不要 (自動的にBGR24)** |
| **パフォーマンス影響** | なし | **軽微 (初回変換1回のみ)** |

---

## 🔍 Geminiレビュー依頼事項

### 1. アーキテクチャ妥当性

**質問**: `CreateMatFromPixelData()`でのチャンネル統一は、Clean Architecture原則に準拠していますか？

**懸念点**:
- Infrastructure層（PaddleOcrImageProcessor）がOpenCVの具体的な色空間を決定している
- IImage → Mat変換のタイミングでチャンネル数を変更することの妥当性

**代替案の検討**:
- Option A: WindowsImageFactory内でBgr24生成に統一する
- Option B (現在の方針): CreateMatFromPixelData内でBGR24変換
- Option C: ApplyPreventiveNormalization内でチャンネル正規化

### 2. パフォーマンス影響

**質問**: 毎回4→3チャンネル変換することのパフォーマンス影響は許容範囲内ですか？

**測定予定**:
- 1920x1080画像のBGRA→BGR変換時間（予想: <5ms）
- OCR全体処理時間への影響率（予想: <1%）

### 3. メモリ管理

**質問**: 以下のリソース管理は正しいですか？

```csharp
Cv2.CvtColor(mat, bgrMat, conversionCode);
mat.Dispose(); // 元の4チャンネルMatを破棄
return bgrMat.Clone(); // PixelDataLockから独立したMatを返す
```

**懸念点**:
- `mat.Clone()`は元のmatとメモリ共有しないため、PixelDataLock.Dispose()後も安全か？
- `bgrMat.Clone()`の二重コピーは必要か？直接`bgrMat`を返せないか？

### 4. 既存コードへの影響

**質問**: この修正により、既存のOCR処理ロジックに破壊的変更はありますか？

**確認事項**:
- PaddleOCR PP-OCRv5は3チャンネルBGR画像を前提としているか？
- 4チャンネルMatを期待しているコードは存在しないか？

### 5. Phase 10.17コードの扱い

**質問**: Phase 10.17のデバッグ画像保存前の変換コードは削除すべきですか？

**判断基準**:
- Phase 10.25実装後、Phase 10.17は冗長になる
- しかし、多重防御（Defense in Depth）の観点から残す選択肢もある

---

## 📋 実装後の検証計画

### ステップ1: ビルド検証
```bash
dotnet build Baketa.Infrastructure/Baketa.Infrastructure.csproj --configuration Debug
dotnet build Baketa.UI/Baketa.UI.csproj --configuration Debug
```

### ステップ2: ログ検証
期待ログ:
```
✅ [PHASE10.25] 4チャンネル→BGR24変換完了: Bgra32 → BGR24
```

### ステップ3: デバッグ画像検証
- `before_resize_*.png`: RGB破損なし、BGR24フォーマット
- `prevention_odd_*.png`: RGB破損なし、BGR24フォーマット

### ステップ4: OCR精度検証
- 翻訳処理が正常に実行されること
- OCR認識精度が変わらないこと

---

## 🎯 結論

**Phase 10.25修正方針の利点**:
1. ✅ **根本解決**: PaddleOCRエンジン全体で常にBGR24を保証
2. ✅ **保守性向上**: Cv2.ImWrite()使用箇所の個別修正不要
3. ✅ **拡張性**: 将来のデバッグコード追加時も自動対応
4. ✅ **一貫性**: OpenCV標準のBGR色空間に統一

**Geminiフィードバック期待事項**:
- アーキテクチャ設計の妥当性評価
- パフォーマンス影響の妥当性判断
- メモリ管理の正確性確認
- 実装方法の改善提案

**最終判断**: Geminiレビュー後、承認が得られればPhase 10.25実装を進める
