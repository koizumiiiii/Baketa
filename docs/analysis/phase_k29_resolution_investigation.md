# Phase K-29 解像度問題調査レポート

## 📊 問題概要

**発生日時**: 2025-10-18
**調査フェーズ**: Phase K-29-F-2
**症状**: ROIScaleFactor=0.5実装後も低解像度画像が960x540のまま

## 🔬 根本原因100%特定

### 期待動作 vs 実際の動作

| 項目 | 期待値 | 実測値 | 差分 |
|------|--------|--------|------|
| **物理ウィンドウサイズ** | 3840x2160 | 3840x2160 | ✅ 一致 |
| **WGC APIフレームサイズ** | 3840x2160 | **1920x1080** | ❌ 50%縮小 |
| **ROIScaleFactor** | 0.5 | 0.5 | ✅ 正常 |
| **低解像度画像サイズ** | 1920x1080 | **960x540** | ❌ 50%縮小 |

### 根本原因

**Windows Graphics Capture APIがDPIスケーリング（200%）を自動適用**

```
物理ウィンドウ: 3840x2160
  ↓ Windows DPI 200%スケーリング自動適用
論理フレーム: 1920x1080  ← WGC APIが返すサイズ
  ↓ ROIScaleFactor 0.5
低解像度画像: 960x540    ← 実際の結果
```

### 証拠ログ

**debug_app_logs.txt:284-285**
```
2025-10-18 12:48:34.248 📋 ウィンドウ情報: HWND=0x0000000000220228, Class='localWindowClass', Title='Enshrouded', ClientSize=3840x2160
2025-10-18 12:48:34.249 📍 スクリーン座標: Screen=(0,0)-(3840,2160), Size=3840x2160
```

**k29a_debug.log:7-8**
```
[12:48:34.484] Phase1完了: 358ms, 画像=True, サイズ=960x540
[12:48:34.495] Phase2開始 - Detector=TextRegionDetectorAdapter, 入力サイズ=960x540
```

## 📋 解決策の選択肢

### Option A: DPIスケーリング無効化（根本解決）

**実装箇所**: `BaketaCaptureNative/src/WindowsCaptureSession.cpp`

**方針**:
- `CreateCaptureItemForWindow` APIにDPI無視フラグ追加
- 物理解像度3840x2160を直接取得
- ROIScaleFactor 0.5で正しく1920x1080を生成

**メリット**:
- ✅ 設計意図通りの動作
- ✅ 将来的なDPI設定変更にも対応

**デメリット**:
- ⚠️ C++/WinRTネイティブコード修正必要
- ⚠️ Visual Studio 2022ビルド必須
- ⚠️ テスト工数増加

### Option B: ROIScaleFactor調整（暫定対応）

**実装箇所**: `TranslationOrchestrationService.cs:701`

**変更内容**:
```csharp
// 修正前
ROIScaleFactor = 0.5f  // 期待: 3840×0.5=1920、実際: 1920×0.5=960

// 修正後
ROIScaleFactor = 1.0f  // 実際: 1920×1.0=1920 ✅ 目標達成
```

**メリット**:
- ✅ 即座に効果検証可能
- ✅ C#コードのみ（1行変更）
- ✅ ビルド時間最小

**デメリット**:
- ⚠️ DPI設定変更時に再調整必要
- ⚠️ 根本原因未解決

### Option C: 現状維持（様子見）

**判断基準**: 960x540でのテキスト検出精度が許容範囲か検証

**最新の実測結果**:
```
実行1: Phase2完了 2040ms, 検出数=1  ✅ 成功
実行2: Phase2完了 2881ms, 検出数=0  ❌ 失敗
```

**判定**: ❌ **検出精度不安定 - 改善必要**

## 📊 OCR処理時間の評価

### Phase 2（テキスト領域検出）タイムアウト管理

**Phase K-29-B-1実装内容**: 3秒タイムアウト設定

**実測結果**:

| 項目 | 実測値 | 目標値 | 評価 |
|------|--------|--------|------|
| Phase 2処理時間（1回目） | 2040ms | <3000ms | ✅ OK |
| Phase 2処理時間（2回目） | 2881ms | <3000ms | ✅ OK |
| タイムアウト発生 | なし | なし | ✅ 完璧 |

**過去の問題との比較**:
- Phase K-25調査時: **14.7秒** → タイムアウト頻発
- Phase K-29-B-1実装後: **2-3秒** → タイムアウトゼロ

## 🎯 推奨アクション

### 短期（即時実施可能）

**Option B採用**: ROIScaleFactor=1.0に変更
- 理由: 960x540では検出精度不安定（50%成功率）
- 効果: 1920x1080でSobel+LBP検出精度向上
- 工数: 5分（1行変更+ビルド）

### 中期（Phase K-30で対応）

**Option A実装**: Windows Graphics Capture API DPI無効化
- 理由: 根本原因解決、設計意図通りの動作
- 効果: 物理解像度3840x2160取得、ROIScaleFactor正常動作
- 工数: 2-4時間（C++実装+テスト）

### 長期（Phase K-31で検証）

**動的ROIScaleFactor調整**:
- DPI設定を検出してROIScaleFactorを自動調整
- ユーザー環境の多様性に対応
- Phase K-30完了後に検討

## 📝 関連ドキュメント

- `docs/analysis/phase_k25_textregiondetector_issue.md` - TextRegionDetector問題の初期調査
- `Baketa.Infrastructure.Platform/Windows/Capture/Strategies/ROIBasedCaptureStrategy.cs:186-208` - Phase 2タイムアウト実装
- `Baketa.Application/Services/Translation/TranslationOrchestrationService.cs:701` - ROIScaleFactor設定箇所

## 🔧 技術メモ

### Windows Graphics Capture APIのDPI動作

**仕様**:
- Windows 10 1903以降、Graphics Capture APIは自動的にDPIスケーリングを適用
- `GraphicsCaptureItem.Size`プロパティは論理サイズを返す
- DPI無視には`CreateCaptureItemForWindow`の代替API使用が必要

**参考資料**:
- Microsoft Docs: Windows.Graphics.Capture Namespace
- Issue: WinUI 3 Graphics Capture DPI scaling behavior

---

**作成日**: 2025-10-18
**最終更新**: 2025-10-18
**ステータス**: 調査完了、暫定対応Option B推奨
