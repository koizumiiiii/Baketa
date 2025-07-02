# ReactiveUI実装ガイド - Baketaプロジェクト

## 1. 概要

このドキュメントは、Baketaプロジェクトにおける[ReactiveUI](https://www.reactiveui.net/)フレームワークの使用方法、推奨パターン、および注意点について説明します。ReactiveUIを活用したMVVM（Model-View-ViewModel）パターンの実装方法と、バージョン互換性の考慮事項を含みます。

## 2. 使用バージョン情報

| パッケージ | バージョン | 説明 |
|------------|------------|------|
| ReactiveUI | 20.1.63 | コアライブラリ |
| ReactiveUI.Fody | 19.5.41 | コード生成サポート |
| ReactiveUI.Validation | 4.1.1 | 検証フレームワーク |
| Avalonia.ReactiveUI | 11.2.7 | Avalonia UI連携 |

## 3. 基本的な実装パターン

### 3.1 ビューモデルの実装

```csharp
// 推奨：ViewModelBaseを継承
public class SampleViewModel : ViewModelBase
{
    // Fodyを使用したプロパティ定義
    [Reactive] public string Name { get; set; } = string.Empty;
    
    // コマンド定義
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    
    // コンストラクタ
    public SampleViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
        : base(eventAggregator, logger)
    {
        // バリデーションルールの設定
        this.ValidationRule(
            vm => vm.Name,
            name => !string.IsNullOrWhiteSpace(name),
            "名前は必須項目です");
            
        // コマンドの作成
        SaveCommand = ReactiveCommandFactory.Create(ExecuteSaveAsync);
    }
    
    // コマンド実行メソッド
    private async Task ExecuteSaveAsync()
    {
        // 実装...
    }
}
```

### 3.2 ビューの実装

```csharp
// 推奨：ReactiveUserControlまたはReactiveWindowを継承
public partial class SampleView : ReactiveUserControl<SampleViewModel>
{
    public SampleView()
    {
        InitializeComponent();
        
        this.WhenActivated(disposables =>
        {
            // 一方向バインディング
            this.OneWayBind(ViewModel, vm => vm.Title, v => v.TitleTextBlock.Text)
                .DisposeWith(disposables);
                
            // 双方向バインディング
            this.TwoWayBind(ViewModel, vm => vm.Name, v => v.NameTextBox.Text)
                .DisposeWith(disposables);
                
            // コマンドバインディング
            this.BindCommand(ViewModel, vm => vm.SaveCommand, v => v.SaveButton)
                .DisposeWith(disposables);
                
            // バリデーションバインディング
            this.BindValidation(ViewModel, vm => vm.Name, v => v.NameErrorTextBlock.Text)
                .DisposeWith(disposables);
        });
    }
}
```

## 4. モダンC#機能の活用

### 4.1 プライマリコンストラクターの使用

C# 12以降では、プライマリコンストラクターを使用して簡潔なクラス定義が可能です：

```csharp
// 従来の方法
internal class SampleHandler : IObserver<Exception>
{
    private readonly ILogger? _logger;

    public SampleHandler(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    // メソッド実装...
}

// プライマリコンストラクターを使用した方法
internal class SampleHandler(ILogger? logger = null) : IObserver<Exception>
{
    private readonly ILogger? _logger = logger;
    
    // メソッド実装...
}
```

### 4.2 コレクション初期化の最適化

コレクションを初期化する際は、適切な初期容量を指定することでパフォーマンスを向上できます：

```csharp
// 良くない例
var items = new List<string>();  // サイズ変更が頻繁に発生する可能性

// 良い例
var items = new List<string>(10);  // 適切な初期容量を指定
```

ラムダ式では`static`修飾子を使用してキャプチャを避けることもパフォーマンス向上に効果的です：

```csharp
// 改善前
var handlers = _dictionary.GetOrAdd(key, _ => new List<object>());

// 改善後
var handlers = _dictionary.GetOrAdd(key, static _ => new List<object>(5));
```

## 5. バージョン互換性の考慮事項

### 5.1 IScreen インターフェースの変更

ReactiveUI 20.xではIScreenインターフェースが簡略化され、Routerプロパティのみとなりました。以前のバージョンにあったNavigateやNavigateBackプロパティは削除されています。

```csharp
// ReactiveUI 20.x での正しい実装
public class ScreenAdapter : ReactiveObject, IScreen
{
    private readonly RoutingState _routingState;
    
    public ScreenAdapter(RoutingState routingState)
    {
        _routingState = routingState ?? throw new ArgumentNullException(nameof(routingState));
    }
    
    // IScreenは現在このプロパティのみ
    public RoutingState Router => _routingState;
}
```

### 5.2 ValidationContext の取り扱い

ValidationContextの内部実装に直接依存せず、リフレクションを使用するか、公開APIのみを使用してください。

```csharp
// 推奨アプローチ
public static IEnumerable<string> GetErrorMessages<TViewModel, TProperty>(
    this ReactiveValidationObject validationObject,
    Expression<Func<TViewModel, TProperty>> propertyExpression)
{
    // プロパティ名を取得
    string propertyName = GetPropertyName(propertyExpression);
    
    // リフレクションを使用して安全にエラーを取得
    try
    {
        var context = validationObject.ValidationContext;
        // リフレクション実装...
    }
    catch (Exception)
    {
        return Enumerable.Empty<string>();
    }
}
```

### 5.3 コマンドバインディング

CommandBindingMixinsに依存せず、独自の実装を提供することでバージョン互換性を確保します。

```csharp
// 推奨アプローチ
public static IDisposable BindCommand<TView, TViewModel, TControl>(
    this TView view,
    Expression<Func<TView, TControl>> controlSelector,
    Expression<Func<TViewModel, ReactiveCommand<Unit, Unit>>> command)
    where TView : class, IViewFor<TViewModel>
    where TViewModel : class
    where TControl : class, ICommand
{
    // リフレクションベースの実装...
}
```

## 6. 推奨プラクティス

1. **WhenActivatedの使用**: ビューのアクティベーションライフサイクルを管理するため、必ずWhenActivatedを使用する
2. **DisposeWithの使用**: リソースリークを防ぐため、バインディング結果をDisposeWithで管理する
3. **リフレクションの最小化**: パフォーマンスのため、頻繁に呼び出される部分ではリフレクションの使用を避ける
4. **内部APIへの依存回避**: ReactiveUIの内部APIではなく公開APIのみに依存する
5. **バージョン互換性テスト**: バージョンアップグレード時は互換性テストを実施する
6. **パラメーター名の適切な管理**: 未使用パラメーターは破棄パラメーター(`_`)を使用して明示する
7. **Null参照チェックの徹底**: `ArgumentNullException.ThrowIfNull`を使用して早期にNullチェックを行う

## 7. よくある問題と解決策

### 7.1 型変換エラー

**問題**: `type X を type Y に暗黙的に変換できません`

**解決策**:
- 明示的なキャストを使用する
- インターフェースの定義を確認し、最新バージョンと互換性があるか確認する
- アダプターパターンを使用して互換性レイヤーを提供する

### 7.2 メソッド参照エラー

**問題**: `型または名前空間の名前 'X' が名前空間 'Y' に存在しません`

**解決策**:
- 対象のクラスやメソッドが現在のバージョンに存在するか確認する
- APIリファレンスで正しい名前空間を確認する
- 代替のアプローチを検討する

### 7.3 バインディングエラー

**問題**: バインディングが機能しない、または例外が発生する

**解決策**:
- デバッグモードを有効にして詳細な情報を確認する
- バインディングパスの型一致を確認する
- リフレクションベースの代替実装を検討する

### 7.4 Exceptionのデフォルトメッセージ

**問題**: テストで空の例外メッセージを期待しているがデフォルトメッセージが返される

**解決策**:
```csharp
// 問題のあるコード
public MyException() : base() { }  // デフォルトメッセージは空ではない

// 修正済みコード
public MyException() : base("") { }  // 空文字列を明示的に指定
```

## 8. 参考リソース

- [ReactiveUI公式ドキュメント](https://www.reactiveui.net/docs/)
- [ReactiveUI GitHub](https://github.com/reactiveui/ReactiveUI)
- [ReactiveUI.Validation GitHub](https://github.com/reactiveui/ReactiveUI.Validation)
- [ReactiveUi.Fody GitHub](https://github.com/kswoll/ReactiveUI.Fody)
- [バージョン互換性の詳細](./reactiveui-version-compatibility.md)

## 9. 更新履歴

- 2025-04-27: 初版作成
- 2025-04-28: モダンC#機能の活用、よくある問題を追加
- バージョン変更時に随時更新予定
