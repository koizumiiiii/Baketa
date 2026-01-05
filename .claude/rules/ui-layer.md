---
description: Baketa.UI レイヤー固有のルール（Avalonia UI, ReactiveUI）
globs:
  - "Baketa.UI/**/*.cs"
  - "Baketa.UI/**/*.axaml"
---

# Baketa.UI レイヤールール

## ReactiveUI パターン

### ViewModel 実装
```csharp
// 推奨: [Reactive] 属性で変更通知を自動化
[Reactive] public string MyProperty { get; set; }

// 推奨: WhenActivated + DisposeWith でライフサイクル管理
this.WhenActivated(disposables =>
{
    Observable.Interval(TimeSpan.FromSeconds(1))
        .Subscribe(_ => DoSomething())
        .DisposeWith(disposables);
});
```

### 禁止パターン
- `DispatcherTimer` の直接使用 → `Observable.Interval` を使用
- ViewModel で `Dispatcher.UIThread` を直接呼び出し → ReactiveUI のスケジューラを使用
- `INotifyPropertyChanged` の手動実装 → `[Reactive]` を使用

## Avalonia UI 固有

### XAML ルール
- `x:DataType` を必ず指定（コンパイル時バインディング）
- `Binding` より `CompiledBinding` を優先
- アニメーションは `TransitioningContentControl` や `Animation` を活用

### リソース管理
- 文字列は `Strings.resx` / `Strings.en.resx` に定義
- ハードコード文字列は禁止

## 依存関係
- Core, Application への依存は許可
- Infrastructure への直接依存は禁止（DI経由のみ）
