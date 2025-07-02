# Baketa名前空間移行計画

**作成日**: 2025年4月20日  
**作成者**: BaketaプロジェクトチームID  
**関連Issue**: #2 改善: 名前空間構造設計と移行計画  
**ステータス**: ドラフト

## 1. 現状分析

### 1.1 現在の名前空間構造

BaketaプロジェクトはWindowsのオーバーレイアプリケーションとして設計されており、以下の主要レイヤーに分かれています：

- **Baketa.Core**: プラットフォーム非依存のコア機能
- **Baketa.Infrastructure**: インフラストラクチャ層
- **Baketa.Infrastructure.Platform**: プラットフォーム依存の実装
- **Baketa.Application**: ビジネスロジックと機能統合
- **Baketa.UI**: ユーザーインターフェース層

現在のプロジェクトでは、インターフェースの定義が主に以下の名前空間で行われています：

```
Baketa.Core.Interfaces.*
```

### 1.2 問題点

1. インターフェース定義の重複
2. 名前空間の境界と責任分担の不明確さ
3. 依存関係の複雑さと追跡困難性
4. プラットフォーム依存コードと非依存コードの不明確な分離
5. 一貫性のないサービス登録

## 2. 新しい名前空間構造

「improved-architecture.md」に基づき、以下の名前空間構造に移行します：

### 2.1 Baketa.Core

```
Baketa.Core/
├── Abstractions/         # 基本抽象化（旧Interfaces）
│   ├── Imaging/          # 画像処理抽象化
│   ├── Capture/          # キャプチャ抽象化
│   ├── Translation/      # 翻訳抽象化
│   └── Common/           # 共通抽象化
├── Models/               # データモデル
├── Services/             # コアサービス実装
│   ├── Imaging/          # 画像処理サービス
│   ├── Capture/          # キャプチャサービス
│   └── Translation/      # 翻訳サービス
├── Events/               # イベント定義と集約機構
│   ├── Abstractions/     # イベント抽象化
│   ├── Implementation/   # イベント実装
│   └── EventTypes/       # イベント型定義
└── Common/               # 共通ユーティリティ
```

### 2.2 Baketa.Infrastructure

```
Baketa.Infrastructure/
├── Abstractions/         # インフラ抽象化
├── Platform/             # プラットフォーム関連機能
│   ├── Abstractions/     # プラットフォーム抽象化インターフェース
│   ├── Common/           # 共通機能
│   └── Windows/          # Windows固有実装
│       ├── Imaging/      # Windows画像処理
│       ├── Capture/      # Windowsキャプチャ
│       └── Adapters/     # Windows用アダプター
├── OCR/                  # OCR機能
├── Translation/          # 翻訳機能
└── Persistence/          # 永続化機能
```

### 2.3 Baketa.Application および Baketa.UI

「improved-architecture.md」の定義に従います。

## 3. インターフェース移行マップ

### 3.1 画像処理インターフェース

| 現在の場所 | 新しい場所 | 優先度 |
|-----------|------------|-------|
| `Baketa.Core.Interfaces.Image.IImage` | `Baketa.Core.Abstractions.Imaging.IImage` | 高 |
| - | `Baketa.Core.Abstractions.Imaging.IImageBase` | 高 |
| - | `Baketa.Core.Abstractions.Imaging.IAdvancedImage` | 中 |
| - | `Baketa.Infrastructure.Platform.Abstractions.IWindowsImage` | 高 |
| - | `Baketa.Infrastructure.Platform.Abstractions.IWindowsImageFactory` | 高 |

### 3.2 プラットフォームインターフェース

| 現在の場所 | 新しい場所 | 優先度 |
|-----------|------------|-------|
| `Baketa.Core.Interfaces.Platform.IWindowManager` | `Baketa.Infrastructure.Platform.Abstractions.IWindowManager` | 高 |
| `Baketa.Core.Interfaces.Platform.IScreenCapturer` | `Baketa.Infrastructure.Platform.Abstractions.IScreenCapturer` | 高 |
| - | `Baketa.Core.Abstractions.Capture.ICaptureService` | 高 |

### 3.3 その他のコアインターフェース

実態のプロジェクトコードを調査して追加予定

## 4. 移行フェーズと優先順位

### 4.1 フェーズ1: 基本インターフェースの移行 (スプリント1)

1. `IImageBase` (新規作成)
2. `IImage` (移行)
3. `IAdvancedImage` (新規作成)
4. `IWindowsImage` (新規作成)
5. イベント集約関連インターフェース

### 4.2 フェーズ2: プラットフォームインターフェースの移行 (スプリント1-2)

1. `IWindowManager` (移行)
2. `IScreenCapturer` (移行)
3. `ICaptureService` (新規作成)
4. Windows固有のインターフェース

### 4.3 フェーズ3: サービスインターフェースの移行 (スプリント2)

1. OCRサービス関連
2. 翻訳サービス関連
3. その他のサービスインターフェース

### 4.4 フェーズ4: アダプターとファクトリーの実装 (スプリント2-3)

1. 各種アダプターの実装
2. ファクトリーの実装
3. 依存性注入の設定

### 4.5 フェーズ5: 古い参照の更新と非推奨化 (スプリント3)

1. 旧インターフェースの非推奨化
2. すべての参照を新しいインターフェースに更新

## 5. インターフェース設計ガイドライン

### 5.1 命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本インターフェース | `I[機能名]` | `IImage`, `ICapture` |
| サービスインターフェース | `I[機能名]Service` | `ICaptureService`, `ITranslationService` |
| ファクトリインターフェース | `I[成果物]Factory` | `IImageFactory`, `ICaptureFactory` |
| Windows固有 | `IWindows[機能名]` | `IWindowsImage`, `IWindowsCapture` |

### 5.2 インターフェース階層設計の原則

1. **単一責任の原則**: 各インターフェースは明確に定義された責任を持つ
2. **インターフェース分離の原則**: クライアントに不要なメソッドを強制しない
3. **階層構造の原則**: 特殊化されたインターフェースは基本インターフェースを継承

### 5.3 インターフェース詳細

#### 5.3.1 基本画像インターフェース階層

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    public interface IImageBase : IDisposable
    {
        int Width { get; }
        int Height { get; }
        Task<byte[]> ToByteArrayAsync();
    }

    public interface IImage : IImageBase
    {
        IImage Clone();
        Task<IImage> ResizeAsync(int width, int height);
    }

    public interface IAdvancedImage : IImage
    {
        Task<IImage> ApplyFilterAsync(IImageFilter filter);
        Task<float> CalculateSimilarityAsync(IImage other);
    }
}
```

#### 5.3.2 Windows画像インターフェース

```csharp
namespace Baketa.Infrastructure.Platform.Abstractions
{
    public interface IWindowsImage : IDisposable
    {
        int Width { get; }
        int Height { get; }
        Bitmap GetNativeImage();
        Task SaveAsync(string path);
    }

    public interface IWindowsImageFactory
    {
        Task<IWindowsImage> CreateFromFileAsync(string path);
        Task<IWindowsImage> CreateFromBytesAsync(byte[] data);
    }
}
```

#### 5.3.3 画像ファクトリー

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    public interface IImageFactory
    {
        Task<IImage> CreateFromFileAsync(string path);
        Task<IImage> CreateFromBytesAsync(byte[] data);
        IImage CreateFromWindowsImage(IWindowsImage windowsImage);
    }
}
```

## 6. 移行テンプレートとブリッジパターン

### 6.1 アダプタークラス

Windows画像をコアインターフェースに適応させるアダプターの例:

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Adapters
{
    public class WindowsImageAdapter : IImage
    {
        private readonly IWindowsImage _windowsImage;
        private bool _disposed;

        public WindowsImageAdapter(IWindowsImage windowsImage)
        {
            _windowsImage = windowsImage;
        }

        public int Width => _windowsImage.Width;
        public int Height => _windowsImage.Height;

        public IImage Clone()
        {
            // Windows実装を使用してクローン
            var nativeImage = _windowsImage.GetNativeImage();
            var clonedNative = new Bitmap(nativeImage);
            var clonedWindows = new WindowsImage(clonedNative);
            
            return new WindowsImageAdapter(clonedWindows);
        }

        // 他のIImageメソッドの実装...

        public void Dispose()
        {
            if (!_disposed)
            {
                _windowsImage.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

### 6.2 非推奨属性の使用

旧インターフェースの非推奨化:

```csharp
namespace Baketa.Core.Interfaces.Image
{
    [Obsolete("このインターフェースは非推奨です。代わりに Baketa.Core.Abstractions.Imaging.IImage を使用してください。")]
    public interface IImage : IDisposable
    {
        // メンバー定義
    }
}
```

## 7. 移行の検証方法

### 7.1 移行チェックリスト

- [ ] すべてのインターフェースが新しい名前空間構造に移行されている
- [ ] 旧インターフェースが非推奨としてマークされている
- [ ] 新しいインターフェース階層が設計原則に従っている
- [ ] アダプターが適切に実装されている
- [ ] 単体テストがすべて通過する
- [ ] コンパイラ警告がない（非推奨の使用を除く）

### 7.2 自動テスト

1. インターフェース整合性テスト
2. アダプターテスト
3. 参照検証テスト

### 7.3 コンパイラ警告の活用

非推奨インターフェースの使用に関する警告を活用して、移行の完全性を確認します。

## 8. タイムラインと担当者

| フェーズ | タスク | 予定期間 | 担当者 |
|---------|-------|----------|-------|
| 1 | 基本インターフェースの移行 | 1週間 | TBD |
| 2 | プラットフォームインターフェースの移行 | 1週間 | TBD |
| 3 | サービスインターフェースの移行 | 1週間 | TBD |
| 4 | アダプターとファクトリーの実装 | 1週間 | TBD |
| 5 | 古い参照の更新と非推奨化 | 1週間 | TBD |

## 9. リスクと対策

| リスク | 影響度 | 対策 |
|-------|--------|-----|
| 互換性の問題 | 高 | アダプターパターンを使用して既存コードとの互換性を維持 |
| 移行期間中の開発の混乱 | 中 | 明確なガイドラインとブランチ戦略の提供 |
| 未発見のインターフェース | 中 | 自動化されたコード分析ツールを使用して依存関係を特定 |
| パフォーマンスへの影響 | 低 | アダプターオーバーヘッドの最小化と必要に応じた最適化 |

## 10. 将来の展望

この名前空間構造の改善により、以下の利点が期待されます：

1. より明確な責任分担
2. 名前空間の一貫性
3. Windows依存部分の整理
4. 拡張性の向上
5. テスト容易性の向上

## 11. 付録

### 11.1 参照ドキュメント

- [改善されたアーキテクチャ設計](E:\dev\Baketa\docs\3-architecture\improved-architecture.md)
- [名前空間移行ガイドライン](E:\dev\Baketa\docs\2-development\guidelines\namespace-migration.md)
- [C#コーディング標準](E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md)

### 11.2 用語集

| 用語 | 説明 |
|------|------|
| インターフェース | 実装を持たないメソッドとプロパティの集合 |
| アダプター | 互換性のないインターフェース間の変換を行うデザインパターン |
| 名前空間 | 関連するクラスとインターフェースをグループ化する仕組み |
| 非推奨化 | 将来的に削除される予定の機能を示すマーキング |
