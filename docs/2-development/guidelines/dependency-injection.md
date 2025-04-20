# Baketaプロジェクト 依存性注入ガイドライン

*最終更新: 2025年4月20日*

## 1. 概要

このドキュメントでは、Baketaプロジェクトにおける依存性注入（DI）の使用に関するガイドラインを提供します。統一された方法でサービスを登録し、疎結合で保守性の高いコードを実現するための指針となります。

## 2. 基本原則

### 2.1 DIの目的
- **疎結合の実現**: コンポーネント間の直接的な依存関係を減らす
- **テスト容易性の向上**: サービスのモック化や置き換えを容易にする
- **ライフタイム管理の一元化**: オブジェクトのライフサイクルを統一的に管理
- **プラットフォーム抽象化**: プラットフォーム固有コードを隠蔽し、共通インターフェースを公開

### 2.2 サービス登録のモジュール化
- 関連するサービス登録は機能コンポーネントごとにグループ化
- 各モジュールは独自の拡張メソッドとして実装
- プロジェクト構造とサービス登録構造の整合性を維持

## 3. サービスライフタイム管理

### 3.1 Singletonサービス
- **適用範囲**: アプリケーション全体で単一インスタンスが必要なサービス
- **使用例**:
  - 設定管理サービス
  - ロギングサービス
  - キャッシュサービス
  - グローバル状態を持つサービス

```csharp
// シングルトンサービスの登録
services.AddSingleton<ISettingsService, JsonSettingsService>();
```

### 3.2 Scopedサービス
- **適用範囲**: 特定のスコープ（例: リクエスト、セッション）内で単一インスタンス
- **使用例**:
  - 翻訳セッション
  - 編集コンテキスト

```csharp
// スコープドサービスの登録
services.AddScoped<ITranslationSession, TranslationSession>();
```

### 3.3 Transientサービス
- **適用範囲**: 要求されるたびに新しいインスタンスを作成
- **使用例**:
  - ダイアログビューモデル
  - ステートレス操作を実行するヘルパーサービス

```csharp
// トランジェントサービスの登録
services.AddTransient<IFileOperation, FileOperation>();
```

## 4. ViewModelの登録とライフタイム

### 4.1 シングルトンViewModel
- **適用基準**: アプリケーション全体で状態共有が必要なViewModel
- **一般的な例**:
  - MainViewModel
  - SystemTrayViewModel
  - OverlayViewModel
  - グローバル状態を保持するViewModel

### 4.2 トランジェントViewModel
- **適用基準**: 使用時に常に新しいインスタンスが必要なViewModel
- **一般的な例**:
  - 設定画面ViewModel
  - ダイアログViewModel
  - 表示ごとに新しい状態を持つ必要があるViewModel

### 4.3 スコープドViewModel
- **適用基準**: 特定のコンテキスト内で共有するViewModel
- **一般的な例**:
  - 編集セッションViewModel
  - マルチステップウィザードのViewModel

## 5. 推奨パターン

### 5.1 コンストラクタインジェクション
- 依存関係はコンストラクタパラメータとして宣言
- すべての必須依存関係は非nullパラメータとして定義
- オプションの依存関係はnull許容型として定義

```csharp
// 推奨パターン
public class TranslationService : ITranslationService
{
    private readonly ITranslationEngine _engine;
    private readonly ITranslationCache _cache;
    private readonly ILogger<TranslationService>? _logger;
    
    public TranslationService(
        ITranslationEngine engine,
        ITranslationCache cache,
        ILogger<TranslationService>? logger = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger;
    }
}
```

### 5.2 サービスコレクションの拡張メソッド
- モジュール化された登録には拡張メソッドを使用
- 関連するサービスは一つの拡張メソッドでグループ化
- 他のモジュールとの依存関係を最小限に抑える

```csharp
// 推奨パターン
public static class OcrServiceRegistration
{
    public static IServiceCollection AddBaketaOcrServices(this IServiceCollection services)
    {
        // 関連するサービス登録
        return services;
    }
}
```

### 5.3 プラットフォーム依存の条件付き登録
- プラットフォーム検出に基づいて適切なサービスを登録
- 常に共通インターフェースを通じて依存関係を定義

```csharp
// 推奨パターン
public static IServiceCollection AddPlatformServices(this IServiceCollection services)
{
    if (OperatingSystem.IsWindows())
    {
        services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
    }
    else if (OperatingSystem.IsLinux())
    {
        services.AddSingleton<IScreenCaptureService, LinuxScreenCaptureService>();
    }
    else
    {
        services.AddSingleton<IScreenCaptureService, BasicScreenCaptureService>();
    }
    
    return services;
}
```

## 6. テスト用モックの活用

### 6.1 モックサービスの登録
- テスト用のモックサービス登録拡張メソッドを提供
- 実装よりもインターフェースに依存したテストを作成

```csharp
// テスト用モックサービス登録
public static IServiceCollection AddMockOcrServices(this IServiceCollection services)
{
    services.AddSingleton(Substitute.For<IOcrService>());
    // その他のモックサービス
    
    return services;
}
```

### 6.2 部分的なサービス置き換え
- 実サービスとモックサービスを組み合わせたテスト

```csharp
// 部分的なサービス置き換え
var services = new ServiceCollection();
services.AddBaketaCoreServices(); // 実サービス
services.AddMockOcrServices(); // モックサービス
```

## 7. 良い例と悪い例

### 7.1 良い例

```csharp
// 推奨される書き方
public static IServiceCollection AddTranslationServices(this IServiceCollection services)
{
    services.AddSingleton<ITranslationService, TranslationService>();
    services.AddSingleton<ITranslationCache, TranslationCache>();
    return services;
}

// 使用側
services.AddBaketaCoreServices()
        .AddTranslationServices();
```

### 7.2 避けるべき例

```csharp
// 避けるべき書き方
// 問題: 散在した登録、プロジェクト間の一貫性のない依存関係
services.AddSingleton<Baketa.Core.Services.SettingsService>();
services.AddTransient<Baketa.UI.Avalonia.Services.SettingsService>();

// 問題: インターフェースを使用していない
services.AddSingleton(new LoggingService());

// 問題: 直接的な依存関係の作成
services.AddSingleton<TranslationService>(sp => 
    new TranslationService(new OnnxTranslationEngine()));
```

## 8. 環境設定による条件付き登録

```csharp
// 開発環境とプロダクション環境の区別
if (isDevelopment)
{
    services.AddBaketaCoreDevelopmentServices();
}
else
{
    services.AddBaketaCoreServices();
}
```

## 9. ファクトリパターン

複雑なサービス生成ロジックがある場合はファクトリパターンを利用：

```csharp
// ファクトリ関数の登録
services.AddSingleton<Func<string, ITranslationEngine>>(sp => engineType =>
{
    return engineType switch
    {
        "onnx" => sp.GetRequiredService<IOnnxTranslationEngine>(),
        "cloud" => sp.GetRequiredService<ICloudTranslationEngine>(),
        _ => throw new ArgumentException($"Unknown engine type: {engineType}")
    };
});

// ファクトリの使用（サービス内）
public class TranslationService
{
    private readonly Func<string, ITranslationEngine> _engineFactory;
    
    public TranslationService(Func<string, ITranslationEngine> engineFactory)
    {
        _engineFactory = engineFactory;
    }
    
    public ITranslationEngine GetEngine(string type)
    {
        return _engineFactory(type);
    }
}
```

## 10. 新名前空間構造での依存性注入

名前空間移行（Issue #1～#6）完了後の新しいインターフェース構造では、以下の点に注意して依存性注入を行います。

### 10.1 新インターフェースの注入

```csharp
// 新しい名前空間でのサービス登録
public static IServiceCollection AddImageServices(this IServiceCollection services)
{
    // 新しい名前空間を使用した登録
    services.AddSingleton<Baketa.Core.Abstractions.Factories.IImageFactory, CoreImageFactory>();
    services.AddSingleton<Baketa.Core.Abstractions.Factories.IWindowsImageFactory, WindowsImageFactory>();
    services.AddSingleton<Baketa.Core.Abstractions.Imaging.IImageProcessor, OpenCvImageProcessor>();
    
    return services;
}
```

### 10.2 型エイリアスの活用

同名のインターフェースが複数の名前空間に存在する可能性がある場合は、型エイリアスを使用して明確にします：

```csharp
// 型エイリアスを使用したサービス登録
using IImageFactoryInterface = Baketa.Core.Abstractions.Factories.IImageFactory;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

public static IServiceCollection AddImageServices(this IServiceCollection services)
{
    services.AddSingleton<IImageFactoryInterface, CoreImageFactory>();
    services.AddSingleton<IWindowsImageFactoryInterface, WindowsImageFactory>();
    
    return services;
}
```

### 10.3 モジュール化された依存性注入

新しいアーキテクチャでは、機能ごとにモジュール化された依存性注入を推奨します：

```csharp
// コア機能の依存性注入
public static IServiceCollection AddBaketaCoreServices(this IServiceCollection services)
{
    // コアサービスの登録
    return services;
}

// Windowsプラットフォームサービスの依存性注入
public static IServiceCollection AddWindowsPlatformServices(this IServiceCollection services)
{
    // Windows固有サービスの登録
    return services;
}

// OCRサービスの依存性注入
public static IServiceCollection AddBaketaOcrServices(this IServiceCollection services)
{
    // OCRサービスの登録
    return services;
}
```

このガイドラインに従うことで、Baketaプロジェクトの依存性管理が向上し、一貫性のあるアーキテクチャが実現できます。