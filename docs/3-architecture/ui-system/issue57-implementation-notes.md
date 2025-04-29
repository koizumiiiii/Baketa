# Issue #57 実装ノート - メインウィンドウUIデザイン

## 1. 概要

本ドキュメントは、Baketa プロジェクトのIssue #57「メインウィンドウUIデザインの実装」に関する実装知見と重要なポイントをまとめたものです。実装中に発生した問題とその解決策に焦点を当てており、将来的な開発やメンテナンスの参考となることを目的としています。

## 2. Avalonia UI のスタイル定義と参照方法

### 2.1 スタイル定義の種類と適切な参照方法

Avalonia UIでは、スタイル定義の方法によって参照方法が異なります。この違いを理解せずに参照すると、ビルドエラーが発生します。

| 定義方法 | 参照方法 | 使用例 |
|----------|----------|--------|
| `<Styles>` をルートとする | `<StyleInclude>` | ボタン、コントロールなどのスタイル |
| `<ResourceDictionary>` をルートとする | `<ResourceInclude>` | 色、アイコン、タイポグラフィなど |

### 2.2 実装例

```xml
<!-- スタイル（Styles）定義の参照 -->
<Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://Baketa.UI/Styles/Buttons.axaml" />
    <StyleInclude Source="avares://Baketa.UI/Styles/Controls.axaml" />
</Application.Styles>

<!-- リソースディクショナリ定義の参照 -->
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="avares://Baketa.UI/Styles/Colors.axaml" />
            <ResourceInclude Source="avares://Baketa.UI/Styles/Icons.axaml" />
            <ResourceInclude Source="avares://Baketa.UI/Styles/Typography.axaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### 2.3 エラー事例

誤った参照方法を使用した場合、以下のようなビルドエラーが発生します：

```
エラー AVLN2000: Resource "avares://Baketa.UI/Styles/Typography.axaml" is defined as "Avalonia.Controls.ResourceDictionary" type in the "Baketa.UI" assembly, but expected "Avalonia.Styling.IStyle".
エラー AVLN2000: Resource "avares://Baketa.UI/Styles/Buttons.axaml" is defined as "Avalonia.Styling.Styles" type in the "Baketa.UI" assembly, but expected "Avalonia.Controls.IResourceDictionary".
```

## 3. DataTemplate 内のコマンドバインディング手法

### 3.1 問題の概要

ListBoxのItemTemplateなどのDataTemplate内でコマンドバインディングを行う場合、単純なバインディング方法では機能しないことがあります。これは、DataTemplate内の要素がメインのVisual Treeとは異なるコンテキストで生成されるためです。

### 3.2 解決パターン

以下の3つの解決パターンを使用できます：

#### パターン1: 親要素へのバインディング (非推奨)

```xml
<Button Command="{Binding $parent[UserControl].DataContext.RemoveItemCommand}"
        CommandParameter="{Binding}" />
```

この方法は簡単ですが、一部のシナリオで機能しないことがあります。

#### パターン2: RelativeSource を使用したバインディング

```xml
<Button Command="{Binding DataContext.RemoveItemCommand, 
                 RelativeSource={RelativeSource FindAncestor, AncestorType=UserControl}}"
        CommandParameter="{Binding}" />
```

より確実な方法ですが、複雑なネストがある場合は失敗することがあります。

#### パターン3: イベント委託パターン (最も確実)

XAML:
```xml
<Button x:Name="RemoveButton"
        CommandParameter="{Binding}"
        Classes="secondary"
        FontSize="10"
        Padding="6,2"
        VerticalAlignment="Top"/>
```

コードビハインド:
```csharp
// ListBoxを名前で参照可能にする
<ListBox x:Name="HistoryListBox"... />

// WhenActivatedで設定
this.WhenActivated(disposables => 
{
    // ボタンクリックイベントをハンドリング
    HistoryListBox.AddHandler(Button.ClickEvent, 
        new EventHandler<RoutedEventArgs>(OnRemoveButtonClick), 
        handledEventsToo: true);
});

// イベントハンドラー
private void OnRemoveButtonClick(object? sender, RoutedEventArgs e)
{
    // クリックされたボタンを取得
    if (e.Source is Button button && button.Name == "RemoveButton")
    {
        // コマンドパラメータを取得
        if (button.CommandParameter is TranslationHistoryItem item && ViewModel != null)
        {
            // ViewModelのコマンドを実行
            ViewModel.RemoveItemCommand.Execute(item).Subscribe();
        }
        
        // イベント処理済みとしてマーク
        e.Handled = true;
    }
}
```

このパターンが最も確実で、複雑なテンプレート構造でも動作します。

## 4. Visual Studio デザイナープレビューのエラー

### 4.1 発生するエラー

Avalonia UI プロジェクトでは、以下のようなデザイナープレビューのエラーが発生することがあります：

```
Error creating XAML completion metadata
System.IO.FileNotFoundException: ファイルまたはアセンブリ 'Baketa.UI.dll'、...
```

### 4.2 対応方法

このエラーはVisual StudioのXAMLデザイナー（プレビュー）機能のみに影響し、実際のアプリケーションのビルドや実行には影響しません。以下の点を理解しておくことが重要です：

- デザイナープレビューと実際のビルドでは、参照解決のメカニズムが異なる
- テストプロジェクトでは特にこの問題が発生しやすい
- 実際の動作確認は、アプリケーションをビルド・実行して行うべき

このエラーは無視して開発を続行しても問題ありません。

## 5. 実装上の留意点

1. **スタイルの一貫性確保**
   - 定義したスタイルが一貫して適用されるよう、定期的なテストが必要です
   - 特にテーマカラーやコントロールスタイルは、すべてのビューで統一感があるか確認

2. **イベント処理のパフォーマンス**
   - イベント委託パターンを使用する場合、不要なイベント処理を避けるために適切な条件チェックを行う
   - `e.Handled = true` でイベントバブリングを適切に制御

3. **コードビハインドとViewModelの責務分離**
   - UI関連の処理はコードビハインドで
   - ビジネスロジックはViewModelで
   - この原則を維持することで、テストやメンテナンスが容易になります

## 6. 参考リソース

- [Avalonia UI公式ドキュメント - スタイリング](https://docs.avaloniaui.net/docs/styling)
- [ReactiveUI公式ドキュメント - コマンド](https://www.reactiveui.net/docs/handbook/commands/)
- [Baketaプロジェクト - ReactiveUI実装ガイド](./reactiveui-guide.md)
