# Avalonia UI移行計画

## 1. 移行概要

Baketaプロジェクトの UIフレームワークをWPFからAvalonia UIに移行する計画です。この文書では、移行の背景、技術選定、実装計画について詳述します。

## 2. 背景と目的

### 2.1 WPFからの移行理由

- **WPFの将来性**: WPFはMicrosoftにより「保守モード」として位置づけられており、積極的な機能追加は期待できない
- **クロスプラットフォーム**: 将来的なアーキテクチャの整理と拡張性向上
- **最新技術の利用**: モダンなMVVMパターンとリアクティブプログラミングの活用
- **特殊機能の実装**: オーバーレイ、クリックスルー、透過ウィンドウなどの実装が容易
- **パフォーマンス**: 描画エンジンの最適化による軽量な実装

### 2.2 期待される利点

- **メンテナンス性の向上**: モダンなアーキテクチャと技術スタックによる保守性向上
- **開発効率の向上**: リアクティブプログラミングによるコード量の削減
- **ユーザー体験の向上**: パフォーマンスと見た目の両面での改善
- **将来的な拡張性**: プラグイン可能なアーキテクチャ

## 3. 技術スタック詳細

### 3.1 コアフレームワーク

- **Avalonia UI** (v11.2.6)
  - 最新のXAMLベースUIフレームワーク
  - JetBrainsによる商業的サポート

- **ReactiveUI** (v19.5.41)
  - リアクティブプログラミングに基づいたMVVMフレームワーク
  - .NET Foundationプロジェクト
  - `WhenAnyValue`や`ObservableAsPropertyHelper`による効率的なバインディング

- **Microsoft.Extensions.DependencyInjection** (v9.0.3)
  - .NET標準の依存性注入コンテナ
  - モジュール間の疎結合性を実現

### 3.2 パッケージ選定とリスク評価

#### コアパッケージ (低リスク)
- **Avalonia** - 活発な開発・JetBrainsサポート
- **Avalonia.Desktop** - コア機能の一部
- **Avalonia.ReactiveUI** - .NET Foundationプロジェクト
- **Avalonia.Diagnostics** - 開発時のデバッグ支援
- **ReactiveUI.Fody** - ReactiveUIの機能拡張

#### 拡張パッケージ (中-高リスク)
| パッケージ | サポート状況 | リスク | 代替手段 |
|------------|--------------|--------|----------|
| Teast.Controls.GroupBox | 安定版 | 低 | 独自GroupBox実装 |
| OxyPlot.Avalonia | 継続的更新 | 低 | 独自グラフ実装 |

### 3.3 アーキテクチャと設計パターン

- **MVVMパターン**: View、ViewModel、Modelの明確な分離
- **依存性注入**: サービスとビューモデルの生成と提供
- **イベント集約**: ViewModel間の疎結合なメッセージング
- **複数ウィンドウアプローチ**: 各機能（メイン画面、設定、オーバーレイ）を独立したウィンドウとして実装

## 4. 実装計画と優先順位

### 4.1 フェーズ1: 基盤準備
- **Avalonia UIプロジェクト構造のセットアップ**
  - Avalonia UIアプリケーションプロジェクト作成
  - 必要なNuGetパッケージの導入
  - フォルダ構造の設計・作成
  - ベーステーマとスタイルの設定

### 4.2 フェーズ2: MVVMフレームワーク構築
- **Avalonia MVVMフレームワーク基盤の実装**
  - ViewModelBaseクラスの実装
  - ReactiveUIとの統合
  - コマンドバインディングの実装
  - 依存性注入の設定
  - サービス抽象化レイヤーの実装

### 4.3 フェーズ3: メインUI実装
- **Avalonia メインウィンドウとシステムトレイ実装**
  - メインウィンドウUI
  - システムトレイアイコンと機能
  - ナビゲーションシステム
  - 基本的なユーザー操作フロー

### 4.4 フェーズ4: オーバーレイUI実装
- **Avalonia オーバーレイウィンドウの実装**
  - 透過ウィンドウの実装
  - クリックスルー機能
  - ドラッグ機能
  - 翻訳テキスト表示UI

### 4.5 フェーズ5: 設定UI実装
- **Avalonia 設定画面の実装**
  - カテゴリ別設定UI
  - 設定の保存・読み込み
  - 設定変更の即時反映
  - リセット機能の実装

## 5. 移行計画と実装

BaketaプロジェクトのすべてのUIコンポーネントをAvalonia UIで再実装し、既存のWPF UIプロジェクトをソリューションから削除する計画です。

### 5.1 Avalonia UI実装の優先順位

1. コアUIコンポーネントの実装
   - ViewModelBaseクラス
   - Window基本クラス
   - 共通コントロール

2. メイン機能の実装
   - メインウィンドウとナビゲーション
   - システムトレイ機能
   - 基本設定の管理

3. オーバーレイの実装
   - 透過ウィンドウ
   - クリックスルー機能
   - テキスト表示と位置調整

4. 高度機能の実装
   - 詳細設定UI
   - テスト・診断機能
   - パフォーマンス最適化

## 6. 移行中に予想される問題と対策

### 6.1 ReactiveUI関連の問題

**想定される問題**: 
- `WhenAnyValue().Subscribe()`メソッドの使用時に型変換エラーが発生
- `ラムダ式はデリゲート型ではないため、'IObserver<T>' 型に変換できません`というエラー

**対策**:
- 標準的なC#の`PropertyChanged`イベントハンドラーを使用

```csharp
// 問題のあるReactiveUIコード
this.WhenAnyValue(x => x.SomeProperty)
    .Subscribe(value => DoSomething(value));

// 代替実装
this.PropertyChanged += (sender, args) => {
    if (args.PropertyName == nameof(SomeProperty))
    {
        DoSomething(SomeProperty);
    }
};
```

### 6.2 GroupBoxコントロールの問題

**想定される問題**:
- Avalonia UIに標準のGroupBoxコントロールが存在しない
- `Unable to resolve type GroupBox from namespace https://github.com/avaloniaui`というエラー

**対策**:
- `Teast.Controls.GroupBox`パッケージをインストール
- 各XAMLファイルで名前空間を追加し、GroupBoxの参照を`controls:GroupBox`に変更

```xml
xmlns:controls="using:Teast.Controls"
...
<controls:GroupBox Header="設定グループ">
    <!-- コンテンツ -->
</controls:GroupBox>
```

### 6.3 コンパイル済みバインディングの問題

**想定される問題**:
- `Cannot parse a compiled binding without an explicit x:DataType directive`エラー
- `Unable to find suitable setter or adder for property Items`エラー

**対策**:
- `x:DataType`属性を追加
- Items属性を ItemsSource に変更（Avaloniaの推奨方法）

```xml
<!-- 修正前 -->
<ComboBox Items="{Binding AvailableLanguages}">

<!-- 修正後 -->
<ComboBox ItemsSource="{Binding AvailableLanguages}">
```

## 7. モダンC#機能の活用

Avalonia UIへの移行に伴い、C# 12の新機能も積極的に活用します：

### 7.1 コレクション初期化の簡素化

```csharp
// 旧式
private readonly List<string> _items = new List<string>();

// 新式
private readonly List<string> _items = [];
```

### 7.2 プライマリコンストラクタ

```csharp
// 旧式
public class SettingsCategoryViewModelBase : ViewModelBase
{
    protected readonly ISettingsService _settingsService;
    
    public SettingsCategoryViewModelBase(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }
}

// 新式
public class SettingsCategoryViewModelBase(ISettingsService settingsService) : ViewModelBase
{
    protected readonly ISettingsService _settingsService = settingsService;
}
```

## 8. 今後の拡張計画

Avalonia UIへの移行は、以下の点について今後さらに改善を進める予定です：

1. **パフォーマンスの最適化**:
   - 大量のデータを表示する場合の仮想化対応
   - バインディング最適化によるメモリ使用量削減

2. **テーマシステムの拡張**:
   - ユーザー定義テーマのサポート
   - アクセシビリティ対応の強化

3. **未実装機能の追加**:
   - オーバーレイのさらなるカスタマイズオプション

## 9. 参考資料と学習リソース

- [Avalonia UI公式ドキュメント](https://docs.avaloniaui.net/)
- [ReactiveUI公式ドキュメント](https://www.reactiveui.net/docs/)
- [Avalonia UIサンプル集](https://github.com/AvaloniaUI/Avalonia.Samples)
- [ReactiveUI in Action (Book)](https://www.manning.com/books/reactive-applications-with-net)