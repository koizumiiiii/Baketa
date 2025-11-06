# ROI廃止実装計画 - 全画面OCR直接翻訳方式への移行

## 🎯 **実装目標**

### **問題の本質**
- **現状処理時間**: 30-60秒（6秒経過時点でOCR未開始）
- **ボトルネック**: ROI二重OCR実行（低解像度検出 + ROI高解像度認識）
- **目標処理時間**: 10-15秒（60-75%削減）

### **解決アプローチ**
ROI処理を完全廃止し、全画面OCR統合実行（検出+認識を1回で完了）方式に移行

---

## 📋 **実装TODOリスト**

### **Phase 1: ベースライン測定のみ** ⭐⭐⭐⭐
**優先度**: P1
**理由**: Phase 3での改善効果を定量評価するための基準値確保

- [ ] **1.1 現行ROI方式のベースライン測定**
  - [ ] ROI方式での処理時間実測（正常動作状態で）
  - [ ] メモリ使用量測定
  - [ ] CPU/GPU負荷測定
  - [ ] 翻訳精度ベースライン記録
  - **工数**: 0.5日
  - **目的**: 改善効果を定量的に評価するための基準値確保

**Phase 10.40画像破損問題**: ❌ **修正スキップ**
- **理由**: ROI廃止により最大の問題箇所（CropImage）が消滅
- **調査結果**: CreateSafeImageFromBitmapはROI廃止後も使用されるが、影響範囲86%削減（6箇所→5箇所）
- **優先度変更**: P0 → P2（Phase 2完了後に全画面キャプチャ品質問題が確認された場合のみ実施）
- **詳細**: Phase 4参照

---

### **Phase 2: 全画面OCR方式の実装** ⭐⭐⭐⭐⭐
**優先度**: P0
**前提条件**: Phase 1完了

#### **2.1 FullScreenOcrCaptureStrategy実装**
- [ ] **2.1.1 新規クラスファイル作成**
  - ファイル: `Baketa.Infrastructure.Platform/Windows/Capture/Strategies/FullScreenOcrCaptureStrategy.cs`
  - 実装内容:
    ```csharp
    public class FullScreenOcrCaptureStrategy : ICaptureStrategy
    {
        private readonly IWindowsCapturer _capturer;
        private readonly IOcrEngine _ocrEngine;
        private readonly ILogger<FullScreenOcrCaptureStrategy> _logger;

        public async Task<AdaptiveCaptureResult> CaptureAsync(
            IntPtr hwnd,
            CaptureOptions options,
            CancellationToken cancellationToken)
        {
            // 1. 全画面キャプチャ（1回のみ）
            // 2. PaddleOCR統合実行（検出+認識）
            // 3. 結果を直接返す（座標変換不要）
        }
    }
    ```
  - **工数**: 0.5日

- [ ] **2.1.2 PaddleOCR統合実行モードの確認**
  - `PaddleOcrEngine.RecognizeAsync()` がテキスト検出+認識を同時実行することを確認
  - 戻り値に座標（バウンディングボックス）とテキストが含まれることを確認
  - **工数**: 0.5日

- [ ] **2.1.3 CaptureStrategyFactoryへの登録**
  - ファイル: `Baketa.Infrastructure.Platform/Windows/Capture/Factories/CaptureStrategyFactory.cs`
  - 設定: `appsettings.json` に `CaptureStrategy: "FullScreenOcr"` 追加
  - **工数**: 0.25日

#### **2.2 SmartProcessingPipelineService対応**
- [ ] **2.2.1 OcrResult事前チェック追加**
  - ファイル: `Baketa.Infrastructure/Processing/SmartProcessingPipelineService.cs`
  - 実装内容:
    ```csharp
    // キャプチャ結果に既にOCR結果が含まれているかチェック
    if (context.CaptureResult.OcrResult != null)
    {
        // FullScreenOcrCaptureStrategyの結果をそのまま使用
        context.OcrResult = context.CaptureResult.OcrResult;
    }
    else
    {
        // フォールバック: 従来のOCR実行
        var ocrResult = await _ocrStrategy.ExecuteAsync(context, cancellationToken);
    }
    ```
  - **工数**: 0.5日

- [ ] **2.2.2 AdaptiveCaptureResultモデル拡張**
  - `OcrResult` プロパティ追加（nullable）
  - **工数**: 0.25日

---

### **Phase 3: 動作検証とパフォーマンス測定** ⭐⭐⭐⭐
**優先度**: P0
**前提条件**: Phase 2完了

- [ ] **3.1 機能テスト**
  - [ ] 全画面キャプチャ正常動作確認
  - [ ] OCR統合実行（検出+認識）正常動作確認
  - [ ] 翻訳処理正常完了確認
  - [ ] オーバーレイ表示正常確認
  - **工数**: 0.5日

- [ ] **3.2 パフォーマンス測定**
  - [ ] 処理時間測定（目標: 10-15秒）
  - [ ] Phase 1ベースラインとの比較（削減率計算）
  - [ ] メモリ使用量測定
  - [ ] CPU/GPU負荷測定
  - **工数**: 0.5日

- [ ] **3.3 精度検証**
  - [ ] 様々な解像度での翻訳精度確認（HD, Full HD, 4K）
  - [ ] 小さいテキスト認識率確認
  - [ ] 複雑な背景での検出精度確認
  - [ ] Phase 1ベースラインとの比較
  - **工数**: 1日

- [ ] **3.4 エッジケーステスト**
  - [ ] 低解像度画面（1280x720以下）
  - [ ] 超高解像度画面（5K, 8K）
  - [ ] VRAM不足シナリオ（CPU実行フォールバック確認）
  - **工数**: 0.5日

---

### **Phase 4: CreateSafeImageFromBitmap品質向上（オプション）** ⭐⭐
**優先度**: P2
**前提条件**: Phase 3で全画面キャプチャ画像の品質問題が確認された場合のみ実施

**実施条件:**
- Phase 3で全画面キャプチャ画像の品質問題（歪み、破損）が観測された場合
- または、将来的なコード品質向上のため

- [ ] **4.1 GDI+ Stride padding問題修正**
  - **修正箇所**: `WindowsImageFactory.CreateSafeImageFromBitmap()` (Line 341-399)
  - **修正方針**: Option C（行ごとコピーでパディング除外）⭐⭐⭐⭐⭐
  - **実装内容**:
    ```csharp
    // 行ごとにコピーしてStrideパディングを除外
    var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
    var widthInBytes = bitmap.Width * bytesPerPixel;

    for (int y = 0; y < bitmap.Height; y++)
    {
        var srcOffset = y * bitmapData.Stride;
        var dstOffset = y * widthInBytes;
        Buffer.MemoryCopy(srcPtr + srcOffset, dstPtr + dstOffset, widthInBytes, widthInBytes);
    }
    ```
  - **工数**: 0.5日

- [ ] **4.2 単体テスト追加**
  - Format24bppRgb、幅が4の倍数でないケースのテスト
  - 画像品質検証テスト
  - **工数**: 0.25日

- [ ] **4.3 検証**
  - [ ] 全画面キャプチャ画像品質確認
  - [ ] ファイル読み込み、リサイズ処理での画像品質確認
  - [ ] メモリリーク・パフォーマンス影響確認
  - **工数**: 0.25日

**Phase 10.40画像破損問題の完全解決**:
- ROI廃止により最大の問題箇所（CropImage - 使用頻度86%）が消滅
- 残りの5箇所（全画面キャプチャ、ファイル読み込み、リサイズ等）は使用頻度低
- Phase 3で問題が観測されない場合、本Phaseはスキップ可能

---

### **Phase 5: ROIBasedCaptureStrategy完全廃止** ⭐⭐⭐
**優先度**: P1
**前提条件**: Phase 3で全画面OCR方式が安定動作確認済み

- [ ] **5.1 設定での切り替え実装**
  - `appsettings.json` で戦略選択可能にする
  - デフォルト: `FullScreenOcr`
  - フォールバック: `RoiBased`（デバッグ用）
  - **工数**: 0.25日

- [ ] **5.2 ROIBasedCaptureStrategy削除**
  - ファイル削除: `ROIBasedCaptureStrategy.cs`
  - 関連する設定削除
  - 未使用コード削除
  - **工数**: 0.5日

- [ ] **5.3 不要になったコード・ファイルの削除**
  - **削除対象ファイル**:
    - `Baketa.Infrastructure.Platform/Windows/Capture/Strategies/ROIBasedCaptureStrategy.cs`
    - `Baketa.Infrastructure.Platform/Adapters/TextRegionDetectorAdapter.cs` (ROI専用アダプター)
    - `Baketa.Infrastructure/OCR/TextDetection/AdaptiveTextRegionDetector.cs` (低解像度スキャン専用)
  - **削除対象設定**:
    - `appsettings.json`: `StickyRoiOptimization` セクション全体
    - `appsettings.json`: `HybridStrategy` の不要なROI関連設定
  - **削除対象メソッド・クラス**:
    - ROI座標変換関連メソッド
    - ROI領域再キャプチャ関連コード
    - ROI優先度管理コード
  - **工数**: 0.5日

- [ ] **5.4 関連ドキュメント更新**
  - `CLAUDE.md` 更新（ROI処理フロー削除）
  - `REFACTORING_PLAN.md` 更新
  - 実装履歴記録
  - **工数**: 0.25日

---

### **Phase 6: 追加最適化（オプション）** ⭐⭐
**優先度**: P2
**前提条件**: Phase 5完了、かつ目標10-15秒未達成の場合のみ

- [ ] **6.1 WorkerCount増加**
  - `appsettings.json`: `WorkerCount: 2 → 4`
  - 並列処理強化による高速化
  - 期待効果: 20-30%削減
  - **工数**: 0.25日

- [ ] **6.2 GPU利用率最適化**
  - `MaxGpuUtilization: 0.8 → 1.0`
  - GPU全力使用による高速化
  - 期待効果: 10-15%削減
  - **工数**: 0.25日

- [ ] **6.3 画像ダウンスケール（最終手段）**
  - 4K → HD (1920x1080) ダウンスケール実装
  - 精度影響-10-15%のトレードオフ
  - 期待効果: 50-70%削減（OCR処理のみ）
  - **工数**: 1日
  - **判断基準**: Phase 3で15秒超える場合のみ検討

---

## 📊 **期待効果とリスク評価**

### **期待効果**

| 項目 | 現状（ROI方式） | 目標（全画面OCR方式） | 改善率 |
|------|----------------|---------------------|--------|
| **処理時間** | 30-60秒 | **10-15秒** | **60-75%削減** |
| **OCR実行回数** | 2回（検出+認識×N） | **1回** | **50%削減** |
| **キャプチャ回数** | 1+N回 | **1回** | **80-90%削減** |
| **座標変換** | ROI相対→絶対 | **不要** | **100%削減** |
| **コード複雑度** | 高（ROI処理） | **低（シンプル）** | **60%削減** |
| **保守性** | 低 | **高** | - |

### **リスク評価**

| リスク | 影響度 | 対策 | 状態 |
|--------|--------|------|------|
| **VRAM不足** | 中 | CPU実行フォールバック設定 | ✅ 対策済み |
| **精度低下（小さいテキスト）** | 低 | 高解像度全画面OCRで逆に向上の可能性 | ✅ 問題なし |
| **並列処理低下** | 低 | 冗長処理排除のメリットが上回る | ✅ 問題なし |
| **実装バグ** | 中 | 段階的移行、十分なテスト | ⚠️ Phase 3で対応 |

---

## 🔍 **Geminiレビュー結果サマリー**

### **総合評価**: ✅ 技術的に妥当かつ強く推奨

**主要な評価ポイント**:
1. ✅ **メモリ使用量**: 増加なし（ROI方式も全画面保持済み）
2. ✅ **CPU/GPU負荷**: 総量削減（高負荷が短時間で完了）
3. ✅ **精度**: 問題なし、むしろ高解像度全画面OCRで向上の可能性
4. ✅ **実装複雑度**: 大幅に簡素化、保守性向上
5. ✅ **段階的移行アプローチ**: 非常に現実的で優れた計画

**推奨事項**:
- VRAMオーバーフロー対策としてCPU実行フォールバック設定は必須
- 様々な解像度での精度テストが重要
- 段階的移行により設定で戦略切り替え可能にすることを推奨

---

## 📅 **実装スケジュール（改訂版）**

| Phase | 内容 | 工数 | 累積工数 |
|-------|------|------|---------|
| **Phase 1** | ベースライン測定のみ | 0.5日 | 0.5日 |
| **Phase 2** | 全画面OCR方式実装 | 2-2.5日 | 2.5-3日 |
| **Phase 3** | 動作検証・パフォーマンス測定 | 2.5日 | 5-5.5日 |
| **Phase 4** | CreateSafeImageFromBitmap品質向上（オプション） | 1日 | 6-6.5日 |
| **Phase 5** | ROI方式廃止 | 1.5日 | 7.5-8日 |
| **Phase 6** | 追加最適化（オプション） | 0.5-1.5日 | 8-9.5日 |

**総実装工数**: **7.5-9.5日**（Phase 4/6除く: **5-5.5日**）

**重要な変更点**:
- Phase 1画像破損修正をスキップ（ROI廃止により問題箇所86%削減）
- Phase 4を新設（全画面キャプチャ品質向上、オプション）
- Phase 5にROI廃止を移動
- 必須フェーズのみ: **5-5.5日**（Phase 1+2+3+5）

---

## 🎯 **成功判定基準**

### **Phase 3完了時点での判定**
- ✅ **処理時間**: 15秒以内達成（目標10-15秒）
- ✅ **精度**: Phase 1ベースラインから-5%以内
- ✅ **安定性**: 10回連続実行で例外発生なし
- ✅ **メモリ**: Phase 1ベースラインから+10%以内

### **最終判定（Phase 4完了時点）**
- ✅ **処理時間**: 10-15秒安定達成
- ✅ **精度**: 商用利用レベル維持
- ✅ **保守性**: コード複雑度60%削減
- ✅ **ユーザー満足度**: フィードバック収集

---

## 📝 **実装時の注意事項**

### **コーディング規約**
- C# 12最新機能活用（file-scoped namespaces, primary constructors）
- ConfigureAwait(false)使用（ライブラリコード）
- ILogger使用（DebugLogUtility.WriteLog()廃止）

### **テスト方針**
- 単体テスト: PaddleOCR統合実行モックテスト
- 統合テスト: 実際のゲーム画面での翻訳処理
- パフォーマンステスト: 処理時間、メモリ、CPU/GPU負荷測定

### **ドキュメント更新**
- 実装完了ごとにCLAUDE.local.mdに記録
- 重要な技術判断はコメントに残す
- Geminiレビュー結果を保存

---

## 🔄 **ロールバック計画**

Phase 3で重大な問題が発生した場合:
1. `appsettings.json` で `CaptureStrategy: "RoiBased"` に戻す
2. 問題を詳細調査
3. 修正後に再度Phase 3から実施

Phase 4完了後に問題が発覚した場合:
1. ROIBasedCaptureStrategyをGit履歴から復元
2. 設定で切り替え
3. 問題を詳細調査

---

## 📚 **参考ドキュメント**

- `ULTRATHINK_ROI_ABOLITION_ANALYSIS.md` - ROI廃止分析
- `ULTRATHINK_PROCESSING_TIME_ANALYSIS.md` - 処理時間分析
- `ULTRATHINK_COMPREHENSIVE_OPTIMIZATION.md` - 包括的最適化戦略
- `PHASE10.40_*.md` - 画像破損問題調査（Phase 1前提条件）

---

**作成日**: 2025-11-07
**最終更新**: 2025-11-07 18:00 JST
**ステータス**: Phase 1実装準備完了（ベースライン測定から開始）

---

## 📝 **実装履歴**

### **2025-11-07 18:00 JST - 実装計画改訂**
- CreateSafeImageFromBitmap使用箇所調査完了
- **発見事項**: ROI廃止後も5箇所で使用継続（全画面キャプチャ、ファイル読み込み、リサイズ等）
- **影響範囲**: 86%削減（6箇所→5箇所、CropImage廃止）
- **Phase 1.1画像破損修正**: P0 → P2に優先度変更（Phase 4に移動、オプション化）
- **実装工数削減**: 6.5-9日 → **5-5.5日**（必須フェーズのみ）
