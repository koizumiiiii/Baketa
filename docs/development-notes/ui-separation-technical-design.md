# ウィンドウ選択分離UI - 技術的実装設計書

## 1. 状態管理設計

### 1.1 状態定義
```csharp
public enum PreparationState
{
    NotSelected,      // 未選択
    Preparing,        // 準備中（V4初期化・事前処理）
    Ready,           // 準備完了（翻訳開始可能）
    Translating,     // 翻訳中
    Error            // エラー状態
}
```

### 1.2 ウィンドウ情報管理
```csharp
public class SelectedWindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; }
    public DateTime SelectedAt { get; set; }
    public bool IsValid => IsWindowValid(Handle);
}
```

## 2. 同一ウィンドウ再選択時の処理

### 2.1 処理フロー
```csharp
public async Task SelectWindowAsync(IntPtr windowHandle, string windowTitle)
{
    // 同一ウィンドウチェック
    if (_selectedWindow?.Handle == windowHandle)
    {
        switch (_currentState)
        {
            case PreparationState.Ready:
                // ハンドル有効性チェックのみ
                if (!_selectedWindow.IsValid)
                {
                    await StartPreparationAsync(windowHandle, windowTitle);
                }
                else
                {
                    ShowFeedback("既に選択済みです");
                }
                break;
                
            case PreparationState.Preparing:
                // 現在の処理を継続
                ShowFeedback("準備処理中です...");
                break;
                
            case PreparationState.Translating:
                // 選択を無視（ボタンが無効化されているはず）
                break;
        }
        return;
    }
    
    // 新規ウィンドウ選択
    await StartPreparationAsync(windowHandle, windowTitle);
}
```

## 3. バックグラウンド事前処理

### 3.0 初期化戦略の選択肢

#### オプション1: アプリ起動時初期化（非推奨）
```csharp
// ❌ 問題点：
// - 起動時間が10秒以上に増加
// - 使用しない場合もメモリ占有
// - 初回起動時のUXが悪化
```

#### オプション2: 遅延初期化（推奨）
```csharp
// ✅ 利点：
// - 高速起動を維持
// - 必要時のみリソース使用
// - ユーザーが明示的に翻訳機能を使う意思表示後に初期化
```

#### オプション3: バックグラウンド起動時初期化（条件付き推奨）
```csharp
public async Task InitializeOnStartupAsync()
{
    // アプリ起動完了後、低優先度でバックグラウンド初期化
    await Task.Delay(3000); // UIが完全に表示されるまで待機
    
    if (await ShouldPreloadV4ModelAsync()) // 前回使用履歴等から判断
    {
        await Task.Run(() => InitializeV4ModelAsync(), 
            TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

private async Task<bool> ShouldPreloadV4ModelAsync()
{
    // 以下の条件でプリロード判断
    // - 前回のセッションで翻訳機能を使用
    // - 設定で「高速起動モード」がOFF
    // - 十分なメモリがある（8GB以上）
    var lastUsed = await GetLastTranslationUsageAsync();
    var memoryAvailable = GetAvailableMemory() > 8_000_000_000;
    var fastStartDisabled = !_settings.EnableFastStartup;
    
    return lastUsed < TimeSpan.FromDays(7) && memoryAvailable && fastStartDisabled;
}
```

### 3.1 非同期処理設計
```csharp
private CancellationTokenSource? _preparationCts;
private TaskCompletionSource<bool>? _preparationTcs;

private async Task StartPreparationAsync(IntPtr handle, string title)
{
    // 既存の準備処理をキャンセル
    _preparationCts?.Cancel();
    _preparationCts = new CancellationTokenSource();
    _preparationTcs = new TaskCompletionSource<bool>();
    
    try
    {
        _currentState = PreparationState.Preparing;
        _selectedWindow = new SelectedWindowInfo 
        { 
            Handle = handle, 
            Title = title, 
            SelectedAt = DateTime.Now 
        };
        
        // プログレス報告付き非同期処理
        var progress = new Progress<PreparationProgress>(UpdateProgress);
        
        await Task.Run(async () =>
        {
            // Phase 1: V4モデル初期化（キャッシュ済みならスキップ）
            if (!_v4ModelInitialized)
            {
                progress.Report(new PreparationProgress(0.3, "V4モデル初期化中..."));
                await InitializeV4ModelAsync(_preparationCts.Token);
                _v4ModelInitialized = true;
            }
            
            // Phase 2: ウィンドウ固有の事前処理
            progress.Report(new PreparationProgress(0.6, "ウィンドウ解析中..."));
            await PreprocessWindowAsync(handle, _preparationCts.Token);
            
            // Phase 3: 初回キャプチャとOCR準備
            progress.Report(new PreparationProgress(0.9, "OCR準備中..."));
            await PrepareOcrEngineAsync(_preparationCts.Token);
            
            progress.Report(new PreparationProgress(1.0, "準備完了"));
        }, _preparationCts.Token);
        
        _currentState = PreparationState.Ready;
        _preparationTcs.SetResult(true);
    }
    catch (OperationCanceledException)
    {
        // キャンセルされた場合
        _currentState = PreparationState.NotSelected;
    }
    catch (Exception ex)
    {
        _currentState = PreparationState.Error;
        _preparationTcs.SetException(ex);
        ShowError($"準備中にエラーが発生しました: {ex.Message}");
    }
}
```

### 3.2 進捗報告
```csharp
public class PreparationProgress
{
    public double Percentage { get; set; }
    public string Message { get; set; }
    public PreparationProgress(double percentage, string message)
    {
        Percentage = percentage;
        Message = message;
    }
}
```

## 4. メモリ・リソース管理

### 4.1 スマートキャッシング
```csharp
public class OcrResourceCache
{
    private readonly MemoryCache _cache;
    private readonly MemoryCacheOptions _options;
    
    public OcrResourceCache()
    {
        _options = new MemoryCacheOptions
        {
            SizeLimit = 500_000_000, // 500MB上限
            CompactionPercentage = 0.25
        };
        _cache = new MemoryCache(_options);
    }
    
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        return await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.Size = EstimateSize<T>();
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return await factory();
        });
    }
}
```

### 4.2 アイドル時リソース解放
```csharp
private Timer? _idleTimer;
private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(5);

private void ResetIdleTimer()
{
    _idleTimer?.Dispose();
    _idleTimer = new Timer(_ => ReleaseIdleResources(), null, _idleTimeout, Timeout.InfiniteTimeSpan);
}

private void ReleaseIdleResources()
{
    if (_currentState == PreparationState.Ready)
    {
        // V4モデル以外のリソースを解放
        _ocrResourceCache.Compact(0.5);
        GC.Collect(2, GCCollectionMode.Optimized);
    }
}
```

## 5. エラーハンドリング戦略

### 5.1 段階的フォールバック
```csharp
private async Task<OcrResult> ExecuteOcrWithFallbackAsync(Mat image)
{
    // Level 1: V4最適化モード
    try
    {
        return await ExecuteV4OptimizedAsync(image, timeout: TimeSpan.FromSeconds(15));
    }
    catch (TimeoutException)
    {
        _logger.LogWarning("V4最適化モードがタイムアウト、標準モードにフォールバック");
    }
    
    // Level 2: V4標準モード
    try
    {
        return await ExecuteV4StandardAsync(image, timeout: TimeSpan.FromSeconds(20));
    }
    catch (TimeoutException)
    {
        _logger.LogWarning("V4標準モードがタイムアウト、V3にフォールバック");
    }
    
    // Level 3: V3モード
    return await ExecuteV3Async(image, timeout: TimeSpan.FromSeconds(10));
}
```

### 5.2 自動リトライ with Exponential Backoff
```csharp
private async Task<T> RetryWithBackoffAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    var delay = TimeSpan.FromSeconds(1);
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (i < maxRetries - 1)
        {
            _logger.LogWarning($"操作失敗 (試行 {i + 1}/{maxRetries}): {ex.Message}");
            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2); // Exponential backoff
        }
    }
    
    return await operation(); // 最後の試行で例外をそのまま投げる
}
```

## 6. UI統合ポイント

### 6.1 ViewModelプロパティ
```csharp
// 状態管理
public PreparationState CurrentState { get; private set; }
public bool IsSelectEnabled => CurrentState != PreparationState.Translating;
public bool IsStartEnabled => CurrentState == PreparationState.Ready;
public string StatusText => GetStatusText(CurrentState);

// 進捗表示
public double PreparationProgress { get; private set; }
public string PreparationMessage { get; private set; }
public bool IsPreparationVisible => CurrentState == PreparationState.Preparing;

// コマンド
public ReactiveCommand<Unit, Unit> SelectWindowCommand { get; }
public ReactiveCommand<Unit, Unit> StartTranslationCommand { get; }
public ReactiveCommand<Unit, Unit> StopTranslationCommand { get; }
```

### 6.2 状態遷移の可視化
```csharp
private string GetStatusText(PreparationState state)
{
    return state switch
    {
        PreparationState.NotSelected => "ウィンドウ未選択",
        PreparationState.Preparing => $"準備中... {PreparationProgress:P0}",
        PreparationState.Ready => "準備完了",
        PreparationState.Translating => "翻訳中",
        PreparationState.Error => "エラー",
        _ => ""
    };
}
```

## 7. パフォーマンス最適化

### 7.1 プログレッシブ初期化
```csharp
// 最小限の初期化で「準備完了」を表示し、詳細な最適化は継続
private async Task StartProgressivePreparationAsync(IntPtr handle)
{
    // Phase 1: 最小限初期化（1-2秒）
    await QuickInitializeAsync();
    _currentState = PreparationState.Ready; // 早期に準備完了
    
    // Phase 2: バックグラウンド最適化（継続）
    _ = Task.Run(async () =>
    {
        await OptimizeInBackgroundAsync();
    });
}
```

### 7.2 事前キャプチャ戦略
```csharp
// ウィンドウ選択直後に低解像度キャプチャを実行
private async Task PreCaptureWindowAsync(IntPtr handle)
{
    var lowResImage = await CaptureWindowAsync(handle, scale: 0.5);
    await _ocrResourceCache.SetAsync($"precapture_{handle}", lowResImage);
}
```

## 作成日
2025-07-19

## 更新履歴
- 2025-07-19: 初版作成 - 技術的実装設計