# ROI Manager - 学習型テキスト検出最適化

Issue #293: ゲーム/ウィンドウ別にテキスト出現位置を学習し、翻訳処理を最適化

## 概要

ROI Manager（Region of Interest Manager）は、ゲーム画面上のテキスト出現位置を**自動学習**し、OCR処理と翻訳を最適化するシステムです。

### 主な機能

1. **ヒートマップ学習**: テキスト頻出領域を16x16グリッドで学習
2. **動的閾値制御**: 学習結果に基づいてOCR処理閾値を自動調整
3. **部分OCR実行**: 変化領域のみをOCR処理（全画面OCRをスキップ）
4. **ゲーム別プロファイル**: 実行ファイルごとに学習データを永続化

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           ROI Manager System                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐   │
│  │  RoiManager     │────▶│RoiLearningEngine│────▶│ RoiHeatmapData  │   │
│  │  (統括管理)     │     │  (学習エンジン)  │     │ (16x16グリッド) │   │
│  └─────────────────┘     └─────────────────┘     └─────────────────┘   │
│           │                                               │             │
│           ▼                                               ▼             │
│  ┌─────────────────┐                            ┌─────────────────┐    │
│  │RoiThresholdProvider│                         │ RoiProfile      │    │
│  │  (動的閾値計算)  │                            │ (永続化データ)  │    │
│  └─────────────────┘                            └─────────────────┘    │
│           │                                               │             │
│           ▼                                               ▼             │
│  ┌─────────────────────────────┐               ┌─────────────────┐    │
│  │EnhancedImageChangeDetection │               │RoiProfileRepository│ │
│  │       Service               │               │  (JSONファイル)  │    │
│  └─────────────────────────────┘               └─────────────────┘    │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## 動作原理

### 1. ヒートマップ学習

画面を16x16のグリッドに分割し、各セルでのテキスト検出頻度を記録します。

```
┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐
│   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│   │   │░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│   │   │   │   │ ← メニュー領域
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │   │
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│███│███│███│   │   │   │   │   │   │   │   │   │   │   │   │   │ ← ステータス
├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│░░░│ ← 字幕領域
└───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘

  ░░░ = 高頻度 (heatmap > 0.7)
  ███ = 中頻度 (0.3 < heatmap < 0.7)
  　　 = 低頻度 (heatmap < 0.3)
```

### 2. 動的閾値制御

ヒートマップ値に基づいて、画面変化検出の閾値を動的に調整します。

| 領域タイプ | ヒートマップ値 | 閾値調整 | 効果 |
|-----------|--------------|---------|------|
| テキスト頻出領域 | > 0.7 | ×1.02（厳格化） | 小さな変化も検出 |
| 通常領域 | 0.3〜0.7 | ×1.0（標準） | デフォルト動作 |
| 背景領域 | < 0.3 | ×0.95（緩和） | ノイズ無視 |

### 3. 部分OCR実行

学習完了後、変化が検出された領域のみをOCR処理します。

```
全画面OCR (従来)          部分OCR (ROI Manager)
┌─────────────────┐      ┌─────────────────┐
│█████████████████│      │                 │
│█████████████████│      │                 │
│█████████████████│  →   │                 │
│█████████████████│      │                 │
│█████████████████│      │░░░░░░░░░░░░░░░░░│ ← 変化領域のみOCR
└─────────────────┘      └─────────────────┘

処理時間: ~6秒            処理時間: <1秒
```

## プロファイル永続化

### 保存場所
```
%USERPROFILE%\.baketa\roi-profiles\
└── {SHA256_of_exe_path}.json
```

### プロファイル構造
```json
{
  "id": "a1b2c3d4...",
  "executablePath": "C:\\Games\\Example\\game.exe",
  "windowTitle": "Example Game",
  "heatmap": {
    "gridWidth": 16,
    "gridHeight": 16,
    "cells": [[0.0, 0.1, ...], ...]
  },
  "exclusionZones": [],
  "createdAt": "2026-01-25T12:00:00Z",
  "updatedAt": "2026-01-25T12:30:00Z",
  "detectionCount": 150
}
```

## コンポーネント詳細

### IRoiManager

ROI管理の中核インターフェース。

```csharp
public interface IRoiManager
{
    // プロファイル管理
    RoiProfile? CurrentProfile { get; }
    Task<RoiProfile> GetOrCreateProfileAsync(string executablePath, string windowTitle, ...);

    // 閾値取得
    float GetThresholdAt(float normalizedX, float normalizedY, float defaultThreshold);
    float GetHeatmapValueAt(float normalizedX, float normalizedY);

    // 学習データ報告
    void ReportTextDetection(NormalizedRect bounds, float confidence);
    Task ReportTextDetectionsAsync(IEnumerable<(NormalizedRect, float)> detections, ...);

    // 除外ゾーン
    bool IsInExclusionZone(float normalizedX, float normalizedY);
    void AddExclusionZone(NormalizedRect zone);
}
```

### RoiLearningEngine

ヒートマップ学習を担当。

```csharp
public interface IRoiLearningEngine
{
    void RecordDetection(NormalizedRect bounds, float confidence);
    RoiHeatmapData GetCurrentHeatmap();
    float GetNormalizedValue(int gridX, int gridY);
    void Reset();
}
```

### RoiThresholdProvider

動的閾値を計算。

```csharp
public interface IRoiThresholdProvider
{
    float GetThreshold(float normalizedX, float normalizedY, float baseThreshold);
}
```

## 設定

### appsettings.json
```json
{
  "Roi": {
    "EnableRoiManager": true,
    "EnableRoiBasedThreshold": true,
    "HeatmapGridSize": 16,
    "MinConfidenceForLearning": 0.9,
    "ThresholdMultiplierHighFrequency": 1.02,
    "ThresholdMultiplierLowFrequency": 0.95
  }
}
```

## パフォーマンス効果

| 項目 | 従来 | ROI Manager適用後 |
|------|------|------------------|
| OCR処理時間 | ~6秒（全画面） | <1秒（部分領域） |
| 処理対象ピクセル | 921,600 (1280x720) | ~92,160 (10%) |
| 不要なAPI呼び出し | 多数 | 大幅削減 |
| 学習後の精度 | - | 向上（ノイズ除去） |

## 関連ファイル

### Core層
| ファイル | 説明 |
|---------|------|
| `Baketa.Core/Abstractions/Roi/IRoiManager.cs` | 管理インターフェース |
| `Baketa.Core/Abstractions/Roi/IRoiLearningEngine.cs` | 学習エンジンIF |
| `Baketa.Core/Abstractions/Roi/IRoiThresholdProvider.cs` | 閾値プロバイダIF |
| `Baketa.Core/Models/Roi/RoiProfile.cs` | プロファイルモデル |
| `Baketa.Core/Models/Roi/RoiHeatmapData.cs` | ヒートマップデータ |

### Infrastructure層
| ファイル | 説明 |
|---------|------|
| `Baketa.Infrastructure/Roi/RoiManager.cs` | 管理実装 |
| `Baketa.Infrastructure/Roi/Services/RoiLearningEngine.cs` | 学習実装 |
| `Baketa.Infrastructure/Roi/Services/RoiThresholdProvider.cs` | 閾値実装 |
| `Baketa.Infrastructure/Roi/Services/RoiRegionMerger.cs` | 領域結合 |
| `Baketa.Infrastructure/Roi/Persistence/RoiProfileRepository.cs` | 永続化 |

### 統合ポイント
| ファイル | 説明 |
|---------|------|
| `Baketa.Infrastructure/Processing/Services/EnhancedImageChangeDetectionService.cs` | 変化検出統合 |
| `Baketa.Infrastructure/Processing/Strategies/OcrExecutionStageStrategy.cs` | 部分OCR実行 |
| `Baketa.Infrastructure/Text/ChangeDetection/TextChangeDetectionService.cs` | Gatekeeper統合 |

## 学習フェーズ

### Phase 1: 初期学習
- 最初の50回の検出で基本パターンを学習
- 実行間隔: 5秒ごと

### Phase 2: 通常学習
- 高信頼度領域が3つ以上確立されるまで
- 実行間隔: 15秒ごと

### Phase 3: 維持モード
- 学習完了後のメンテナンス
- 実行間隔: 60秒ごと
- 部分OCRが有効化

## トラブルシューティング

### 学習がリセットされる
- プロファイルIDは実行ファイルパスのSHA256
- パスが変わると新規プロファイルになる

### 部分OCRが実行されない
- 変化領域が画面の70%以上を占める場合は全画面OCR
- 領域数が5個を超える場合も全画面OCR

### 除外ゾーンの設定
```csharp
// UIボタン領域など、翻訳不要な領域を除外
_roiManager.AddExclusionZone(new NormalizedRect(0.9f, 0.9f, 0.1f, 0.1f));
```

---

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2026-01-25 | 初版作成 |
| 2026-01-24 | Phase 7-10 実装完了（部分OCR、Gatekeeper統合、投機的OCR） |
| 2026-01-22 | Phase 6 実装完了（ROI領域のみOCR） |
| 2026-01-20 | Phase 1-5 実装完了（基盤、学習、動的閾値、永続化） |
