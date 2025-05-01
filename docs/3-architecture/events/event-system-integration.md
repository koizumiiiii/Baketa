# イベントシステム統合ガイド

## 1. 概要

Baketaプロジェクトでは、当初別々に実装されていた2つのイベントシステム（Core層とUI層）を統合する設計変更を行いました。このドキュメントでは、イベントシステムの統合アプローチとその実装方法について説明します。

## 2. 統合の背景

当初、BaketaプロジェクトにはUIレイヤー（Baketa.UI.Framework.Events）とCore層（Baketa.Core.Events）に個別のイベントシステムがあり、以下の問題が発生していました：

- 名前空間の衝突（同じIEventインターフェース名を使用）
- イベントクラスの重複実装
- 互換性のない実装による型変換エラー
- コード修正時の依存関係の追跡の困難さ

## 3. 採用したアプローチ

これらの問題を解決するため、以下のアプローチで両イベントシステムを統合しました：

1. **Core層のIEventを基本とする階層構造の採用**
   - Baketa.Core.Events.IEventを基底インターフェースとする
   - Baketa.UI.Framework.Events.IEventはCore層のIEventを継承

2. **UIEventBaseの導入**
   - UIイベント用の共通基底クラスとしてUIEventBaseを作成
   - Core層のEventBaseを継承し、UI層のIEventも実装
   - 従来の`EventId`と新しい`Id`の互換性を確保

3. **名前空間エイリアスの活用**
   - 名前空間衝突を避けるためにエイリアスを使用
   ```csharp
   using CoreEvents = Baketa.Core.Events;
   using UIEvents = Baketa.UI.Framework.Events;
   ```

## 4. UIEventBase 実装例

```csharp
namespace Baketa.UI.Framework.Events
{
    /// <summary>
    /// UI関連イベントの基底クラス
    /// </summary>
    internal abstract class UIEventBase : Baketa.Core.Events.EventBase, IEvent
    {
        /// <summary>
        /// イベントの一意な識別子 (コンパチビリティ用)
        /// </summary>
        public Guid EventId => Id;
    }
}
```

## 5. イベント実装の標準パターン

### 5.1 Core層イベント

```csharp
namespace Baketa.Core.Events
{
    /// <summary>
    /// イベント説明
    /// </summary>
    public class SampleEvent : EventBase
    {
        /// <summary>
        /// イベントプロパティ
        /// </summary>
        public string SomeProperty { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SampleEvent(string someProperty)
        {
            SomeProperty = someProperty;
        }

        /// <inheritdoc/>
        public override string Name => "SampleEvent";

        /// <inheritdoc/>
        public override string Category => "SampleCategory";
    }
}
```

### 5.2 UI層イベント

```csharp
namespace Baketa.UI.Framework.Events
{
    /// <summary>
    /// イベント説明
    /// </summary>
    internal class UISampleEvent : UIEventBase
    {
        /// <summary>
        /// イベントプロパティ
        /// </summary>
        public string SomeProperty { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public UISampleEvent(string someProperty)
        {
            SomeProperty = someProperty;
        }

        /// <inheritdoc/>
        public override string Name => "UISampleEvent";

        /// <inheritdoc/>
        public override string Category => "UI.SampleCategory";
    }
}
```

## 6. イベントハンドリング実装例

### 6.1 イベント購読

```csharp
// Core層イベントの購読
SubscribeToEvent<CoreEvents.SampleEvent>(async eventData => 
{
    // イベント処理
    await Task.CompletedTask.ConfigureAwait(false);
});

// UI層イベントの購読
SubscribeToEvent<UIEvents.UISampleEvent>(async eventData => 
{
    // イベント処理
    await Task.CompletedTask.ConfigureAwait(false);
});
```

### 6.2 イベント発行

```csharp
// Core層イベントの発行
await _eventAggregator.PublishAsync(new CoreEvents.SampleEvent("value")).ConfigureAwait(false);

// UI層イベントの発行
await _eventAggregator.PublishAsync(new UIEvents.UISampleEvent("value")).ConfigureAwait(false);
```

## 7. 既存コードの移行ガイドライン

既存のイベントコードを新しい統合アプローチに移行する際の手順：

1. **名前空間エイリアスの追加**
   ```csharp
   using CoreEvents = Baketa.Core.Events;
   using UIEvents = Baketa.UI.Framework.Events;
   ```

2. **イベントの型変更**
   - IEvent → UIEventBase（UI層）または EventBase（Core層）
   - EventId/Timestampの削除（基底クラスが提供）
   - Name/Categoryプロパティの追加（オーバーライド）

3. **イベント集約器の使用**
   - 型の明示的な指定: UIEvents.IEventAggregator または CoreEvents.IEventAggregator
   - 参照の曖昧さを常に解消

## 8. 注意点

- **名前の衝突回避**: 同じ名前のイベントクラスを異なる名前空間に定義しない
- **名前空間の一貫性**: 適切な名前空間に配置し、UIとCoreの境界を明確に
- **インターフェース型制約**: ジェネリックメソッドの型制約はCore.IEventを使用

## 9. よくある問題とその解決策

### 9.1 「型 'XEvent' はジェネリック型またはメソッド内で型パラメーター 'TEvent' として使用できません」

**解決策**:
- イベントクラスがUIEventBaseまたはEventBaseを継承しているか確認
- メソッドの型制約がBaketa.Core.Events.IEventになっているか確認

### 9.2 「'IEventAggregator' は、'Baketa.UI.Framework.Events.IEventAggregator' と 'Baketa.Core.Events.IEventAggregator' 間のあいまいな参照です」

**解決策**:
- 名前空間エイリアスを使用して明示的に指定
  ```csharp
  UIEvents.IEventAggregator または CoreEvents.IEventAggregator
  ```

### 9.3 「メソッド 'PublishAsync<TEvent>' の型パラメーター 'TEvent' に対する制約はインターフェース メソッドと一致しなければなりません」

**解決策**:
- インターフェース実装とメソッド実装の型制約を合わせる
  ```csharp
  where TEvent : Baketa.Core.Events.IEvent
  ```