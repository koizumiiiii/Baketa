# 画面変化検知（Image Change Detection）

## 概要

Baketaの画面変化検知システムは、ゲーム画面のテキスト変化を高速かつ正確に検出し、不要なOCR処理を削減するための3段階フィルタリングアーキテクチャを採用しています。

## 関連ファイル

| レイヤー | ファイルパス | 役割 |
|---------|-------------|------|
| Core (設定) | `Baketa.Core/Settings/ImageChangeDetectionSettings.cs` | 閾値設定 |
| Core (モデル) | `Baketa.Core/Models/ImageProcessing/ImageChangeModels.cs` | 検知結果モデル |
| Infrastructure | `Baketa.Infrastructure/Imaging/ChangeDetection/EnhancedImageChangeDetectionService.cs` | 3段階フィルタリング実装 |
| Infrastructure | `Baketa.Infrastructure/Imaging/ChangeDetection/OptimizedPerceptualHashService.cs` | ハッシュアルゴリズム実装 |
| Strategy | `Baketa.Infrastructure/Processing/Strategies/ImageChangeDetectionStageStrategy.cs` | パイプライン統合 |

## アルゴリズム

### 使用ハッシュアルゴリズム

| アルゴリズム | ハッシュサイズ | 特性 | 用途 |
|-------------|--------------|------|------|
| **AverageHash** | 32x32 (1024bit) | 全体平均輝度に基づく | 背景変化検知 |
| **DifferenceHash** | 32x32 (1024bit) | エッジ（輝度勾配）に敏感 | テキスト変更検知（Stage 1推奨） |
| **PerceptualHash** | 32x32 (1024bit) | DCT（離散コサイン変換）ベース | 高精度検知（Stage 2-3） |
| **WaveletHash** | 16x16 (256bit) | Haar Wavelet変換 | ゲーム画面特化（Stage 3） |

### 類似度計算

```
similarity = 1.0 - (ハミング距離 / 最大ビット数)
```

- **ハミング距離**: 2つのハッシュが異なるビット数
- **16進数ベース計算**: 1文字=4bit、高速化のため文字単位で比較

## 3段階フィルタリングアーキテクチャ

### 処理フロー

```
┌─────────────────────────────────────┐
│ 入力: 現在画像 (currentImage)       │
│ 前回キャッシュ: GridHashCache       │
└──────────────┬──────────────────────┘
               ↓
    ╔═══════════════════════════════╗
    ║ Stage 1: Grid Quick Filter    ║ (~0.5ms)
    ╚═════════════┬═════════════════╝
        • 画面を 4x4=16 ブロック に分割
        • 各ブロック: 16x16 DifferenceHash (256bit)
        • 並列計算: Task.WhenAll で 16 タスク同時実行
        • 判定: 1ブロックでも類似度 < 0.90 → 変化あり

        → 90% のフレームをここで除外
               ↓ HasPotentialChange = true
    ╔═══════════════════════════════╗
    ║ Stage 2: Change Validation    ║ (~0.5ms)
    ╚═════════════┬═════════════════╝
        • ノイズフィルタリング（カーソル点滅等を除外）
        • 判定条件:
          - 単一ブロック + 端 + 高類似度 → ノイズ
          - 複数ブロック OR 隣接ブロック → 有意な変化
          - 中央ブロック OR 低類似度 → 大きな変化

        → 8% のフレームをここで除外
               ↓ IsSignificantChange = true
    ╔═══════════════════════════════╗
    ║ Stage 3: Region Analysis      ║ (~2ms)
    ╚═════════════┬═════════════════╝
        • 変化ブロックの領域を収集
        • 総面積・変化率計算
               ↓
    ╔═══════════════════════════════╗
    ║ テキスト安定化待機            ║ (500-3000ms)
    ╚═════════════┬═════════════════╝
        • タイプライター効果対応
        • 変化検出 → 500ms待機 → 安定化確認
        • 最大3秒でタイムアウト
               ↓
    ┌────────────────────────────────────┐
    │ 出力: ImageChangeResult             │
    │  - HasChanged: bool                 │
    │  - ChangePercentage: 0.0-1.0       │
    │  - ChangedRegions: Rectangle[]      │
    │  - DetectionStage: 1/2/3           │
    └────────────────────────────────────┘
```

### グリッド分割

```
フルスクリーン画像 (1920x1080)
┌─────────────┬─────────────┬─────────────┬─────────────┐
│ Block 0     │ Block 1     │ Block 2     │ Block 3     │
│ (480x270)   │ (480x270)   │ (480x270)   │ (480x270)   │
├─────────────┼─────────────┼─────────────┼─────────────┤
│ Block 4     │ Block 5     │ Block 6     │ Block 7     │
│ (480x270)   │ (480x270)   │ (480x270)   │ (480x270)   │
├─────────────┼─────────────┼─────────────┼─────────────┤
│ Block 8     │ Block 9     │ Block 10    │ Block 11    │
│ (480x270)   │ (480x270)   │ (480x270)   │ (480x270)   │
├─────────────┼─────────────┼─────────────┼─────────────┤
│ Block 12    │ Block 13    │ Block 14    │ Block 15    │
│ (480x270)   │ (480x270)   │ (480x270)   │ (480x270)   │
└─────────────┴─────────────┴─────────────┴─────────────┘

各ブロック: 16x16 ハッシュ（256bit）
並列計算: Task.WhenAll で 16 タスク同時実行
```

## 設定パラメータ

`appsettings.json`:

```json
"ImageChangeDetection": {
  // === 3段階フィルタリング閾値 ===
  "Stage1SimilarityThreshold": 0.90,
  "Stage2ChangePercentageThreshold": 0.02,
  "Stage3SSIMThreshold": 0.70,
  "RegionSSIMThreshold": 0.92,

  // === キャッシング ===
  "EnableCaching": true,
  "MaxCacheSize": 1000,
  "CacheExpirationMinutes": 30,

  // === グリッド分割設定 ===
  "EnableGridPartitioning": true,
  "GridRows": 4,
  "GridColumns": 4,
  "GridBlockSimilarityThreshold": 0.90,

  // === テキスト安定化設定 ===
  "EnableTextStabilization": true,
  "TextStabilizationDelayMs": 500,
  "MaxStabilizationWaitMs": 3000
}
```

### パラメータ説明

| パラメータ | デフォルト | 説明 |
|-----------|-----------|------|
| `Stage1SimilarityThreshold` | 0.90 | グリッドブロックの類似度閾値（90%未満で変化検知） |
| `Stage2ChangePercentageThreshold` | 0.02 | 変化率閾値（2%以上で有意な変化） |
| `Stage3SSIMThreshold` | 0.70 | SSIM高精度検証の閾値 |
| `GridBlockSimilarityThreshold` | 0.90 | ブロック単位の厳格な閾値 |
| `TextStabilizationDelayMs` | 500 | テキストアニメーション待機時間 |
| `MaxStabilizationWaitMs` | 3000 | 最大待機時間（無限待機防止） |

## パフォーマンス最適化

### キャッシング戦略

| キャッシュ種別 | 保持内容 | TTL |
|---------------|---------|-----|
| QuickHashCache | AverageHash + DifferenceHash | 30分 |
| GridHashCache | 16ブロック全ハッシュ + チェックサム | 30分 |
| StabilizationState | 安定化モード情報 | Clear時 |

### 早期終了

- **Stage 1**: 1ブロックで閾値下回れば即座に「変化あり」判定
- **Stage 2**: ノイズ判定で即座に「変化なし」返却
- **Stage 3**: 有意な変化確定後のみ領域分析

### メモリ管理

`ArrayPool<byte>.Shared` を使用してGC圧力を軽減。

## 特殊機能

### テキスト安定化（Issue #229）

タイプライター効果（文字が一文字ずつ表示）に対応：

1. 変化検出 → OCR抑制（NoChange返却）
2. 500ms待機
3. 変化なし → 安定化完了 → OCR実行
4. 3秒タイムアウト → 強制実行

### チェックサムフォールバック

ハッシュ衝突時の検出漏れ防止：
- ハッシュが同一でも画像が異なる場合を検出
- 下部（テキスト領域）を優先的に変化ブロック化

### 隣接ブロック判定

- 8方向（上下左右＋斜め）の隣接チェック
- 隣接なし + 端ブロック = カーソル点滅（ノイズ）
- 隣接あり = テキスト変更（有意な変化）

## パフォーマンス目標

| ステージ | 処理フレーム割合 | 目標時間 | 実績 |
|---------|-----------------|---------|------|
| Stage 1 | 全フレーム | <1ms | 0.3-0.5ms |
| Stage 2 | 10% | <3ms | 0.5-1.0ms |
| Stage 3 | 2% | <5ms | 2-3ms |

**フィルタリング効率**: 約98%のフレームをStage 1-2で除外

## 関連Issue

- Issue #229: グリッド分割・テキスト安定化
- Issue #230: ハッシュサイズ拡大（8x8→32x32）
