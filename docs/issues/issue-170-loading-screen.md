# Issue #170: ローディング画面実装

## 📋 概要
アプリケーション起動時に表示するローディング画面を実装し、初期化処理の進捗をユーザーに視覚的にフィードバックします。

## 🎯 目的
- アプリケーション起動時の体験向上
- 初期化処理の進捗可視化
- ユーザーの待機時間に対する不安軽減

## 📦 Epic
**Epic 3: UI/UXの刷新** (#166 - #173)

## 🔗 依存関係
- **Blocks**: なし
- **Blocked by**: なし
- **Related**: #171 (メインウィンドウUI刷新)

## 📝 要件

### 機能要件
1. **ローディング画面表示**
   - アプリケーション起動直後に全画面ローディング画面を表示
   - 半透明背景 (`#000000` 80% opacity)
   - 中央にBaketaロゴと進捗インジケーター

2. **進捗ステップ表示**
   - 初期化処理の各ステップを表示:
     - "依存関係を解決しています..."
     - "OCRモデルを読み込んでいます..."
     - "翻訳エンジンを初期化しています..."
     - "UI コンポーネントを準備しています..."
   - 各ステップ完了時にチェックマーク (✓) を表示

3. **アニメーション**
   - スピナー/ローディングアニメーション (回転する円形など)
   - ステップ切り替え時のフェードイン/アウト (0.3秒)
   - 完了後のフェードアウト (0.5秒) → MainWindow表示

4. **完了判定**
   - 全ての初期化処理が完了したらMainWindowへ遷移
   - エラー発生時はエラーダイアログ表示後に終了

### 非機能要件
1. **パフォーマンス**
   - ローディング画面表示までの時間: <500ms
   - UIスレッドブロッキングなし (すべて非同期処理)

2. **UIデザイン**
   - ダークテーマベース (背景: `#1E1E1E`)
   - ロゴ: 中央配置、200x200px
   - テキスト: `#FFFFFF`、16px、Meiryo UI
   - スピナー: Primary color (`#007ACC`)

## 🏗️ 実装方針

### 1. LoadingWindow.axaml
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Baketa.UI.Views.LoadingWindow"
        Title="Baketa - 起動中..."
        Width="600" Height="400"
        WindowStartupLocation="CenterScreen"
        CanResize="False"
        ShowInTaskbar="False"
        Background="#CC000000">

    <Grid>
        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="30">
            <!-- Baketaロゴ -->
            <Image Source="/Assets/baketa-logo.png" Width="200" Height="200" />

            <!-- スピナー -->
            <ProgressBar IsIndeterminate="True" Width="300" />

            <!-- 進捗ステップ -->
            <ItemsControl ItemsSource="{Binding InitializationSteps}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="10" Margin="0,5">
                            <TextBlock Text="{Binding Status}"
                                       FontSize="14"
                                       Foreground="#FFFFFF"
                                       Width="30" />
                            <TextBlock Text="{Binding Message}"
                                       FontSize="14"
                                       Foreground="#FFFFFF" />
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Grid>
</Window>
```

### 2. LoadingViewModel.cs
```csharp
namespace Baketa.UI.ViewModels;

public class LoadingViewModel : ViewModelBase
{
    private readonly IApplicationInitializer _initializer;

    public ObservableCollection<InitializationStep> InitializationSteps { get; } = new();

    public LoadingViewModel(IApplicationInitializer initializer)
    {
        _initializer = initializer;
        _initializer.ProgressChanged += OnProgressChanged;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _initializer.InitializeAsync();
        }
        catch (Exception ex)
        {
            // エラーダイアログ表示
            await ShowErrorDialogAsync(ex.Message);
            Environment.Exit(1);
        }
    }

    private void OnProgressChanged(object? sender, InitializationProgressEventArgs e)
    {
        var step = InitializationSteps.FirstOrDefault(s => s.Id == e.StepId);
        if (step != null)
        {
            step.Status = e.IsCompleted ? "✓" : "...";
        }
    }
}

public class InitializationStep : ReactiveObject
{
    [Reactive] public string Id { get; set; } = string.Empty;
    [Reactive] public string Message { get; set; } = string.Empty;
    [Reactive] public string Status { get; set; } = "...";
}
```

### 3. IApplicationInitializer Interface
```csharp
namespace Baketa.Core.Abstractions.Services;

public interface IApplicationInitializer
{
    event EventHandler<InitializationProgressEventArgs> ProgressChanged;
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public class InitializationProgressEventArgs : EventArgs
{
    public required string StepId { get; init; }
    public required string Message { get; init; }
    public bool IsCompleted { get; init; }
}
```

### 4. ApplicationInitializer.cs
```csharp
namespace Baketa.Application.Services;

public class ApplicationInitializer : IApplicationInitializer
{
    private readonly IServiceProvider _serviceProvider;
    public event EventHandler<InitializationProgressEventArgs>? ProgressChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Step 1: 依存関係解決
        ReportProgress("resolve_dependencies", "依存関係を解決しています...", false);
        await ResolveDependenciesAsync(cancellationToken);
        ReportProgress("resolve_dependencies", "依存関係を解決しています...", true);

        // Step 2: OCRモデル読み込み
        ReportProgress("load_ocr", "OCRモデルを読み込んでいます...", false);
        var ocrEngine = _serviceProvider.GetRequiredService<IOcrEngine>();
        await ocrEngine.InitializeAsync(cancellationToken);
        ReportProgress("load_ocr", "OCRモデルを読み込んでいます...", true);

        // Step 3: 翻訳エンジン初期化
        ReportProgress("init_translation", "翻訳エンジンを初期化しています...", false);
        var translationService = _serviceProvider.GetRequiredService<ITranslationService>();
        await translationService.WarmUpAsync(cancellationToken);
        ReportProgress("init_translation", "翻訳エンジンを初期化しています...", true);

        // Step 4: UIコンポーネント準備
        ReportProgress("prepare_ui", "UIコンポーネントを準備しています...", false);
        await PrepareUIComponentsAsync(cancellationToken);
        ReportProgress("prepare_ui", "UIコンポーネントを準備しています...", true);
    }

    private void ReportProgress(string stepId, string message, bool isCompleted)
    {
        ProgressChanged?.Invoke(this, new InitializationProgressEventArgs
        {
            StepId = stepId,
            Message = message,
            IsCompleted = isCompleted
        });
    }
}
```

### 5. App.axaml.cs 統合
```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        // ローディング画面を表示
        var loadingWindow = new LoadingWindow
        {
            DataContext = _serviceProvider.GetRequiredService<LoadingViewModel>()
        };

        desktop.MainWindow = loadingWindow;
        loadingWindow.Show();

        // バックグラウンドで初期化
        _ = Task.Run(async () =>
        {
            await ((LoadingViewModel)loadingWindow.DataContext).InitializeAsync();

            // 初期化完了後、MainWindowへ遷移
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var mainWindow = new MainWindow
                {
                    DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
                };

                desktop.MainWindow = mainWindow;
                loadingWindow.Close();
                mainWindow.Show();
            });
        });
    }

    base.OnFrameworkInitializationCompleted();
}
```

## ✅ 受け入れ基準

### 機能テスト
- [ ] アプリケーション起動時にローディング画面が表示される
- [ ] 4つの初期化ステップが順番に実行・表示される
- [ ] 各ステップ完了時にチェックマーク (✓) が表示される
- [ ] 全ステップ完了後、MainWindowへ遷移する
- [ ] 初期化エラー時にエラーダイアログが表示される
- [ ] **進行状況パーセンテージが表示される**
- [ ] **リトライ可能なエラーで自動リトライが実行される**
- [ ] **エラーメッセージが適切に分類される** (ファイル不足、権限エラー等)

### UIテスト
- [ ] ローディング画面のデザインが仕様通り
- [ ] スピナーアニメーションが正常に動作
- [ ] ステップ切り替え時のフェードイン/アウトが滑らか
- [ ] 完了後のフェードアウトが正常に動作
- [ ] **プログレスバーが正確に更新される**

### パフォーマンステスト
- [ ] ローディング画面表示までの時間が500ms以内
- [ ] UIスレッドがブロックされない
- [ ] **各ステップの実行時間がログに記録される**
- [ ] **総初期化時間がログに記録される**

### 単体テスト
```csharp
public class ApplicationInitializerTests
{
    [Fact]
    public async Task InitializeAsync_成功時_全ステップを順番に実行()
    {
        // Arrange
        var initializer = new ApplicationInitializer(_serviceProvider);
        var progressEvents = new List<InitializationProgressEventArgs>();
        initializer.ProgressChanged += (s, e) => progressEvents.Add(e);

        // Act
        await initializer.InitializeAsync();

        // Assert
        progressEvents.Should().HaveCount(8); // 4ステップ × 2 (開始/完了)
        progressEvents.Where(e => e.IsCompleted).Should().HaveCount(4);
    }

    [Fact]
    public async Task InitializeAsync_OCR初期化失敗時_例外をスロー()
    {
        // Arrange
        _mockOcrEngine.Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new Exception("OCR initialization failed"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _initializer.InitializeAsync());
    }
}
```

## 🔧 追加機能（強化版）

### 1. 進行状況の詳細化
```csharp
public class InitializationStep : ReactiveObject
{
    [Reactive] public string Id { get; set; } = string.Empty;
    [Reactive] public string Message { get; set; } = string.Empty;
    [Reactive] public string Status { get; set; } = "...";
    [Reactive] public int Progress { get; set; } = 0; // 0-100%
    [Reactive] public bool IsCompleted { get; set; }
    [Reactive] public bool HasError { get; set; }
    [Reactive] public string? ErrorMessage { get; set; }
}

// LoadingWindow.axamlにプログレスバー追加
<ProgressBar Value="{Binding CurrentProgress}"
             Maximum="100"
             Width="300"
             Height="8"
             Foreground="#007ACC" />
```

### 2. パフォーマンスメトリクス
```csharp
public class ApplicationInitializer : IApplicationInitializer
{
    private readonly ILogger<ApplicationInitializer> _logger;
    private readonly Stopwatch _stopwatch = new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Start();

        await ExecuteStepAsync("resolve_dependencies", "依存関係を解決しています...",
            async () => await ResolveDependenciesAsync(cancellationToken), cancellationToken);

        await ExecuteStepAsync("load_ocr", "OCRモデルを読み込んでいます...",
            async () => await InitializeOcrAsync(cancellationToken), cancellationToken);

        await ExecuteStepAsync("init_translation", "翻訳エンジンを初期化しています...",
            async () => await InitializeTranslationAsync(cancellationToken), cancellationToken);

        await ExecuteStepAsync("prepare_ui", "UIコンポーネントを準備しています...",
            async () => await PrepareUIComponentsAsync(cancellationToken), cancellationToken);

        _stopwatch.Stop();
        _logger.LogInformation("アプリケーション初期化完了: {ElapsedMs}ms", _stopwatch.ElapsedMilliseconds);
    }

    private async Task ExecuteStepAsync(string stepId, string message, Func<Task> action, CancellationToken cancellationToken)
    {
        var stepStopwatch = Stopwatch.StartNew();
        ReportProgress(stepId, message, false, 0);

        try
        {
            await action();
            stepStopwatch.Stop();

            _logger.LogInformation("ステップ完了: {StepId} ({ElapsedMs}ms)", stepId, stepStopwatch.ElapsedMilliseconds);
            ReportProgress(stepId, message, true, 100);
        }
        catch (Exception ex)
        {
            stepStopwatch.Stop();
            _logger.LogError(ex, "ステップ失敗: {StepId} ({ElapsedMs}ms)", stepId, stepStopwatch.ElapsedMilliseconds);
            throw new InitializationException(stepId, message, ex);
        }
    }
}
```

### 3. エラーハンドリング強化
```csharp
public class InitializationException : Exception
{
    public string StepId { get; }
    public string StepMessage { get; }
    public InitializationErrorType ErrorType { get; }

    public InitializationException(string stepId, string stepMessage, Exception innerException)
        : base($"初期化ステップ '{stepMessage}' が失敗しました", innerException)
    {
        StepId = stepId;
        StepMessage = stepMessage;
        ErrorType = ClassifyError(innerException);
    }

    private static InitializationErrorType ClassifyError(Exception ex)
    {
        return ex switch
        {
            FileNotFoundException => InitializationErrorType.MissingFile,
            UnauthorizedAccessException => InitializationErrorType.PermissionDenied,
            TimeoutException => InitializationErrorType.Timeout,
            OutOfMemoryException => InitializationErrorType.InsufficientMemory,
            _ => InitializationErrorType.Unknown
        };
    }
}

public enum InitializationErrorType
{
    Unknown,
    MissingFile,
    PermissionDenied,
    Timeout,
    InsufficientMemory,
    NetworkError
}

// LoadingViewModel.csでエラー処理
private async Task InitializeAsync()
{
    try
    {
        await _initializer.InitializeAsync();
    }
    catch (InitializationException ex)
    {
        var errorMessage = ex.ErrorType switch
        {
            InitializationErrorType.MissingFile => "必要なファイルが見つかりません。アプリケーションを再インストールしてください。",
            InitializationErrorType.PermissionDenied => "ファイルへのアクセス権限がありません。管理者権限で実行してください。",
            InitializationErrorType.Timeout => "初期化がタイムアウトしました。もう一度お試しください。",
            InitializationErrorType.InsufficientMemory => "メモリが不足しています。他のアプリケーションを終了してください。",
            _ => $"初期化に失敗しました: {ex.Message}"
        };

        await ShowErrorDialogAsync(ex.StepMessage, errorMessage, ex.InnerException?.Message);
        Environment.Exit(1);
    }
}
```

### 4. リトライロジック
```csharp
public class ApplicationInitializer : IApplicationInitializer
{
    private async Task ExecuteStepWithRetryAsync(string stepId, string message, Func<Task> action,
        CancellationToken cancellationToken, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                ReportProgress(stepId, $"{message} (試行 {attempt}/{maxRetries})", false, 0);
                await action();
                ReportProgress(stepId, message, true, 100);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                _logger.LogWarning(ex, "ステップ {StepId} が失敗しました。リトライします... ({Attempt}/{MaxRetries})",
                    stepId, attempt, maxRetries);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        // 最終試行
        await action();
    }

    private static bool IsTransientError(Exception ex)
    {
        return ex is TimeoutException or IOException or HttpRequestException;
    }
}
```

## 📊 見積もり
- **作業時間**: 10時間（エラーハンドリング強化により+2時間）
- **優先度**: 🟠 High
- **リスク**: 🟡 Medium (非同期処理の複雑さ)

## 📌 備考
- ローディング画面は初回起動時のみ表示 (2回目以降は設定で無効化可能)
- 各ステップの実行時間はログに記録し、パフォーマンス分析に活用
- エラー時のロールバック処理は不要 (アプリケーション終了)

---

**作成日**: 2025-11-18
**最終更新**: 2025-11-18
**作成者**: Claude Code

---

## 更新履歴

### 2025-11-18: エラーハンドリングとパフォーマンス監視強化
- **変更理由**: より堅牢な初期化処理とユーザーフィードバック向上
- **追加内容**:
  - 進行状況パーセンテージ表示
  - パフォーマンスメトリクス（各ステップの実行時間測定）
  - 詳細なエラー分類（5種類のエラータイプ）
  - リトライロジック（一時的なエラーで最大3回リトライ）
  - ユーザーフレンドリーなエラーメッセージ
- **作業時間変更**: 8時間 → 10時間
