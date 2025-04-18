# 名前空間移行ガイドライン

*最終更新: 2025年4月18日*

このドキュメントでは、GTTプロジェクトの改善されたアーキテクチャに基づく名前空間構造および移行方針について詳述します。

> **重要**: GTTプロジェクトはWindows専用アプリケーションとして開発を継続し、Linux/macOSへのクロスプラットフォーム対応は行いません。

## 1. 改善されたアーキテクチャの概要

GTTプロジェクトを以下の4つの主要レイヤーに分割します：

```
GTT/
├── GTT.Core/               # プラットフォーム非依存のコア機能
├── GTT.Infrastructure/     # インフラストラクチャ層
├── GTT.Application/        # アプリケーションサービス
└── GTT.UI/                 # UI層
```

## 2. 詳細な名前空間構造

### 2.1 GTT.Core (コア層)

プラットフォーム非依存の基本機能と抽象化を提供します：

```
GTT.Core/
├── GTT.Core.Abstractions/  # 基本抽象化（旧Interfaces）
│   ├── Imaging/            # 画像処理抽象化
│   ├── Capture/            # キャプチャ抽象化
│   ├── Translation/        # 翻訳抽象化
│   └── Common/             # 共通抽象化
├── GTT.Core.Models/        # データモデル
├── GTT.Core.Services/      # コアサービス実装
│   ├── Imaging/            # 画像処理サービス
│   ├── Capture/            # キャプチャサービス
│   └── Translation/        # 翻訳サービス
├── GTT.Core.Events/        # イベント定義と集約機構
└── GTT.Core.Common/        # 共通ユーティリティ
```

### 2.2 GTT.Infrastructure (インフラストラクチャ層)

Windows固有の実装と外部サービス連携を担当します：

```
GTT.Infrastructure/
├── GTT.Infrastructure.Abstractions/  # インフラ抽象化
├── GTT.Infrastructure.Platform/      # プラットフォーム関連機能
│   └── Windows/                      # Windows固有実装
│       ├── Imaging/                  # Windows画像処理
│       ├── Capture/                  # Windowsキャプチャ
│       └── Adapters/                 # Windows用アダプター
├── GTT.Infrastructure.OCR/           # OCR機能
│   ├── PaddleOCR/                    # PaddleOCR実装
│   ├── Services/                     # OCRサービス
│   └── Optimization/                 # OpenCVベースのOCR最適化
├── GTT.Infrastructure.Translation/   # 翻訳機能
│   ├── Engines/                      # 翻訳エンジン
│   ├── ONNX/                         # ONNXモデル実装
│   └── Cloud/                        # クラウドサービス連携
└── GTT.Infrastructure.Persistence/   # 永続化機能
```

### 2.3 GTT.Application (アプリケーション層)

ビジネスロジックと機能統合を担当します：

```
GTT.Application/
├── GTT.Application.Abstractions/     # アプリケーション抽象化
├── GTT.Application.Services/         # アプリケーションサービス
│   ├── OCR/                          # OCRアプリケーションサービス
│   ├── Translation/                  # 翻訳アプリケーションサービス
│   ├── Capture/                      # キャプチャアプリケーションサービス
│   └── Integration/                  # 統合サービス
├── GTT.Application.DI/               # 依存性注入設定
├── GTT.Application.Handlers/         # イベントハンドラー
└── GTT.Application.Configuration/    # アプリケーション設定
```

### 2.4 GTT.UI (UI層)

ユーザーインターフェースとプレゼンテーションロジックを担当します：

```
GTT.UI/
├── GTT.UI.Abstractions/    # UI抽象化
├── GTT.UI.Core/            # UI共通機能
│   ├── Services/           # UI共通サービス
│   ├── Controls/           # 共通コントロール
│   └── ViewModels/         # 共通ビューモデル
└── GTT.UI.Avalonia/        # Avalonia UI実装
    ├── Views/              # XAML Views
    ├── ViewModels/         # ViewModels
    ├── Controls/           # カスタムコントロール
    └── Services/           # Avalonia固有サービス
```

## 3. 命名規則

### 3.1 インターフェース命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本インターフェース | `I[機能名]` | `IImage`, `ICapture` |
| サービスインターフェース | `I[機能名]Service` | `ICaptureService`, `ITranslationService` |
| ファクトリインターフェース | `I[成果物]Factory` | `IImageFactory`, `ICaptureFactory` |
| Windows固有 | `IWindows[機能名]` | `IWindowsImage`, `IWindowsCapture` |

### 3.2 実装クラス命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本実装 | `[機能名]` | `Image`, `Capture` |
| サービス実装 | `[機能名]Service` | `CaptureService`, `TranslationService` |
| アダプター | `[機能名]Adapter` | `WindowsImageAdapter` |
| Windows実装 | `Windows[機能名]` | `WindowsImage`, `WindowsCapture` |

### 3.3 名前空間命名規則

| レイヤー | 命名パターン | 例 |
|---------|--------------|-----|
| コア層 | `GTT.Core.[機能分野]` | `GTT.Core.Imaging`, `GTT.Core.Capture` |
| インフラ層 | `GTT.Infrastructure.[機能/プラットフォーム]` | `GTT.Infrastructure.Platform.Windows` |
| アプリケーション層 | `GTT.Application.[機能]` | `GTT.Application.Services.OCR` |
| UI層 | `GTT.UI.[フレームワーク].[機能]` | `GTT.UI.Avalonia.ViewModels` |
| 抽象化 | `[レイヤー].Abstractions.[機能]` | `GTT.Core.Abstractions.Imaging` |

## 4. 名前空間移行指針

### 4.1 移行ステップ

1. **フェーズ1**: 新しいインターフェース定義の作成
   - 新しい名前空間構造とインターフェース階層の作成
   - 既存インターフェースを`[Obsolete]`に設定

2. **フェーズ2**: アダプターの実装
   - 新旧インターフェース間のブリッジとなるアダプターの実装
   - 既存コードからのスムーズな移行を可能に

3. **フェーズ3**: コアモジュールの移行
   - 基本機能を新しいアーキテクチャに移行
   - イベント集約機構の導入

4. **フェーズ4**: Windows依存コードの整理
   - Windows固有コードの整理と明確な分離
   - Windows関連インターフェースの定義

5. **フェーズ5**: アプリケーション層とUI層の構築
   - 新しいアプリケーションサービスの構築
   - UI層とのインテグレーション

### 4.2 移行時のベストプラクティス

- 移行前にすべての依存関係を確認する
- 一度に関連するクラス群をまとめて移行する
- 移行後は単体テストを実施し機能が保たれていることを確認する
- 名前空間エイリアスを使用して移行期間中の互換性を維持する

## 5. 移行の検証

移行完了後、以下の検証を行います：

1. すべてのプロジェクトがエラーなくビルドできること
2. すべての単体テストが成功すること
3. 主要な機能が正常に動作すること
4. パフォーマンス低下がないこと

## 6. 移行スケジュール

| フェーズ | 対象モジュール | 完了予定 |
|--------|--------------|--------|
| 1      | 新インターフェース定義 | 2025年5月中旬 |
| 2      | アダプターパターン実装 | 2025年5月下旬 |
| 3      | コアモジュール移行 | 2025年6月上旬 |
| 4      | Windows依存コード整理 | 2025年6月下旬 |
| 5      | アプリケーション層とUI層 | 2025年7月中旬 |

移行に関する詳細な質問や課題は開発チームに相談してください。
