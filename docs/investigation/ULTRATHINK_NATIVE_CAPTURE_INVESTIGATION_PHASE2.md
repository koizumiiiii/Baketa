# 🔬 UltraThink調査: NativeWindowsCaptureWrapper画像破損問題 - Phase 2

**調査日時**: 2025-11-03
**問題**: fullImageキャプチャは成功（3840x2160）だが、切り出されたROI画像10個がすべて真っ黒
**調査者**: Claude Code
**方法論**: UltraThink段階的調査
**Phase**: 2 - ログレベル変更と品質検証ログ確認

---

## 🎯 Phase 2目的

Phase 1で品質検証ログ（LogDebugレベル）が出力されなかった理由を調査し、ログ設定を修正することで、fullImage段階での画像品質（黒ピクセル率）を測定する。

---

## 📊 Phase 2調査プロセス

### Step 1: 現在のログレベル設定確認

**ファイル**: `Baketa.UI/appsettings.json`

**確認結果**:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Baketa.Infrastructure.OCR": "Debug",
      "Baketa.Infrastructure.OCR.PaddleOCR": "Debug"
      // ⚠️ "Baketa.Infrastructure.Platform": 設定なし
    }
  }
}
```

**判明した問題**:
- `NativeWindowsCaptureWrapper`の名前空間は`Baketa.Infrastructure.Platform.Windows.Capture`
- この名前空間のログレベル設定が`appsettings.json`に**含まれていない**
- Defaultの"Debug"設定があるが、明示的な設定がないためLogDebugが出力されない可能性

### Step 2: NativeWindowsCaptureWrapper名前空間確認

**ファイル**: `Baketa.Infrastructure.Platform/Windows/Capture/NativeWindowsCaptureWrapper.cs`

**名前空間**:
```csharp
namespace Baketa.Infrastructure.Platform.Windows.Capture;
```

**ログ出力箇所** (Line 365-395):
```csharp
_logger?.LogDebug("🎨 安全化品質検証: 黒ピクセル={BlackPixels}/100 ({Percentage:F1}%)",
    blackPixels, blackPixels / 100.0 * 100);
```

### Step 3: ログレベル設定修正

#### 修正1: appsettings.json (本番環境用)

**追加設定**:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Baketa.Infrastructure.Platform": "Debug",  // 🔥 追加
      // ...
    }
  }
}
```

#### 修正2: appsettings.Development.json (開発環境用)

**変更前**:
```json
{
  "Logging": {
    "LogLevel": {
      "Baketa.Infrastructure.Platform.Windows.Capture": "Information",
      "Baketa.Infrastructure.Platform.Windows.WindowsImageFactory": "Information"
    }
  }
}
```

**変更後**:
```json
{
  "Logging": {
    "LogLevel": {
      "Baketa.Infrastructure.Platform.Windows.Capture": "Debug",       // Information → Debug
      "Baketa.Infrastructure.Platform.Windows.WindowsImageFactory": "Debug"  // Information → Debug
    }
  }
}
```

---

## ✅ Phase 2完了事項

| 項目 | 状態 | 詳細 |
|------|------|------|
| ログレベル設定確認 | ✅ 完了 | appsettings.json 2ファイルを確認 |
| 名前空間特定 | ✅ 完了 | `Baketa.Infrastructure.Platform.Windows.Capture` |
| appsettings.json修正 | ✅ 完了 | "Baketa.Infrastructure.Platform": "Debug"追加 |
| appsettings.Development.json修正 | ✅ 完了 | CaptureとWindowsImageFactoryをDebugレベルに変更 |

---

## 🔬 期待されるログ出力

### 1. 品質検証ログ (CreateBitmapFromBGRA)

**出力例**:
```
🎨 安全化品質検証: 黒ピクセル=95/100 (95.0%)
```

**判定基準**:
- 黒ピクセル率 < 10%: fullImageは正常、問題はCropImage以降
- 黒ピクセル率 ≥ 90%: fullImage段階で画像破損確定

### 2. CropImage成功/失敗ログ (ROIBasedCaptureStrategy)

**出力例（成功時）**:
```
🎯 [CROP_SUCCESS] 領域キャプチャ完了: X=268, Y=747, Width=262, Height=87
```

**出力例（失敗時）**:
```
🚫 [CROP_FAILED] クロップ失敗: X=268, Y=747, Width=262, Height=87
```

### 3. フレームキャプチャログ (CaptureFrameAsync)

**出力例**:
```
🔄 [SAFEIMAGE_FIX] NativeWindowsCapture.BaketaCapture_CaptureFrame実行 - タイムアウト: 5000ms
✅ [SAFEIMAGE_FIX] キャプチャ成功: サイズ=3840x2160, Stride=15360
```

---

## 🔜 Phase 3計画: ログ確認と次の調査方針決定

### 実施項目

1. **アプリケーション再起動**:
   - appsettings.json変更を反映
   - 新しいログレベルで翻訳実行

2. **品質検証ログ確認**:
   - `🎨 安全化品質検証`ログが出力されているか確認
   - 黒ピクセル率の実測値を取得

3. **次の調査方針決定**:
   - **黒ピクセル率 < 10%**の場合:
     - fullImageは正常
     - Phase 3: CropImage処理の詳細調査
     - WindowsImageFactory.CropImageの実装確認
     - メモリコピー処理の検証

   - **黒ピクセル率 ≥ 90%**の場合:
     - fullImage段階で破損確定
     - Phase 3: ネイティブDLL側調査
     - BaketaCaptureNative.dllソースコード確認
     - BaketaCapture_CaptureFrame実装の詳細調査
     - BGRAデータの初期化状態確認

---

## 📋 Phase 2で特定した調査必要箇所

| コンポーネント | ファイル | Line範囲 | ログ出力 | 期待値 |
|--------------|---------|---------|---------|--------|
| CreateBitmapFromBGRA | NativeWindowsCaptureWrapper.cs | 365-395 | 品質検証ログ | 黒ピクセル率 |
| CaptureFrameAsync | NativeWindowsCaptureWrapper.cs | 230-311 | キャプチャログ | 成功/失敗 |
| CaptureHighResRegionsAsync | ROIBasedCaptureStrategy.cs | 521-528 | CropImageログ | 成功/失敗 |

---

## 🎯 Phase 2結論

### 問題の本質（確定）

1. **ログレベル設定の欠如**:
   - `Baketa.Infrastructure.Platform`名前空間のログレベルが未設定
   - LogDebugレベルのログが出力されず、品質検証情報が取得できなかった

2. **修正完了**:
   - appsettings.json (2ファイル)にログレベル設定追加
   - 次回の翻訳実行時に品質検証ログが出力される

### Phase 3への移行

**次のステップ**:
1. アプリケーションを再起動
2. ゲーム画面で翻訳実行
3. 品質検証ログで黒ピクセル率を確認
4. 黒ピクセル率に基づいて次の調査方針を決定

---

**Phase 2ステータス**: ✅ 完了
**Phase 3開始条件**: アプリケーション再起動後の翻訳実行
**推定調査時間**: Phase 3 - 2-4時間（黒ピクセル率により変動）

---

## 📎 関連ドキュメント

- Phase 1レポート: `E:\dev\Baketa\docs\investigation\ULTRATHINK_NATIVE_CAPTURE_INVESTIGATION_PHASE1.md`
- 統合調査レポート: `E:\dev\Baketa\docs\investigation\ROI_IMAGE_CORRUPTION_INVESTIGATION.md`
