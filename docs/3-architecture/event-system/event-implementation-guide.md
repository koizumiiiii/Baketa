# イベント集約機構 実装ガイド

## 1. はじめに

このドキュメントは、Baketaアプリケーションでイベント集約機構を活用するための実装ガイドです。システムコンポーネント間の疎結合なコミュニケーションを実現するためのベストプラクティスを提供します。

## 2. イベントの作成

### 2.1 基本的なイベント作成

イベントは `IEvent` インターフェースを実装する必要があります。以下のような基本実装が推奨されます：

```csharp
/// <summary>
/// イベント基本実装の抽象クラス
/// </summary>
public abstract class EventBase : IEvent
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    protected EventBase()
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }
    
    /// <inheritdoc />
    public Guid Id { get; }
    
    /// <inheritdoc />
    public DateTime Timestamp { get; }
    
    /// <inheritdoc />
    public abstract string Name { get; }
    
    /// <inheritdoc />
    public abstract string Category { get; }
}

/// <summary>
/// キャプチャ完了イベント
/// </summary>
public class CaptureCompletedEvent : EventBase
{
    /// <summary>
    /// キャプチャ完了イベントを初期化します
    /// </summary>
    /// <param name="capturedImage">キャプチャされた画像</param>
    public CaptureCompletedEvent(IImage capturedImage)
    {
        CapturedImage = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));
    }
    
    /// <summary>
    /// キャプチャされた画像
    /// </summary>
    public IImage CapturedImage { get; }
    
    /// <inheritdoc />
    public override string Name => "CaptureCompleted";
    
    /// <inheritdoc />
    public override string Category => "Capture";
}
```

### 2.2 イベント設計のポイント

- **イベント名**: わかりやすく具体的な名前を付ける（`OcrCompleted`, `TranslationRequested` など）
- **カテゴリ**: 機能領域ごとにカテゴリを設定（`OCR`, `Capture`, `Translation` など）
- **データ**: イベントに必要な情報を適切にプロパティとして持たせる
- **不変性**: イベントデータは変更不可（イミュータブル）にする
- **検証**: コンストラクタでパラメータの検証を行う

### 2.3 イベントカテゴリの一覧

以下のカテゴリを標準として使用します：

| カテゴリ名 | 用途 |
|------------|------|
| `Capture` | 画面キャプチャ関連イベント |
| `OCR` | テキスト検出・認識関連イベント |
| `Translation` | 翻訳処理関連イベント |
| `UI` | ユーザーインターフェース関連イベント |
| `System` | アプリケーション全体やシステム関連イベント |
| `Settings` | 設定変更関連イベント |

## 3. イベントプロセッサの作成

### 3.1 基本的なイベントプロセッサ

イベントプロセッサは `IEventProcessor<TEvent>` インターフェースを実装する必要があります：

```csharp
/// <summary>
/// キャプチャ完了イベントを処理するプロセッサ
/// </summary>
public class CaptureCompletedProcessor : IEventProcessor<CaptureCompletedEvent>
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<CaptureCompletedProcessor> _logger;
    
    /// <summary>
    /// プロセッサを初期化します
    /// </summary>
    /// <param name="ocrService">OCRサービス</param>
    /// <param name="logger">ロガー</param>
    public CaptureCompletedProcessor(
        IOcrService ocrService,
        ILogger<CaptureCompletedProcessor> logger)
    {
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <inheritdoc />
    public async Task HandleAsync(CaptureCompletedEvent eventData)
    {
        try
        {
            _logger.LogDebug("キャプチャ完了イベントの処理を開始: {EventId}", eventData.Id);
            
            // キャプチャ画像に対してOCR処理を実行
            await _ocrService.ProcessImageAsync(eventData.CapturedImage);
            
            _logger.LogDebug("キャプチャ完了イベントの処理が完了: {EventId}", eventData.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キャプチャ完了イベントの処理中にエラーが発生: {EventId}", eventData.Id);
            throw; // 再スローして上位レイヤーでのハンドリングを可能にする
        }
    }
}
```

### 3.2 プロセッサ設計のポイント

- **単一責任**: 各プロセッサは明確に定義された単一の責任を持つ
- **例外処理**: 適切な例外処理を行い、必要に応じてログを残す
- **非同期処理**: 非同期メソッドを正しく実装し、ConfigureAwait(false)の使用を検討
- **依存性注入**: 必要なサービスはDIで注入する
- **パフォーマンス**: 長時間実行される処理はバックグラウンドで実行することを検討

## 4. DIでの登録と使用

### 4.1 サービス登録

アプリケーション起動時に、イベント集約機構とプロセッサを登録します：

```csharp
// Startup.cs または Program.cs での登録例
services.AddEventAggregator(); // EventAggregatorServiceExtensionsの拡張メソッド

// プロセッサの登録
services.AddTransient<CaptureCompletedProcessor>();
services.AddTransient<OcrCompletedProcessor>();
services.AddTransient<TranslationCompletedProcessor>();
```

### 4.2 プロセッサの自動登録

多数のプロセッサを管理する場合は、自動登録ヘルパーの使用を検討します：

```csharp
// イベントプロセッサ登録サービス
public class EventProcessorRegistrationService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventProcessorRegistrationService> _logger;
    
    public EventProcessorRegistrationService(
        IEventAggregator eventAggregator,
        IServiceProvider serviceProvider,
        ILogger<EventProcessorRegistrationService> logger)
    {
        _eventAggregator = eventAggregator;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public void RegisterAllProcessors()
    {
        // リフレクションを使用して全プロセッサタイプを取得
        var processorTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && IsEventProcessor(t))
            .ToList();
            
        foreach (var processorType in processorTypes)
        {
            RegisterProcessor(processorType);
        }
        
        _logger.LogInformation("{Count}個のイベントプロセッサを登録しました", processorTypes.Count);
    }
    
    private bool IsEventProcessor(Type type)
    {
        return type.GetInterfaces()
            .Any(i => i.IsGenericType && 
                i.GetGenericTypeDefinition() == typeof(IEventProcessor<>));
    }
    
    private void RegisterProcessor(Type processorType)
    {
        try
        {
            // プロセッサインスタンスをDIから取得
            var processor = _serviceProvider.GetService(processorType);
            if (processor == null)
            {
                _logger.LogWarning("プロセッサ {ProcessorType} をDIから取得できませんでした", processorType.Name);
                return;
            }
            
            // イベント型を取得
            var eventType = processorType.GetInterfaces()
                .First(i => i.IsGenericType && 
                    i.GetGenericTypeDefinition() == typeof(IEventProcessor<>))
                .GetGenericArguments()[0];
                
            // 動的に登録メソッドを呼び出す
            var subscribeMethod = typeof(IEventAggregator)
                .GetMethod(nameof(IEventAggregator.Subscribe))
                ?.MakeGenericMethod(eventType);
                
            subscribeMethod?.Invoke(_eventAggregator, new[] { processor });
            
            _logger.LogDebug("プロセッサ {ProcessorType} をイベント {EventType} に登録しました", 
                processorType.Name, eventType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロセッサ {ProcessorType} の登録に失敗しました", processorType.Name);
        }
    }
}
```

## 5. イベント発行パターン

### 5.1 基本的なイベント発行

```csharp
// サービスからのイベント発行例
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
```

### 5.2 キャンセレーション対応

長時間実行される処理ではキャンセレーショントークンを使用したイベント発行を検討します：

```csharp
public async Task<IImage> CaptureRegionWithCancellationAsync(
    Rectangle region, 
    CancellationToken cancellationToken = default)
{
    // キャンセル可能なキャプチャを実行
    var image = await _windowsCaptureService.CaptureRegionAsync(region);
    
    // キャンセレーション対応のイベント発行
    await _eventAggregator.PublishAsync(
        new CaptureCompletedEvent(image), 
        cancellationToken);
    
    return image;
}
```

### 5.3 イベントチェーン

イベントプロセッサから別のイベントを発行することで、処理フローを構築できます：

```csharp
// OCR完了後に翻訳イベントを発行するプロセッサ
public class OcrCompletedProcessor : IEventProcessor<OcrCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator;
    
    public OcrCompletedProcessor(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }
    
    public async Task HandleAsync(OcrCompletedEvent eventData)
    {
        // 検出されたテキストがある場合は翻訳イベントを発行
        if (eventData.DetectedTexts.Any())
        {
            foreach (var textResult in eventData.DetectedTexts)
            {
                await _eventAggregator.PublishAsync(
                    new TranslationRequestedEvent(
                        textResult.Text,
                        textResult.Region,
                        SourceLanguage.Japanese,
                        TargetLanguage.English));
            }
        }
    }
}
```

## 6. テスト戦略

### 6.1 ユニットテスト例

イベント集約機構とプロセッサのテスト例：

```csharp
// イベントプロセッサテスト
public class CaptureCompletedProcessorTests
{
    [Fact]
    public async Task HandleAsync_WithValidImage_CallsOcrService()
    {
        // Arrange
        var mockOcrService = new Mock<IOcrService>();
        var mockLogger = new Mock<ILogger<CaptureCompletedProcessor>>();
        
        var processor = new CaptureCompletedProcessor(
            mockOcrService.Object,
            mockLogger.Object);
            
        var mockImage = new Mock<IImage>();
        var testEvent = new CaptureCompletedEvent(mockImage.Object);
        
        // Act
        await processor.HandleAsync(testEvent);
        
        // Assert
        mockOcrService.Verify(
            s => s.ProcessImageAsync(It.Is<IImage>(img => img == mockImage.Object)),
            Times.Once);
    }
}

// イベント集約機構テスト
public class EventAggregatorTests
{
    [Fact]
    public async Task PublishAsync_WithRegisteredProcessor_ProcessesEvent()
    {
        // Arrange
        var eventAggregator = new EventAggregator();
        var mockProcessor = new Mock<IEventProcessor<TestEvent>>();
        var testEvent = new TestEvent("テストデータ");
        
        // プロセッサを登録
        eventAggregator.Subscribe(mockProcessor.Object);
        
        // Act
        await eventAggregator.PublishAsync(testEvent);
        
        // Assert
        mockProcessor.Verify(
            p => p.HandleAsync(It.Is<TestEvent>(e => e == testEvent)),
            Times.Once);
    }
    
    // テスト用イベント
    private class TestEvent : IEvent
    {
        public TestEvent(string data)
        {
            Data = data;
            Id = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
        }
        
        public string Data { get; }
        public Guid Id { get; }
        public DateTime Timestamp { get; }
        public string Name => "Test";
        public string Category => "Test";
    }
}
```

### 6.2 統合テスト

複数のコンポーネントが連携する統合テスト例：

```csharp
public class CaptureToTranslationIntegrationTests
{
    [Fact]
    public async Task CaptureToTranslation_CompletesFullFlow()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // モックサービスを登録
        var mockWindowsCaptureService = new Mock<IWindowsCaptureService>();
        var mockOcrService = new Mock<IOcrService>();
        var mockTranslationService = new Mock<ITranslationService>();
        
        services.AddSingleton(mockWindowsCaptureService.Object);
        services.AddSingleton(mockOcrService.Object);
        services.AddSingleton(mockTranslationService.Object);
        
        // 実際のイベント集約機構を登録
        services.AddEventAggregator();
        
        // プロセッサを登録
        services.AddTransient<CaptureCompletedProcessor>();
        services.AddTransient<OcrCompletedProcessor>();
        services.AddTransient<TranslationRequestedProcessor>();
        
        // Service Providerを構築
        var serviceProvider = services.BuildServiceProvider();
        
        // 各コンポーネントを取得
        var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
        var captureService = new CaptureService(
            eventAggregator,
            mockWindowsCaptureService.Object);
            
        // プロセッサを登録
        var captureProcessor = serviceProvider.GetRequiredService<CaptureCompletedProcessor>();
        var ocrProcessor = serviceProvider.GetRequiredService<OcrCompletedProcessor>();
        var translationProcessor = serviceProvider.GetRequiredService<TranslationRequestedProcessor>();
        
        eventAggregator.Subscribe(captureProcessor);
        eventAggregator.Subscribe(ocrProcessor);
        eventAggregator.Subscribe(translationProcessor);
        
        // 戻り値をセットアップ
        var mockImage = new Mock<IImage>();
        mockWindowsCaptureService
            .Setup(s => s.CaptureScreenAsync())
            .ReturnsAsync(mockImage.Object);
            
        var ocrResults = new List<OcrTextResult>
        {
            new OcrTextResult { Text = "テストテキスト", Region = new Rectangle(10, 10, 100, 20) }
        };
        
        mockOcrService
            .Setup(s => s.ProcessImageAsync(It.IsAny<IImage>()))
            .Callback<IImage>(img => 
            {
                // OCR処理が完了したらイベントを発行
                eventAggregator.PublishAsync(new OcrCompletedEvent(img, ocrResults));
            })
            .Returns(Task.CompletedTask);
            
        mockTranslationService
            .Setup(s => s.TranslateAsync(
                It.IsAny<string>(), 
                It.IsAny<SourceLanguage>(), 
                It.IsAny<TargetLanguage>()))
            .ReturnsAsync("Test text");
            
        // Act
        await captureService.CaptureScreenAsync();
        
        // スレッド間の競合を避けるために少し待機
        await Task.Delay(100);
        
        // Assert
        mockOcrService.Verify(
            s => s.ProcessImageAsync(It.IsAny<IImage>()),
            Times.Once);
            
        mockTranslationService.Verify(
            s => s.TranslateAsync(
                It.Is<string>(t => t == "テストテキスト"),
                It.Is<SourceLanguage>(sl => sl == SourceLanguage.Japanese),
                It.Is<TargetLanguage>(tl => tl == TargetLanguage.English)),
            Times.Once);
    }
}
```

## 7. パフォーマンスに関する考慮事項

### 7.1 イベント処理のパフォーマンス

- 重い処理はバックグラウンドタスクに移行する
- 適切なタイミングでイベントを発行する（UI更新など頻度の高いものには注意）
- イベントデータのサイズを適切に保つ（大きな画像などは参照を渡す）

### 7.2 メモリリーク防止

- イベントハンドラーの解除を忘れないようにする
- 長寿命オブジェクトでのイベント購読には特に注意
- Disposeパターンの適切な実装

```csharp
public class SomeViewModel : IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IEventProcessor<SomeEvent> _processor;
    private bool _disposed;
    
    public SomeViewModel(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _processor = new SomeEventProcessor(this);
        
        // イベント購読
        _eventAggregator.Subscribe(_processor);
    }
    
    // リソース解放
    public void Dispose()
    {
        if (!_disposed)
        {
            // イベント購読解除
            _eventAggregator.Unsubscribe(_processor);
            _disposed = true;
        }
    }
}
```

## 8. よくある問題と解決策

### 8.1 イベントチェーンの無限ループ

複数のイベントプロセッサが循環的にイベントを発行するとスタックオーバーフローやメモリ不足の原因になります。

**解決策**:
- イベントチェーンの設計を見直す
- ループ検出メカニズムの導入（イベントIDの追跡など）
- 処理中フラグの導入

### 8.2 非同期処理の問題

`async void`メソッドを使用するとエラーハンドリングが難しくなります。

**解決策**:
- `async Task`を使用して非同期メソッドを実装
- `await`を忘れずに使用
- 適切な例外処理

### 8.3 パフォーマンス問題

多数のイベントが発行される場合やイベント処理が重い場合にパフォーマンス問題が発生することがあります。

**解決策**:
- イベント処理の頻度を減らす（スロットリング、デバウンシング）
- バッチ処理の導入
- 並列処理のチューニング
- 処理時間が長いイベントは別のバックグラウンドタスクに移行

## 9. ベストプラクティス

1. **命名規則を統一する**
   - イベント: `[動作][過去分詞]Event` (例: `CaptureCompletedEvent`)
   - プロセッサ: `[イベント名]Processor` (例: `CaptureCompletedProcessor`)

2. **適切な粒度のイベントを設計する**
   - 粒度が大きすぎると再利用性が下がる
   - 粒度が小さすぎるとパフォーマンスとメンテナンス性が悪化

3. **イベントの不変性を保証する**
   - イベントデータはイミュータブルに設計
   - コンストラクタでのみ値を設定
   - 必要に応じてディープコピーを実装

4. **例外を適切に処理する**
   - イベントプロセッサ内の例外はログに記録
   - 重大なエラーは適切に通知

5. **CancellationTokenを使用する**
   - 長時間実行される処理ではキャンセレーション対応を実装
   - ユーザー操作によるキャンセルを適切に処理

6. **メトリクスを活用する**
   - `EventProcessorMetrics`を使用してパフォーマンスを監視
   - ボトルネックを特定して最適化

## 10. 参考資料

- [イベント集約機構の概要](./event-system-overview.md)
- [Issue #24: イベント集約機構の構築](../../.github/issues/issue_24.md)
- [Issue #27: イベント集約管理機能の実装](../../.github/issues/issue_27.md)
- [アーキテクチャ設計ドキュメント](../improved-architecture.md#7-イベント集約機構の設計)