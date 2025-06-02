# Baketa翻訳設定UI 実装手順とトラブルシューティング完全ガイド

*作成日: 2025年6月2日*  
*対象: Phase 4.1 最優先実装項目*

## 📚 関連ドキュメント

### 設計ドキュメント
- [UI設計詳細](./baketa_translation_ui_design.md) - UI/UXデザイン仕様と画面構成

### 実装ファイル
- [ViewModelサンプル実装](./baketa_translation_viewmodel_implementation.cs) - ViewModel完全実装例
- [DI設定ファイル](./baketa_translation_ui_di_setup.cs) - 依存性注入設定の完全実装
- [テストファイル](./baketa_translation_ui_tests.cs) - 単体テスト・統合テストの実装例
- [統合設定ファイル](./baketa_translation_ui_integration.cs) - アプリケーション統合コード例

### XAML・スタイル
- [完全XAML実装](./baketa_translation_ui_xaml_complete.txt) - 全画面のXAML実装
- [CodeBehind実装](./baketa_translation_ui_codebehind.cs) - 必要なCodeBehind処理
- [スタイル定義](./baketa_translation_ui_complete_styles.txt) - 完全スタイルファイル

### プロジェクト管理
- [SentencePiece統合研究ノート](../sentencepiece-integration-research.md) - 技術基盤の詳細
- [翻訳システム状況](../baketa-translation-status.md) - 現在の実装状況とタスク管理
- [プロジェクトナレッジベース](../../Baketa%20プロジェクトナレッジベース（完全版）.md) - プロジェクト全体の技術仕様

## 📋 実装チェックリスト

### ✅ 事前準備（必須）

- [ ] **既存サービス確認**: TranslationEngineStatusService が正常動作することを確認
- [ ] **プロジェクト構造確認**: 既存のDI設定とサービス登録が正常に機能することを確認  
- [ ] **NuGetパッケージ**: 必要なパッケージがすべてインストール済みであることを確認
  - [ ] Avalonia.UI
  - [ ] ReactiveUI.Avalonia
  - [ ] Microsoft.Extensions.Hosting
  - [ ] Microsoft.Extensions.DependencyInjection
  - [ ] System.Reactive

### ✅ Phase 1: 基本プロジェクト構造（1日目）

#### ディレクトリ構造作成

```
Baketa.UI/
├── Views/
│   └── Settings/
│       ├── TranslationSettingsView.axaml
│       ├── EngineSelectionControl.axaml
│       ├── LanguagePairSelectionControl.axaml
│       ├── TranslationStrategyControl.axaml
│       └── EngineStatusControl.axaml
├── ViewModels/
│   └── Settings/
│       ├── TranslationSettingsViewModel.cs
│       ├── EngineSelectionViewModel.cs
│       ├── LanguagePairSelectionViewModel.cs
│       ├── TranslationStrategyViewModel.cs
│       └── EngineStatusViewModel.cs
├── Services/
│   ├── IUserPlanService.cs
│   ├── UserPlanService.cs
│   ├── ILocalizationService.cs
│   ├── LocalizationService.cs
│   ├── INotificationService.cs
│   └── AvaloniaNotificationService.cs
├── Extensions/
│   └── UIServiceCollectionExtensions.cs
├── Styles/
│   └── TranslationSettingsStyles.axaml
└── Configuration/
    └── TranslationUIOptions.cs
```

#### 実装手順

1. **ディレクトリ作成**
```bash
mkdir -p Baketa.UI/Views/Settings
mkdir -p Baketa.UI/ViewModels/Settings  
mkdir -p Baketa.UI/Services
mkdir -p Baketa.UI/Extensions
mkdir -p Baketa.UI/Styles
mkdir -p Baketa.UI/Configuration
```

2. **基本ViewModelファイル作成**
- ViewModelBase.cs（既存確認）
- TranslationSettingsViewModel.cs
- 各子ViewModelファイル

3. **基本サービスインターフェース作成**
- IUserPlanService.cs
- ILocalizationService.cs
- INotificationService.cs

### ✅ Phase 2: DI設定とサービス実装（2日目）

#### サービス実装の優先順位

1. **最優先**: UserPlanService（無料/有料プラン判定）
2. **高優先**: LocalizationService（言語設定）
3. **中優先**: AvaloniaNotificationService（通知）
4. **低優先**: その他のヘルパーサービス

#### 実装チェック

- [ ] **UserPlanService**: 現在のプランタイプを正確に返すことを確認
- [ ] **LocalizationService**: アプリ言語設定を正確に取得することを確認
- [ ] **DI設定**: すべてのサービスが正常に解決できることを確認

```csharp
// DI動作テストコード
var services = new ServiceCollection();
services.AddTranslationSettingsUI(configuration);
var provider = services.BuildServiceProvider();

// 各サービスが正常に解決できることを確認
var translationVM = provider.GetRequiredService<TranslationSettingsViewModel>();
var engineVM = provider.GetRequiredService<EngineSelectionViewModel>();
// ... 他のサービス
```

### ✅ Phase 3: ViewModel実装（3-4日目）

#### ViewModel実装順序

1. **EngineSelectionViewModel**（最重要）
   - [ ] プラン判定ロジック
   - [ ] エンジン状態監視
   - [ ] エンジン切り替え処理

2. **LanguagePairSelectionViewModel**
   - [ ] 言語ペア一覧表示
   - [ ] 中国語変種選択
   - [ ] アプリ言語連動

3. **TranslationStrategyViewModel**
   - [ ] Direct/TwoStage選択
   - [ ] フォールバック設定

4. **EngineStatusViewModel**
   - [ ] リアルタイム状態表示
   - [ ] エラー・フォールバック通知

5. **TranslationSettingsViewModel**（統合）
   - [ ] 子ViewModelの統合
   - [ ] 設定保存・復元

#### デバッグポイント

```csharp
// ViewModel動作確認用テストコード
[Test]
public void EngineSelectionViewModel_Construction_ShouldSucceed()
{
    // Arrange
    var mockStatusService = new Mock<ITranslationEngineStatusService>();
    var mockPlanService = new Mock<IUserPlanService>();
    
    // Act & Assert
    Assert.DoesNotThrow(() => 
        new EngineSelectionViewModel(mockStatusService.Object, mockPlanService.Object));
}
```

### ✅ Phase 4: XAML UI実装（5-6日目）

#### XAML実装順序

1. **スタイルファイル作成**: TranslationSettingsStyles.axaml
2. **基本コントロール**: EngineSelectionControl.axaml
3. **言語設定**: LanguagePairSelectionControl.axaml  
4. **戦略設定**: TranslationStrategyControl.axaml
5. **状態表示**: EngineStatusControl.axaml
6. **メイン画面**: TranslationSettingsView.axaml

#### UI動作確認項目

- [ ] **データバインディング**: すべてのプロパティが正しくバインドされている
- [ ] **コマンドバインディング**: すべてのボタン・操作が正常動作する
- [ ] **レスポンシブデザイン**: ウィンドウサイズ変更に適切に対応する
- [ ] **アクセシビリティ**: キーボードナビゲーション・スクリーンリーダー対応

### ✅ Phase 5: 統合テスト（7日目）

#### 統合テストシナリオ

1. **基本フロー**
   - アプリ起動→設定画面表示→エンジン選択→保存

2. **エラーケース**
   - ネットワーク障害時の動作
   - 設定ファイル破損時の回復
   - 無効な設定値での動作

3. **パフォーマンス**
   - UI応答性（設定変更時の遅延）
   - メモリ使用量（長時間使用時）

## 🔧 よくある問題と解決策

### 1. DI関連問題

#### 問題: `Unable to resolve service for type 'ITranslationEngineStatusService'`

**原因**: サービス登録の順序または設定ミス

**解決策**:
```csharp
// Program.cs または App.xaml.cs で正しい順序で登録
services.AddSentencePieceTokenizer(configuration);           // 1. 基盤サービス
services.AddChineseTranslationSupport(configuration);       // 2. 翻訳サービス
services.AddCompleteTranslationServices(configuration);     // 3. 統合サービス
services.AddTranslationSettingsUI(configuration);          // 4. UI層サービス
```

#### 問題: Circular dependency detected

**原因**: ViewModel間の循環参照

**解決策**:
```csharp
// 直接参照ではなくイベント経由で通信
public class TranslationSettingsViewModel : ViewModelBase
{
    private readonly IEventAggregator _eventAggregator;
    
    // 循環参照を避けてイベント経由で通信
    private void OnEngineChanged()
    {
        _eventAggregator.PublishAsync(new EngineChangedEvent(selectedEngine));
    }
}
```

### 2. XAML バインディング問題

#### 問題: バインディングが動作しない

**チェック項目**:
- [ ] `x:DataType` 属性が正しく設定されている
- [ ] プロパティ名のタイポがない
- [ ] ViewModelが正しく設定されている

```xml
<!-- 正しいバインディング例 -->
<UserControl xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:DataType="vm:EngineSelectionViewModel">
    <TextBlock Text="{Binding SelectedEngineDescription}"/>
</UserControl>
```

#### 問題: コマンドが実行されない

**デバッグ方法**:
```csharp
// ViewModelでコマンドの実行可能性を確認
public ReactiveCommand<Unit, Unit> SaveCommand { get; }

public TranslationSettingsViewModel()
{
    var canExecute = this.WhenAnyValue(x => x.HasChanges);
    SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canExecute);
    
    // デバッグ: コマンドの実行可能性をログ出力
    SaveCommand.CanExecute.Subscribe(canExecute => 
        Debug.WriteLine($"SaveCommand CanExecute: {canExecute}"));
}
```

### 3. ReactiveUI 関連問題

#### 問題: `this.RaiseAndSetIfChanged` が動作しない

**確認点**:
- [ ] ViewModelが `ReactiveObject` を継承している
- [ ] プロパティが正しく実装されている

```csharp
// 正しい実装例
public class EngineSelectionViewModel : ReactiveObject
{
    private TranslationEngine _selectedEngine;
    
    public TranslationEngine SelectedEngine
    {
        get => _selectedEngine;
        set => this.RaiseAndSetIfChanged(ref _selectedEngine, value);
    }
}
```

#### 問題: WhenAnyValue が反応しない

**解決策**:
```csharp
// Disposableの適切な管理
public class EngineSelectionViewModel : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    
    public EngineSelectionViewModel()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.SelectedEngine)
                .Subscribe(UpdateDescription)
                .DisposeWith(disposables);
        });
    }
}
```

### 4. パフォーマンス問題

#### 問題: UI更新が遅い

**最適化策**:
```csharp
// 1. 更新頻度の調整
this.WhenAnyValue(x => x.SearchText)
    .Throttle(TimeSpan.FromMilliseconds(300))  // 300ms遅延
    .DistinctUntilChanged()                    // 同じ値の重複排除
    .ObserveOn(RxApp.MainThreadScheduler)      // UIスレッドで実行
    .Subscribe(ExecuteSearch);

// 2. バッチ更新
using (DelayChangeNotifications())
{
    Property1 = value1;
    Property2 = value2;
    Property3 = value3;
    // 1回だけ通知が発生
}
```

#### 問題: メモリリーク

**対策**:
```csharp
// IDisposable の適切な実装
public class EngineSelectionViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private bool _disposed = false;
    
    public EngineSelectionViewModel()
    {
        // サブスクリプションをCompositeDisposableに追加
        statusService.StatusUpdated
            .Subscribe(UpdateStatus)
            .DisposeWith(_disposables);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposables.Dispose();
            _disposed = true;
        }
    }
}
```

## 🧪 テスト実行方法

### 1. 単体テスト実行

```bash
# 特定のViewModelのテスト実行
dotnet test --filter "ClassName=EngineSelectionViewModelTests"

# すべてのUI関連テスト実行  
dotnet test --filter "Category=UI"

# カバレッジ付きテスト実行
dotnet test --collect:"XPlat Code Coverage"
```

### 2. 統合テスト実行

```bash
# 統合テストの実行
dotnet test --filter "Category=Integration"

# メモリリークテスト
dotnet test --filter "Category=MemoryLeak" --logger "console;verbosity=detailed"
```

### 3. 手動テストチェックリスト

#### 基本機能テスト
- [ ] アプリ起動: 設定画面が正常に表示される
- [ ] エンジン選択: LocalOnly/CloudOnly切り替えが正常動作
- [ ] 言語ペア: 各言語ペアの有効/無効切り替えが正常動作
- [ ] 中国語設定: 簡体字/繁体字切り替えが正常動作
- [ ] 戦略選択: Direct/TwoStage切り替えが正常動作
- [ ] 設定保存: 設定変更が正常に保存される
- [ ] 設定復元: アプリ再起動後に設定が復元される

#### エラーケーステスト
- [ ] ネットワーク切断: CloudOnlyエンジンでのフォールバック動作
- [ ] 設定ファイル削除: デフォルト設定での起動
- [ ] 無効な設定値: エラー回復とデフォルト値設定
- [ ] メモリ不足: 適切なエラーハンドリング

#### UI/UXテスト
- [ ] レスポンシブ: ウィンドウサイズ変更での適切な表示
- [ ] キーボード操作: Tab/Enter/Spaceでの操作
- [ ] 通知表示: 成功/エラー通知の適切な表示
- [ ] アクセシビリティ: スクリーンリーダーでの読み上げ

## 🚀 パフォーマンス最適化

### 1. 起動時間最適化

```csharp
// 遅延初期化の活用
public class TranslationSettingsViewModel : ViewModelBase
{
    private EngineStatusViewModel? _engineStatus;
    
    public EngineStatusViewModel EngineStatus => 
        _engineStatus ??= _serviceProvider.GetRequiredService<EngineStatusViewModel>();
}
```

### 2. メモリ使用量最適化

```csharp
// WeakReference の活用
public class EventAggregator : IEventAggregator
{
    private readonly Dictionary<Type, List<WeakReference<IEventHandler>>> _handlers = new();
    
    public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        var eventType = typeof(T);
        if (!_handlers.ContainsKey(eventType))
            _handlers[eventType] = new List<WeakReference<IEventHandler>>();
            
        _handlers[eventType].Add(new WeakReference<IEventHandler>(handler));
    }
}
```

### 3. UI更新最適化

```csharp
// バーチャル化とページング
public class LanguagePairSelectionViewModel : ViewModelBase
{
    private readonly ObservableCollection<LanguagePairItemViewModel> _allPairs;
    
    public ReadOnlyObservableCollection<LanguagePairItemViewModel> VisiblePairs { get; }
    
    public LanguagePairSelectionViewModel()
    {
        // フィルタリングされたコレクションのみ表示
        var filteredPairs = _allPairs
            .ToObservableChangeSet()
            .Filter(pair => pair.IsVisible)
            .ObserveOn(RxApp.MainThreadScheduler);
            
        filteredPairs.Bind(out var visiblePairs).Subscribe();
        VisiblePairs = visiblePairs;
    }
}
```

## 🔍 デバッグ手法

### 1. ログ設定

```csharp
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Baketa.UI": "Debug",
      "Baketa.UI.ViewModels": "Trace"
    }
  }
}
```

### 2. リアクティブストリームのデバッグ

```csharp
// Observable のログ出力
this.WhenAnyValue(x => x.SelectedEngine)
    .Do(engine => Debug.WriteLine($"Engine changed to: {engine}"))
    .Subscribe(UpdateEngineDescription);
```

### 3. メモリリークデバッグ

```csharp
// ファイナライザーでのリーク検出
public class EngineSelectionViewModel : ReactiveObject, IDisposable
{
    private bool _disposed = false;
    
    ~EngineSelectionViewModel()
    {
        if (!_disposed)
        {
            Debug.WriteLine("⚠️ EngineSelectionViewModel がDisposeされずに破棄されました");
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // リソース解放
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
```

## 📦 プロダクション展開準備

### 1. 設定ファイル最適化

```json
// appsettings.Production.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Baketa": "Information"
    }
  },
  "TranslationUI": {
    "EnableNotifications": true,
    "StatusUpdateInterval": 60000,
    "AutoSaveSettings": true
  }
}
```

### 2. エラー報告システム

```csharp
// グローバル例外ハンドラー
public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        // 未処理例外の報告
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            LogCriticalError(exception);
        };
        
        base.OnExit(e);
    }
}
```

### 3. パフォーマンス監視

```csharp
// パフォーマンスカウンターの設定
public class PerformanceMonitor : IDisposable
{
    private readonly PerformanceCounter _memoryCounter;
    private readonly Timer _monitoringTimer;
    
    public PerformanceMonitor()
    {
        _memoryCounter = new PerformanceCounter("Process", "Working Set", Process.GetCurrentProcess().ProcessName);
        _monitoringTimer = new Timer(LogPerformanceMetrics, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }
    
    private void LogPerformanceMetrics(object? state)
    {
        var memoryUsage = _memoryCounter.NextValue() / 1024 / 1024; // MB
        Debug.WriteLine($"Memory Usage: {memoryUsage:F2} MB");
    }
}
```

## ✅ 最終チェックリスト

### 機能完成度
- [ ] すべての ViewModels が正常に動作する
- [ ] すべての XAML バインディングが正常に動作する
- [ ] すべてのコマンドが適切に実行される
- [ ] 設定の保存・復元が正常に動作する
- [ ] エラー処理が適切に実装されている

### 品質保証
- [ ] 単体テストが90%以上のカバレッジを達成
- [ ] 統合テストがすべて成功
- [ ] メモリリークが発生しない
- [ ] UI応答性が保たれている（操作後200ms以内）
- [ ] アクセシビリティ要件を満たしている

### プロダクション準備
- [ ] ログレベルが本番環境用に設定されている
- [ ] 例外処理が本番環境で適切に動作する
- [ ] パフォーマンス監視が設定されている
- [ ] ユーザードキュメントが準備されている

---

このガイドに従って実装を進めることで、**1週間程度で完全に動作する翻訳設定UI**を構築できます。

*最終更新: 2025年6月2日*  
*実装準備: 完了 ✅*