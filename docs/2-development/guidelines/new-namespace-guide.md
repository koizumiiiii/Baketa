# 新名前空間構造 開発者ガイド

*作成: 2025年4月20日*

## 1. 概要

Baketaプロジェクトでは、名前空間の明確化と責任分担の適正化を目的として、インターフェース構造を新たに整理しました。このドキュメントは、新しい名前空間構造の概要と、開発時に守るべきガイドラインを提供します。

## 2. 新名前空間構造

### 2.1 基本構造

新しい名前空間構造は以下の主要レイヤーで構成されています：

```
Baketa/
├── Baketa.Core/
│   ├── Abstractions/     # 基本抽象化レイヤー（旧Interfaces）
│   ├── Models/           # データモデル
│   ├── Services/         # コアサービス
│   └── Common/           # 共通ユーティリティ
├── Baketa.Infrastructure/
│   ├── Platform/         # プラットフォーム関連機能
│   ├── OCR/              # OCR機能
│   └── Translation/      # 翻訳機能
├── Baketa.Application/
│   ├── Services/         # アプリケーションサービス
│   └── DI/               # 依存性注入設定
└── Baketa.UI/
    ├── Core/             # UI共通機能
    └── Avalonia/         # Avalonia UI実装
```

### 2.2 名前空間命名規則

| レイヤー | 命名パターン | 例 |
|---------|--------------|-----|
| コア層 | `Baketa.Core.Abstractions.[機能]` | `Baketa.Core.Abstractions.Imaging` |
| インフラ層 | `Baketa.Infrastructure.[機能]` | `Baketa.Infrastructure.Platform.Windows` |
| アプリケーション層 | `Baketa.Application.[機能]` | `Baketa.Application.Services.OCR` |
| UI層 | `Baketa.UI.[フレームワーク].[機能]` | `Baketa.UI.Avalonia.ViewModels` |

## 3. インターフェース階層

### 3.1 画像処理インターフェース

```
IImageBase           // 基本的な画像プロパティ
   ↑
IImage               // 標準的な画像操作機能
   ↑
IAdvancedImage       // 高度な画像処理機能
```

### 3.2 プラットフォーム依存インターフェース

```
Baketa.Core.Abstractions.Imaging.IImage              // プラットフォーム非依存
Baketa.Core.Abstractions.Platform.Windows.IWindowsImage  // Windows固有
```

### 3.3 ファクトリインターフェース

```
Baketa.Core.Abstractions.Factories.IImageFactory         // 基本ファクトリ
Baketa.Core.Abstractions.Factories.IWindowsImageFactory  // Windows固有ファクトリ
```

## 4. 開発者のためのベストプラクティス

### 4.1 インターフェース選択

* **プラットフォーム非依存のコードを書く場合**:
  ```csharp
  // ✅ 推奨
  using Baketa.Core.Abstractions.Imaging;
  void ProcessImage(IImage image) { /* ... */ }
  ```

* **Windows固有の機能を利用する場合**:
  ```csharp
  // ✅ 推奨
  using Baketa.Core.Abstractions.Platform.Windows;
  void ProcessWindowsImage(IWindowsImage image) { /* ... */ }
  ```

* **インターフェース間の変換が必要な場合**:
  ```csharp
  // ✅ 推奨
  using Baketa.Core.Abstractions.Imaging;
  using Baketa.Core.Abstractions.Platform.Windows;
  using Baketa.Core.Abstractions.Factories;
  
  // ファクトリを利用した変換
  IImage ConvertToCore(IWindowsImage windowsImage, IImageFactory factory)
  {
      return factory.CreateFromPlatformImage(windowsImage);
  }
  ```

### 4.2 型エイリアスの活用

同名インターフェースを扱う場合は型エイリアスを使用することを推奨します：

```csharp
// ✅ 推奨
using CoreImage = Baketa.Core.Abstractions.Imaging.IImage;
using WindowsImage = Baketa.Core.Abstractions.Platform.Windows.IWindowsImage;

public class ImageService
{
    public CoreImage ConvertToCore(WindowsImage windowsImage)
    {
        // 変換処理
    }
}
```

### 4.3 アダプターパターンの活用

異なるインターフェース間の変換にはアダプターパターンを使用します：

```csharp
// ✅ 推奨
public class WindowsImageAdapter : IImage
{
    private readonly IWindowsImage _windowsImage;
    
    public WindowsImageAdapter(IWindowsImage windowsImage)
    {
        _windowsImage = windowsImage;
    }
    
    // IImageインターフェースの実装
}
```

### 4.4 依存性注入での登録

```csharp
// ✅ 推奨
services.AddSingleton<Baketa.Core.Abstractions.Factories.IWindowsImageFactory, WindowsImageFactory>();
services.AddSingleton<Baketa.Core.Abstractions.Factories.IImageFactory, WindowsImageAdapterFactory>();
```

## 5. 実装例

### 5.1 プラットフォーム抽象化の例

```csharp
// Core層のキャプチャサービスインターフェース
namespace Baketa.Core.Abstractions.Capture
{
    public interface ICaptureService
    {
        Task<IImage> CaptureScreenAsync();
        Task<IImage> CaptureRegionAsync(Rectangle region);
    }
}

// Windows固有の実装
namespace Baketa.Infrastructure.Platform.Windows.Capture
{
    public class WindowsCaptureService : ICaptureService
    {
        private readonly IWindowsImageFactory _windowsImageFactory;
        private readonly IImageFactory _imageFactory;
        
        public WindowsCaptureService(
            IWindowsImageFactory windowsImageFactory,
            IImageFactory imageFactory)
        {
            _windowsImageFactory = windowsImageFactory;
            _imageFactory = imageFactory;
        }
        
        public async Task<IImage> CaptureScreenAsync()
        {
            // Windows APIを使用した実装
            // ...
            var windowsImage = await _windowsImageFactory.CreateFromScreenCapture();
            return _imageFactory.CreateFromPlatformImage(windowsImage);
        }
        
        // 他のメソッド実装
    }
}
```

### 5.2 新しいサービスの実装例

```csharp
// アプリケーション層サービス
namespace Baketa.Application.Services.OCR
{
    public class OcrService : IOcrService
    {
        private readonly IImageProcessor _imageProcessor;
        private readonly IOcrEngine _ocrEngine;
        private readonly ILogger<OcrService> _logger;
        
        public OcrService(
            IImageProcessor imageProcessor,
            IOcrEngine ocrEngine,
            ILogger<OcrService> logger)
        {
            _imageProcessor = imageProcessor;
            _ocrEngine = ocrEngine;
            _logger = logger;
        }
        
        public async Task<OcrResult> RecognizeTextAsync(IImage image)
        {
            try
            {
                // 前処理
                var processedImage = await _imageProcessor.OptimizeForOcrAsync(image);
                
                // OCR処理
                return await _ocrEngine.RecognizeAsync(processedImage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR処理中にエラーが発生しました");
                throw new OcrException("テキスト認識に失敗しました", ex);
            }
        }
    }
}
```

### 5.3 イベント集約機構の例

```csharp
// イベント定義
namespace Baketa.Core.Events
{
    public class CaptureCompletedEvent : IEvent
    {
        public CaptureCompletedEvent(IImage capturedImage)
        {
            CapturedImage = capturedImage;
            Id = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
        }
        
        public IImage CapturedImage { get; }
        public Guid Id { get; }
        public DateTime Timestamp { get; }
        public string Name => "CaptureCompleted";
        public string Category => "Capture";
    }
}

// イベントプロセッサ
namespace Baketa.Application.Events.Processors
{
    public class CaptureCompletedProcessor : IEventProcessor<CaptureCompletedEvent>
    {
        private readonly IOcrService _ocrService;
        private readonly ILogger<CaptureCompletedProcessor> _logger;
        
        public CaptureCompletedProcessor(
            IOcrService ocrService,
            ILogger<CaptureCompletedProcessor> logger)
        {
            _ocrService = ocrService;
            _logger = logger;
        }
        
        public async Task HandleAsync(CaptureCompletedEvent eventData)
        {
            _logger.LogInformation("キャプチャ完了イベントを処理します");
            
            try
            {
                await _ocrService.ProcessImageAsync(eventData.CapturedImage);
                _logger.LogInformation("キャプチャ画像のOCR処理が完了しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "キャプチャ画像のOCR処理中にエラーが発生しました");
                throw; // 上位レイヤーでのエラーハンドリングを容易にするために再スロー
            }
        }
    }
}

// イベント集約機構の使用例
namespace Baketa.Application.Services.Capture
{
    public class CaptureService : ICaptureService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IWindowsCaptureService _windowsCaptureService;
        
        public CaptureService(
            IEventAggregator eventAggregator,
            IWindowsCaptureService windowsCaptureService)
        {
            _eventAggregator = eventAggregator;
            _windowsCaptureService = windowsCaptureService;
        }
        
        public async Task<IImage> CaptureScreenAsync()
        {
            // スクリーンキャプチャを実行
            var image = await _windowsCaptureService.CaptureScreenAsync();
            
            // イベントを発行
            await _eventAggregator.PublishAsync(new CaptureCompletedEvent(image));
            
            return image;
        }
    }
}
```

## 6. 旧インターフェースからの移行

Issue #1～#6の対応により、旧名前空間のインターフェースは既にすべて新しい名前空間に移行され、プロジェクトから削除されています。もしあなたが開発中のブランチにまだ旧インターフェースへの参照が含まれている場合は、以下の対応が必要です：

1. 以下のような旧参照を更新：
   ```csharp
   // 旧参照
   using Baketa.Core.Interfaces.Image;
   
   // 新参照
   using Baketa.Core.Abstractions.Imaging;
   ```

2. 以下のような旧インターフェースの使用を更新：
   ```csharp
   // 旧実装
   public class MyClass : IImage  // Baketa.Core.Interfaces.Image.IImage
   
   // 新実装
   public class MyClass : IImage  // Baketa.Core.Abstractions.Imaging.IImage
   ```

## 8. 翻訳システムの名前空間構造

翻訳システムにおいて、以下の2つの名前空間が存在します：

```
Baketa.Core.Models.Translation     // 標準の翻訳モデル名前空間
Baketa.Core.Translation.Models    // 翻訳機能固有のモデル名前空間
```

これらは以下のように使い分けられています：

### 8.1 `Baketa.Core.Models.Translation`

- 主に外部サービスとの連携に使用される標準データモデル
- `TranslationRequest`, `TranslationResponse`, `Language`, `LanguagePair` などの基本モデルを定義
- アプリケーション全体で共有される翻訳関連の一般的なデータ構造

### 8.2 `Baketa.Core.Translation.Models`

- 翻訳システムの内部実装に特化した拡張モデル
- `TranslationContext`, `TranslationCacheEntry`, `TranslationManagementOptions` などの詳細実装モデル
- キャッシュやイベントなど翻訳システムの拡張機能に関連するデータ構造

### 8.3 名前空間の競合を避ける

同じ名前のクラスが複数の名前空間に存在する場合、以下のアプローチを使用して名前空間の競合を避けてください：

```csharp
// 名前空間エイリアスを定義して曖昧さを回避
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;

// 使用例
public Task<CoreModels.TranslationResponse> TranslateAsync(
    CoreModels.TranslationRequest request,
    TransModels.TranslationContext context)
{
    // 実装
}
```

### 8.4 長期的な目標

長期的には、下記のように名前空間を整理していく予定です：

- `Baketa.Core.Models.Translation` を標準名前空間として維持
- 重複するデータモデルを標準名前空間に統合
- 標準名前空間に統合できない延長モデルは `Baketa.Core.Translation.Models.機能名` のように分類

## 9. まとめ

新しい名前空間構造は、より明確な責任分担と拡張性の高いアーキテクチャを実現します。このガイドラインに従うことで、一貫性のあるコードベースを維持し、名前空間の衝突や構造上の問題を防ぐことができます。

疑問や提案がある場合は、開発チームにお問い合わせください。
