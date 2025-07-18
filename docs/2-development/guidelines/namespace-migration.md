# 名前空間移行ガイドライン

*最終更新: 2025年5月3日*

このドキュメントでは、Baketaプロジェクトの改善されたアーキテクチャに基づく名前空間構造および移行方針について詳述します。

> **重要**: BaketaプロジェクトはWindows専用アプリケーションとして開発を継続し、Linux/macOSへのクロスプラットフォーム対応は行いません。

## 1. 改善されたアーキテクチャの概要

Baketaプロジェクトを以下の4つの主要レイヤーに分割します：

```
Baketa/
├── Baketa.Core/               # プラットフォーム非依存のコア機能
├── Baketa.Infrastructure/     # インフラストラクチャ層
├── Baketa.Application/        # アプリケーションサービス
└── Baketa.UI/                 # UI層
```

## 2. 詳細な名前空間構造

### 2.1 Baketa.Core (コア層)

プラットフォーム非依存の基本機能と抽象化を提供します：

```
Baketa.Core/
├── Baketa.Core.Abstractions/  # 基本抽象化（旧Interfaces）
│   ├── Imaging/            # 画像処理抽象化
│   ├── Capture/            # キャプチャ抽象化
│   ├── Translation/        # 翻訳抽象化
│   └── Common/             # 共通抽象化
├── Baketa.Core.Models/        # データモデル
├── Baketa.Core.Services/      # コアサービス実装
│   ├── Imaging/            # 画像処理サービス
│   ├── Capture/            # キャプチャサービス
│   └── Translation/        # 翻訳サービス
├── Baketa.Core.Events/        # イベント定義と集約機構
│   ├── Abstractions/       # イベント抽象化
│   ├── Implementation/     # イベント実装
│   └── EventTypes/         # イベント型定義
└── Baketa.Core.Common/        # 共通ユーティリティ
```

### 2.2 Baketa.Infrastructure (インフラストラクチャ層)

Windows固有の実装と外部サービス連携を担当します：

```
Baketa.Infrastructure/
├── Baketa.Infrastructure.Abstractions/  # インフラ抽象化
├── Baketa.Infrastructure.Platform/      # プラットフォーム関連機能
│   └── Windows/                      # Windows固有実装
│       ├── Imaging/                  # Windows画像処理
│       ├── Capture/                  # Windowsキャプチャ
│       └── Adapters/                 # Windows用アダプター
├── Baketa.Infrastructure.OCR/           # OCR機能
│   ├── PaddleOCR/                    # PaddleOCR実装
│   ├── Services/                     # OCRサービス
│   └── Optimization/                 # OpenCVベースのOCR最適化
├── Baketa.Infrastructure.Translation/   # 翻訳機能
│   ├── Engines/                      # 翻訳エンジン
│   ├── ONNX/                         # ONNXモデル実装
│   └── Cloud/                        # クラウドサービス連携
└── Baketa.Infrastructure.Persistence/   # 永続化機能
```

### 2.3 Baketa.Application (アプリケーション層)

ビジネスロジックと機能統合を担当します：

```
Baketa.Application/
├── Baketa.Application.Abstractions/     # アプリケーション抽象化
├── Baketa.Application.Services/         # アプリケーションサービス
│   ├── OCR/                          # OCRアプリケーションサービス
│   ├── Translation/                  # 翻訳アプリケーションサービス
│   ├── Capture/                      # キャプチャアプリケーションサービス
│   └── Integration/                  # 統合サービス
├── Baketa.Application.DI/               # 依存性注入設定
├── Baketa.Application.Handlers/         # イベントハンドラー
└── Baketa.Application.Configuration/    # アプリケーション設定
```

### 2.4 Baketa.UI (UI層)

ユーザーインターフェースとプレゼンテーションロジックを担当します：

```
Baketa.UI/
├── Baketa.UI.Abstractions/    # UI抽象化
├── Baketa.UI.Core/            # UI共通機能
│   ├── Services/           # UI共通サービス
│   ├── Controls/           # 共通コントロール
│   └── ViewModels/         # 共通ビューモデル
└── Baketa.UI.Avalonia/        # Avalonia UI実装
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
| コア層 | `Baketa.Core.[機能分野]` | `Baketa.Core.Imaging`, `Baketa.Core.Capture` |
| インフラ層 | `Baketa.Infrastructure.[機能/プラットフォーム]` | `Baketa.Infrastructure.Platform.Windows` |
| アプリケーション層 | `Baketa.Application.[機能]` | `Baketa.Application.Services.OCR` |
| UI層 | `Baketa.UI.[フレームワーク].[機能]` | `Baketa.UI.Avalonia.ViewModels` |
| 抽象化 | `[レイヤー].Abstractions.[機能]` | `Baketa.Core.Abstractions.Imaging` |

## 4. イベント集約機構のインターフェース定義

イベント集約機構では、以下のインターフェースを使用します：

```csharp
/// <summary>
/// 基本イベントインターフェース
/// </summary>
public interface IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    Guid Id { get; }
    
    /// <summary>
    /// イベント発生時刻
    /// </summary>
    DateTime Timestamp { get; }
    
    /// <summary>
    /// イベント名
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    string Category { get; }
}

/// <summary>
/// イベント処理インターフェース
/// </summary>
/// <typeparam name="TEvent">イベント型</typeparam>
public interface IEventProcessor<in TEvent> where TEvent : IEvent
{
    /// <summary>
    /// イベント処理
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    /// <returns>処理の完了を表すTask</returns>
    Task HandleAsync(TEvent eventData);
}

/// <summary>
/// イベント集約インターフェース
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// イベントの発行
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="eventData">イベント</param>
    /// <returns>イベント発行の完了を表すTask</returns>
    Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent;
    
    /// <summary>
    /// イベントプロセッサの登録
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="processor">イベントプロセッサ</param>
    void Subscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
    
    /// <summary>
    /// イベントプロセッサの登録解除
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="processor">イベントプロセッサ</param>
    void Unsubscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
}
```

## 5. 名前空間移行指針

### 5.1 移行ステップ

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

### 5.2 移行時のベストプラクティス

- 移行前にすべての依存関係を確認する
- 一度に関連するクラス群をまとめて移行する
- 移行後は単体テストを実施し機能が保たれていることを確認する
- 名前空間エイリアスを使用して移行期間中の互換性を維持する

## 6. 移行の検証

移行完了後、以下の検証を行います：

1. すべてのプロジェクトがエラーなくビルドできること
2. すべての単体テストが成功すること
3. 主要な機能が正常に動作すること
4. パフォーマンス低下がないこと

**移行状況 (2025年5月3日時点)**:
- すべての古いインターフェースが非推奨化され、プロジェクト全体から削除されました
- すべてのコードが新しい名前空間構造を使用しています
- すべての依存性注入設定が更新されました
- イベント集約機構の実装が完了しました（Issue #24～#27）
- インターフェース移行は完了しました（Issue #1～#6）

## 7. 移行スケジュール

| フェーズ | 対象モジュール | 完了予定 | 実際の完了日 |
|--------|--------------|--------|--------|
| 1      | 新インターフェース定義 | 2025年5月中旬 | 2025年4月10日 |
| 2      | アダプターパターン実装 | 2025年5月下旬 | 2025年4月12日 |
| 3      | コアモジュール移行 | 2025年6月上旬 | 2025年4月15日 |
| 4      | Windows依存コード整理 | 2025年6月下旬 | 2025年4月17日 |
| 5      | アプリケーション層とUI層 | 2025年7月中旬 | 2025年4月20日 |
| 6      | イベント集約機構実装 | 2025年7月下旬 | 2025年5月3日 |

移行は予定よりも早く完了し、すべてのインターフェースが新しい名前空間構造に移行され、古いインターフェースはプロジェクトから削除されました。イベント集約機構の実装も完了し、アプリケーション全体でのイベントベースの通信が可能になりました。

移行に関する詳細な質問や課題は開発チームに相談してください。