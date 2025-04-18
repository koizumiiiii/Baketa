# Baketaプロジェクト - 改善されたアーキテクチャ設計

## 1. 概要

本ドキュメントは、Baketaプロジェクトの名前空間衝突や構造上の問題を解決するための改善されたアーキテクチャ設計について説明します。

**重要事項**: BaketaプロジェクトはWindows専用アプリケーションとして開発を継続し、Linux/macOSへのクロスプラットフォーム対応は行いません。また、OCR最適化にはOpenCVのみを使用します。

## 2. 現状の問題点

現在のBaketaプロジェクトには以下の問題が存在します：

1. **インターフェース定義の重複**：
   - 同名のインターフェース（例：`IScreenCaptureService`、`IGTTImage`）が複数の名前空間に存在
   - 不明確な責任分担によるコードの重複

2. **アーキテクチャの課題**：
   - 名前空間の境界と責任分担が不明確
   - 依存関係が複雑で追跡困難
   - プラットフォーム依存コードと非依存コードの分離が不十分
   - モジュール間の結合度が高く、テスト容易性が低い

3. **依存性注入の問題**：
   - サービス登録コードの散在と重複
   - 一貫性のないサービスライフタイム管理

## 3. 改善されたアーキテクチャ構成

### 3.1 全体構造

Baketaプロジェクトを以下の4つの主要レイヤーに分割します：

```
Baketa/
├── src/
│   ├── Baketa.Core/                 # プラットフォーム非依存のコア機能
│   ├── Baketa.Infrastructure/       # インフラストラクチャ層
│   ├── Baketa.Application/          # アプリケーションサービス
│   └── Baketa.UI/                   # UI層
│
├── tests/                        # テスト
└── tools/                        # ツール、スクリプト等
```

### 3.2 レイヤー構成の詳細

#### 3.2.1 Baketa.Core (コア層)

プラットフォーム非依存の基本機能と抽象化を提供します：

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

#### 3.2.2 Baketa.Infrastructure (インフラストラクチャ層)

Windows固有の実装と外部サービス連携を担当します：

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
│   ├── PaddleOCR/        # PaddleOCR実装
│   ├── Services/         # OCRサービス
│   └── Optimization/     # OpenCVベースのOCR最適化
├── Translation/          # 翻訳機能
│   ├── Engines/          # 翻訳エンジン
│   ├── ONNX/             # ONNXモデル実装
│   └── Cloud/            # クラウドサービス連携
└── Persistence/          # 永続化機能
    ├── Settings/         # 設定保存
    └── Cache/            # キャッシュ機能
```

#### 3.2.3 Baketa.Application (アプリケーション層)

ビジネスロジックと機能統合を担当します：

```
Baketa.Application/
├── Abstractions/         # アプリケーション抽象化
├── Services/             # アプリケーションサービス
│   ├── OCR/              # OCRアプリケーションサービス
│   ├── Translation/      # 翻訳アプリケーションサービス
│   ├── Capture/          # キャプチャアプリケーションサービス
│   └── Integration/      # 統合サービス
├── DI/                   # 依存性注入設定
│   ├── Modules/          # DIモジュール
│   └── Extensions/       # DI拡張メソッド
├── Handlers/             # イベントハンドラー
└── Configuration/        # アプリケーション設定
```

#### 3.2.4 Baketa.UI (UI層)

ユーザーインターフェースとプレゼンテーションロジックを担当します：

```
Baketa.UI/
├── Baketa.UI.Abstractions/  # UI抽象化
├── Baketa.UI.Core/          # クロスプラットフォームUI機能
│   ├── Services/         # UI共通サービス
│   ├── Controls/         # 共通コントロール
│   └── ViewModels/       # 共通ビューモデル
├── Baketa.UI.Avalonia/      # Avalonia UI実装
│   ├── Views/            # XAML Views
│   │   ├── Main/         # メイン画面
│   │   ├── Settings/     # 設定画面
│   │   └── Overlay/      # オーバーレイ
│   ├── ViewModels/       # ViewModels
│   ├── Controls/         # カスタムコントロール
│   ├── Behaviors/        # UI振る舞い
│   ├── Converters/       # 値コンバーター
│   └── Services/         # Avalonia固有サービス
└── Baketa.UI.Web/           # （将来的な）Web UI実装用
```

### 3.3 テスト構造

```
tests/
├── Baketa.Core.Tests/
│   ├── Imaging/          # 画像処理テスト
│   ├── Events/           # イベントテスト
│   └── Services/         # サービステスト
├── Baketa.Infrastructure.Tests/
│   ├── Platform/         # プラットフォームテスト
│   ├── OCR/              # OCRテスト
│   └── Translation/      # 翻訳テスト
├── Baketa.Application.Tests/
│   ├── Services/         # アプリケーションサービステスト
│   └── Handlers/         # ハンドラーテスト
└── Baketa.UI.Tests/
    ├── ViewModels/       # ビューモデルテスト
    └── Services/         # UIサービステスト
```

## 4. 命名規則とコーディング基準

### 4.1 インターフェース命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本インターフェース | `I[機能名]` | `IImage`, `ICapture` |
| サービスインターフェース | `I[機能名]Service` | `ICaptureService`, `ITranslationService` |
| ファクトリインターフェース | `I[成果物]Factory` | `IImageFactory`, `ICaptureFactory` |
| Windows固有 | `IWindows[機能名]` | `IWindowsImage`, `IWindowsCapture` |

### 4.2 実装クラス命名規則

| カテゴリ | 命名パターン | 例 |
|---------|--------------|-----|
| 基本実装 | `[機能名]` | `Image`, `Capture` |
| サービス実装 | `[機能名]Service` | `CaptureService`, `TranslationService` |
| アダプター | `[機能名]Adapter` | `LegacyImageAdapter`, `WindowsImageAdapter` |
| Windows実装 | `Windows[機能名]` | `WindowsImage`, `WindowsCapture` |

### 4.3 名前空間命名規則

| レイヤー | 命名パターン | 例 |
|---------|--------------|-----|
| コア層 | `Baketa.Core.[機能分野]` | `Baketa.Core.Imaging`, `Baketa.Core.Capture` |
| インフラ層 | `Baketa.Infrastructure.[機能/プラットフォーム]` | `Baketa.Infrastructure.Platform.Windows` |
| アプリケーション層 | `Baketa.Application.[機能]` | `Baketa.Application.Services.OCR` |
| UI層 | `Baketa.UI.[フレームワーク].[機能]` | `Baketa.UI.Avalonia.ViewModels` |
| 抽象化 | `[レイヤー].Abstractions.[機能]` | `Baketa.Core.Abstractions.Imaging` |

## 5. インターフェース設計の例

### 5.1 画像処理インターフェース階層

```csharp
namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 基本的な画像プロパティを提供する基底インターフェース
    /// </summary>
    public interface IImageBase : IDisposable
    {
        /// <summary>
        /// 画像の幅（ピクセル単位）
        /// </summary>
        int Width { get; }

        /// <summary>
        /// 画像の高さ（ピクセル単位）
        /// </summary>
        int Height { get; }
        
        /// <summary>
        /// 画像をバイト配列に変換します。
        /// </summary>
        /// <returns>画像データを表すバイト配列</returns>
        Task<byte[]> ToByteArrayAsync();
    }

    /// <summary>
    /// 標準的な画像操作機能を提供するインターフェース
    /// </summary>
    public interface IImage : IImageBase
    {
        /// <summary>
        /// 画像のクローンを作成します。
        /// </summary>
        /// <returns>元の画像と同じ内容を持つ新しい画像インスタンス</returns>
        IImage Clone();
        
        /// <summary>
        /// 画像のサイズを変更します。
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        Task<IImage> ResizeAsync(int width, int height);
    }

    /// <summary>
    /// 高度な画像処理機能を提供するインターフェース
    /// </summary>
    public interface IAdvancedImage : IImage
    {
        /// <summary>
        /// 画像にフィルターを適用します。
        /// </summary>
        /// <param name="filter">適用するフィルター</param>
        /// <returns>フィルターが適用された新しい画像インスタンス</returns>
        Task<IImage> ApplyFilterAsync(IImageFilter filter);
        
        /// <summary>
        /// 2つの画像の類似度を計算します。
        /// </summary>
        /// <param name="other">比較対象の画像</param>
        /// <returns>0.0〜1.0の類似度（1.0が完全一致）</returns>
        Task<float> CalculateSimilarityAsync(IImage other);
    }
}
```

### 5.2 Windows画像インターフェース

```csharp
namespace Baketa.Infrastructure.Platform.Abstractions
{
    /// <summary>
    /// Windows画像機能を定義するインターフェース
    /// </summary>
    public interface IWindowsImage : IDisposable
    {
        /// <summary>
        /// 画像の幅（ピクセル単位）
        /// </summary>
        int Width { get; }

        /// <summary>
        /// 画像の高さ（ピクセル単位）
        /// </summary>
        int Height { get; }

        /// <summary>
        /// Windows固有の画像オブジェクトを取得
        /// </summary>
        /// <returns>Windows Bitmap</returns>
        Bitmap GetNativeImage();

        /// <summary>
        /// Windowsの形式でファイルに保存
        /// </summary>
        /// <param name="path">保存先のパス</param>
        Task SaveAsync(string path);
    }

    /// <summary>
    /// Windows画像作成ファクトリ
    /// </summary>
    public interface IWindowsImageFactory
    {
        /// <summary>
        /// ファイルから画像を作成
        /// </summary>
        /// <param name="path">ファイルパス</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CreateFromFileAsync(string path);

        /// <summary>
        /// バイト配列から画像を作成
        /// </summary>
        /// <param name="data">画像データ</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CreateFromBytesAsync(byte[] data);
    }
}
```

### 5.3 Windows画像実装

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Imaging
{
    /// <summary>
    /// Windows固有の画像実装
    /// </summary>
    public class WindowsImage : IWindowsImage
    {
        private readonly Bitmap _bitmap;
        private bool _disposed;

        public WindowsImage(Bitmap bitmap)
        {
            _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        }

        public int Width => _bitmap.Width;
        public int Height => _bitmap.Height;

        public Bitmap GetNativeImage() => _bitmap;

        public Task SaveAsync(string path)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WindowsImage));
                
            _bitmap.Save(path);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _bitmap.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Windows画像ファクトリ
    /// </summary>
    public class WindowsImageFactory : IWindowsImageFactory
    {
        public Task<IWindowsImage> CreateFromFileAsync(string path)
        {
            var bitmap = new Bitmap(path);
            return Task.FromResult<IWindowsImage>(new WindowsImage(bitmap));
        }

        public Task<IWindowsImage> CreateFromBytesAsync(byte[] data)
        {
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            return Task.FromResult<IWindowsImage>(new WindowsImage(bitmap));
        }
    }
}
```

### 5.4 アダプターの実装例

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Adapters
{
    /// <summary>
    /// WindowsImageをIImageインターフェースに適応させるアダプター
    /// </summary>
    public class WindowsImageAdapter : IImage
    {
        private readonly IWindowsImage _windowsImage;
        private bool _disposed;

        public WindowsImageAdapter(IWindowsImage windowsImage)
        {
            _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
        }

        public int Width => _windowsImage.Width;
        public int Height => _windowsImage.Height;

        public IImage Clone()
        {
            // Windows固有の実装を使用して画像をクローン
            var nativeImage = _windowsImage.GetNativeImage();
            var clonedNative = new Bitmap(nativeImage);
            var clonedWindows = new WindowsImage(clonedNative);
            
            return new WindowsImageAdapter(clonedWindows);
        }

        public async Task<byte[]> ToByteArrayAsync()
        {
            using var stream = new MemoryStream();
            var nativeImage = _windowsImage.GetNativeImage();
            nativeImage.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        public async Task<IImage> ResizeAsync(int width, int height)
        {
            var nativeImage = _windowsImage.GetNativeImage();
            var resized = new Bitmap(nativeImage, new Size(width, height));
            var resizedWindows = new WindowsImage(resized);
            
            return new WindowsImageAdapter(resizedWindows);
        }

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

## 6. サービス層設計

### 6.1 キャプチャサービス抽象化

```csharp
namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// キャプチャサービスの抽象化
    /// </summary>
    public interface ICaptureService
    {
        /// <summary>
        /// 画面全体をキャプチャ
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureScreenAsync();

        /// <summary>
        /// 指定した領域をキャプチャ
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureRegionAsync(Rectangle region);
    }
}

namespace Baketa.Infrastructure.Platform.Abstractions
{
    /// <summary>
    /// Windows固有のキャプチャ機能
    /// </summary>
    public interface IWindowsCaptureService
    {
        /// <summary>
        /// 画面全体をキャプチャ
        /// </summary>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CaptureScreenAsync();

        /// <summary>
        /// 指定した領域をキャプチャ
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>Windows画像</returns>
        Task<IWindowsImage> CaptureRegionAsync(Rectangle region);
    }
}
```

### 6.2 アプリケーションサービス実装

```csharp
namespace Baketa.Application.Services.Capture
{
    /// <summary>
    /// キャプチャサービスの実装
    /// </summary>
    public class CaptureService : ICaptureService
    {
        private readonly IWindowsCaptureService _windowsCaptureService;
        private readonly IImageFactory _imageFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger<CaptureService> _logger;

        public CaptureService(
            IWindowsCaptureService windowsCaptureService,
            IImageFactory imageFactory,
            IEventAggregator eventAggregator,
            ILogger<CaptureService> logger)
        {
            _windowsCaptureService = windowsCaptureService;
            _imageFactory = imageFactory;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public async Task<IImage> CaptureScreenAsync()
        {
            try
            {
                _logger.LogInformation("画面全体のキャプチャを開始");
                
                // Windows固有の実装を使用
                var windowsImage = await _windowsCaptureService.CaptureScreenAsync();
                
                // コアインターフェースに変換
                var image = _imageFactory.CreateFromWindowsImage(windowsImage);
                
                // キャプチャ完了イベントを発行
                await _eventAggregator.PublishAsync(new CaptureCompletedEvent(image));
                
                _logger.LogInformation("画面キャプチャが完了: {Width}x{Height}", image.Width, image.Height);
                
                return image;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画面キャプチャ中にエラーが発生");
                throw new CaptureException("画面キャプチャに失敗しました", ex);
            }
        }

        public async Task<IImage> CaptureRegionAsync(Rectangle region)
        {
            try
            {
                _logger.LogInformation("領域キャプチャを開始: {Region}", region);
                
                // Windows固有の実装を使用
                var windowsImage = await _windowsCaptureService.CaptureRegionAsync(region);
                
                // コアインターフェースに変換
                var image = _imageFactory.CreateFromWindowsImage(windowsImage);
                
                // キャプチャ完了イベントを発行
                await _eventAggregator.PublishAsync(new CaptureCompletedEvent(image));
                
                _logger.LogInformation("領域キャプチャが完了: {Width}x{Height}", image.Width, image.Height);
                
                return image;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "領域キャプチャ中にエラーが発生: {Region}", region);
                throw new CaptureException($"領域キャプチャに失敗しました: {region}", ex);
            }
        }
    }
}
```

## 7. イベント集約機構の設計

イベント集約機構により、モジュール間の疎結合なコミュニケーションを実現します：

```csharp
namespace Baketa.Core.Abstractions.Events
{
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
    }

    /// <summary>
    /// イベントハンドラインターフェース
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// イベント処理
        /// </summary>
        /// <param name="event">イベント</param>
        Task HandleAsync(TEvent @event);
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
        /// <param name="event">イベント</param>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
        
        /// <summary>
        /// イベントハンドラの登録
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
        
        /// <summary>
        /// イベントハンドラの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    }
}
```

## 8. 依存性注入の最適化

### 8.1 モジュールベースのDI

```csharp
namespace Baketa.Application.DI
{
    /// <summary>
    /// サービス登録モジュールインターフェース
    /// </summary>
    public interface IServiceModule
    {
        /// <summary>
        /// サービスの登録
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        void RegisterServices(IServiceCollection services);
    }

    /// <summary>
    /// コアサービスモジュール
    /// </summary>
    public class CoreModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // コアサービスの登録
            services.AddSingleton<IEventAggregator, EventAggregator>();
            services.AddSingleton<IImageFactory, ImageFactory>();
            // ...その他のコアサービス
        }
    }

    /// <summary>
    /// インフラストラクチャサービスモジュール
    /// </summary>
    public class InfrastructureModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // 基本インフラサービスの登録
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ITranslationCache, TranslationCache>();
            // ...その他のインフラサービス
            
            // Windows固有サービスの登録
            services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            services.AddSingleton<IWindowsCaptureService, WindowsCaptureService>();
        }
    }

    /// <summary>
    /// アプリケーションサービスモジュール
    /// </summary>
    public class ApplicationModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // アプリケーションサービスの登録
            services.AddSingleton<ICaptureService, CaptureService>();
            services.AddSingleton<ITranslationService, TranslationService>();
            services.AddSingleton<IOcrService, OcrService>();
            // ...その他のアプリケーションサービス
        }
    }

    /// <summary>
    /// UIサービスモジュール
    /// </summary>
    public class UIModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // ViewModelの登録
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<OverlayViewModel>();
            
            // UIサービスの登録
            services.AddSingleton<IDialogService, AvaloniaDialogService>();
            services.AddSingleton<INotificationService, AvaloniaNotificationService>();
            // ...その他のUIサービス
        }
    }
}
```

### 8.2 統合DIコンテナの設定

```csharp
namespace Baketa.Application.DI
{
    /// <summary>
    /// 依存性注入拡張メソッド
    /// </summary>
    public static class DependencyInjectionExtensions
    {
        /// <summary>
        /// Baketaサービスの登録
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaServices(this IServiceCollection services)
        {
            // すべてのモジュールを登録
            var modules = new IServiceModule[]
            {
                new CoreModule(),
                new InfrastructureModule(),
                new ApplicationModule(),
                new UIModule()
            };
            
            // 各モジュールのサービスを登録
            foreach (var module in modules)
            {
                module.RegisterServices(services);
            }
            
            return services;
        }
    }
}
```

## 9. 移行戦略

現行システムから新アーキテクチャへの移行は段階的に行います：

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

## 10. 名前空間の衝突が起きにくいようにする提案

### 10.1 階層的な名前空間構造の徹底

```csharp
Baketa.Core.Abstractions.Imaging
Baketa.Core.Abstractions.Capture
Baketa.Infrastructure.Platform.Windows
```

のように、機能ごとに明確に区分けされた階層構造を作り、一貫性を保ちます。

### 10.2 インターフェースと実装の命名規則の厳格化

- インターフェース: `IImageProcessor`
- 実装クラス: `WindowsImageProcessor`
- アダプター: `WindowsImageProcessorAdapter`

明確な接頭辞/接尾辞のパターンを使用することで、同名の衝突を防ぎます。

### 10.3 モジュール単位のアセンブリ分割

各機能を独立したアセンブリ（DLL）に分割します：
- `Baketa.Core.Abstractions.dll`
- `Baketa.Core.Services.dll`
- `Baketa.Infrastructure.Platform.Windows.dll`

これにより、参照関係が明確になり、循環参照も防止できます。

### 10.4 アダプターレイヤーの集中管理

異なる実装間の変換を行うアダプターは、専用の名前空間に集約します：
```csharp
Baketa.Infrastructure.Adapters
```

### 10.5 グローバル名前空間の明示的な使用

```csharp
using CoreImage = global::Baketa.Core.Abstractions.Imaging.IImage;
using WindowsImage = global::Baketa.Infrastructure.Platform.Windows.Imaging.IWindowsImage;
```

衝突が予想される型には、エイリアスを使用して明示的に区別します。

### 10.6 非推奨インターフェースへの段階的移行

既存の衝突するインターフェースには[Obsolete]属性を付け、新しいインターフェースへの移行パスを提供します：

```csharp
[Obsolete("代わりに IImage を使用してください。")]
public interface IGTTImage { }
```

### 10.7 アセンブリ強い名前の使用

プロジェクトに強い名前（署名）を付けることで、アセンブリレベルでの衝突を防ぎます。

### 10.8 依存性注入コンテナのモジュール分割

```csharp
public static IServiceCollection AddBaketaCoreServices(this IServiceCollection services)
public static IServiceCollection AddBaketaInfrastructureServices(this IServiceCollection services)
```

サービス登録も機能ごとに分離することで、依存関係の管理が容易になります。

### 10.9 バージョン管理の導入

```csharp
namespace Baketa.Core.Abstractions.V1.Imaging
namespace Baketa.Core.Abstractions.V2.Imaging
```

主要なAPIの変更がある場合、バージョンを名前空間に含めることで旧バージョンとの互換性を保持できます。

### 10.10 ドメイン駆動設計の境界コンテキスト導入

機能を論理的な「境界コンテキスト」で区切り、各コンテキスト内での命名は独立させます：

```csharp
Baketa.OCR.Domain
Baketa.Translation.Domain
```

## 11. まとめ

本ドキュメントで提案した改善されたアーキテクチャは、以下の利点をもたらします：

1. **明確な責任分担**: 各レイヤーとモジュールの役割が明確に定義されている
2. **名前空間の一貫性**: 名前空間の衝突がなく、責任範囲が明確
3. **Windows依存部分の整理**: Windows依存コードが適切に分離され、管理が容易になっている
4. **拡張性**: インターフェースを通じた機能拡張が容易
5. **テスト容易性**: 依存関係が明確で、各コンポーネントの単体テストが容易

この改善されたアーキテクチャにより、Baketaプロジェクトのメンテナンス性と拡張性が大幅に向上します。なお、本プロジェクトはWindows専用アプリケーションとして開発を継続し、OCR最適化にはOpenCVのみを使用します。