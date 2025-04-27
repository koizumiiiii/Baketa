# ReactiveUI バージョン互換性ガイド

## 概要

このドキュメントは、ReactiveUIのバージョン間の互換性に関する情報と、バージョンアップグレード時の注意点をまとめたものです。Baketaプロジェクトでは、ReactiveUI 20.1.63を使用していますが、将来的なバージョンアップに備えて、互換性の問題が発生しやすい箇所とその対策を記録しています。

## バージョン比較表

| 機能/インターフェース | ReactiveUI 15.x (旧) | ReactiveUI 20.x (現行) | 変更点 |
|----------------------|-------------------|---------------------|-------|
| IScreen              | Router, Navigate, NavigateBack のプロパティを含む | Router プロパティのみ | NavigateとNavigateBackがRoutingStateに移動 |
| RoutingState         | 基本的なルーティング機能 | 拡張されたルーティング機能 | より多くの機能がRoutingStateに移動 |
| ReactiveCommand      | 単純なコマンド型 | TInput, TOutput型パラメータ | ジェネリック型パラメータの変更 |
| CommandBindingMixins | 外部からアクセス可能 | 内部実装またはリファクタリング | 直接参照が困難に |
| ValidationContext    | 一部の実装が異なる | 安定した実装 | 内部実装の変更 |

## 互換性の問題と対応策

### 1. IScreen インターフェース

**問題**: ReactiveUI 20.xでは、IScreenインターフェースがRouterプロパティのみとなり、NavigateとNavigateBackプロパティが削除されました。

**対応策**:
- ScreenAdapterを使用してIScreenを実装
- RoutingStateの機能を直接使用
- 明示的なインターフェース実装を避ける

### 2. コマンドバインディング

**問題**: CommandBindingMixinsクラスへの直接参照が困難になりました。

**対応策**:
- リフレクションベースの独自実装を提供
- 標準的なプロパティバインディングを使用
- コマンドプロパティを直接設定

### 3. バリデーション

**問題**: ReactiveUI.ValidationのAPIは比較的安定していますが、内部実装が変更される可能性があります。

**対応策**:
- ValidationContextの内部実装に直接依存しない
- リフレクションを使用して柔軟な実装を提供
- 公開APIのみを使用

### 4. ビューアクティベーション

**問題**: ビューアクティベーションの動作が変更される可能性があります。

**対応策**:
- WhenActivatedパターンを一貫して使用
- DisposeWithを使用してリソースを管理
- アクティベーションテストを実装

## バージョンアップグレードのチェックリスト

バージョンアップグレードを実施する際は、以下の項目を確認してください：

1. **依存関係の確認**:
   - ReactiveUI
   - ReactiveUI.Fody
   - ReactiveUI.Validation
   - Avalonia.ReactiveUI

2. **互換性テスト**:
   - IScreen実装のテスト
   - コマンドバインディングのテスト
   - バリデーションのテスト
   - イベントアグリゲーションのテスト

3. **API変更の確認**:
   - 公式リリースノートの確認
   - 非推奨APIの特定
   - 削除されたAPIの代替方法の確認

4. **リファクタリング計画**:
   - 非推奨APIの使用箇所を特定
   - 代替実装の優先順位付け
   - 段階的なリファクタリング計画の立案

## 互換性維持の設計パターン

### 1. アダプターパターン

APIが変更された場合に、新旧バージョン間の互換性を保つためのアダプタークラスを提供します。

```csharp
// 例: IScreenアダプター
public class ScreenAdapter : ReactiveObject, IScreen
{
    private readonly RoutingState _routingState;
    
    public ScreenAdapter(RoutingState routingState)
    {
        _routingState = routingState;
    }
    
    public RoutingState Router => _routingState;
}
```

### 2. ファサードパターン

複雑な実装を隠蔽し、シンプルなインターフェースを提供します。

```csharp
// 例: バリデーションファサード
public static class ValidationFacade
{
    public static IEnumerable<string> GetErrors<TViewModel, TProperty>(
        this ReactiveValidationObject validationObject,
        Expression<Func<TViewModel, TProperty>> propertyExpression)
    {
        // 実装の詳細を隠蔽
    }
}
```

### 3. 抽象化レイヤー

ReactiveUIへの直接依存を減らし、独自の抽象化レイヤーを提供します。

```csharp
// 例: コマンドファクトリー
public static class ReactiveCommandFactory
{
    public static ReactiveCommand<Unit, Unit> Create(Func<Task> execute)
    {
        // バージョン間の違いを吸収
        return ReactiveCommand.CreateFromTask(execute);
    }
}
```

## Issue56で得られた教訓

Issue56（ReactiveUIベースのMVVMフレームワーク実装）の対応過程で、以下の教訓が得られました：

1. **モダンC#構文の活用**:
   - プライマリコンストラクターは、ボイラープレートコードを大幅に削減
   - パターンマッチングで条件分岐をより読みやすく

2. **パフォーマンス最適化**:
   - コレクション初期化時の適切な初期容量指定
   - ラムダ式の`static`修飾子による不要なキャプチャの回避

3. **Null参照安全性**:
   - `ArgumentNullException.ThrowIfNull`の一貫した使用
   - Null条件演算子と合わせた安全なアクセス

4. **例外処理の注意点**:
   - 基底クラスの`Exception()`コンストラクターは空の文字列ではなく既定のメッセージを設定する
   - 明示的に`base("")`を使用して空メッセージを設定する必要がある
   - これはユニットテストなどで期待値との不一致を引き起こす可能性がある

## 将来の検討事項

1. **代替MVVMフレームワークの調査**:
   - CommunityToolkit.Mvvm
   - Prism
   - MvvmCross

2. **クロスプラットフォーム対応の検討**:
   - 他のUIフレームワークとの互換性
   - 再利用可能なビューモデルの設計

3. **テスト戦略の強化**:
   - ビューモデルの単体テスト
   - バインディングテスト
   - 互換性テスト

## 参考リソース

- ReactiveUI GitHub: [https://github.com/reactiveui/ReactiveUI](https://github.com/reactiveui/ReactiveUI)
- ReactiveUI リリースノート: [https://github.com/reactiveui/ReactiveUI/releases](https://github.com/reactiveui/ReactiveUI/releases)
- ReactiveUI.Validation GitHub: [https://github.com/reactiveui/ReactiveUI.Validation](https://github.com/reactiveui/ReactiveUI.Validation)

---

作成日: 2025-04-28  
最終更新日: 2025-04-28  
担当者: Baketa開発チーム
