# Issue #57 イベントシステム修正レポート

## 1. 概要

Issue #57「メインウィンドウUIデザインの実装」の対応過程で、イベントシステムに関する複数の問題が発見されました。特に、Core層とUI層の間でのイベントシステムの不整合があり、これを解決するために統合的なアプローチを採用しました。

## 2. 発見された問題点

### 2.1 イベントシステムの二重実装

- Core層（Baketa.Core.Events）とUI層（Baketa.UI.Framework.Events）それぞれに独立したイベントシステムが実装されていた
- 両方のシステムで同じインターフェース名（IEvent）が使用されていた
- イベントクラスが両方の名前空間で重複して定義されていた

### 2.2 型の不一致

- Core層のIEventはId, Name, Categoryプロパティを持つ
- UI層のIEventはEventId, Timestampプロパティを持つ
- 互換性がないため型変換エラーが発生

### 2.3 ReactiveCommandFactoryの重複

- `Baketa.UI.Framework.ReactiveCommandFactory`と`Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory`が重複して存在

### 2.4 Avalonia UIのAPIの変更

- `ControlAutomationPeer.ControlType`が存在せず、`AutomationProperties.SetControlType`が代わりに使用される

## 3. 実施した修正

### 3.1 イベントシステムの統合

1. **UIEventBaseの導入**
   - Core層のEventBaseを継承し、UI層のIEventインターフェースも実装
   - EventIdプロパティをIdにマッピングすることで互換性を確保

2. **IEventの階層化**
   - UI層のIEventをCore層のIEventの継承として定義

3. **イベント集約器の統一**
   - 型制約をBaketa.Core.Events.IEventに統一
   - 名前空間エイリアスを使用して明示的に区別

```csharp
using CoreEvents = Baketa.Core.Events;
using UIEvents = Baketa.UI.Framework.Events;
```

### 3.2 重複イベントの整理

1. **名前空間の整理**
   - TranslationEvents.csから重複イベントを削除
   - NavigationEvents.csのイベントをUIEventBaseを継承するように修正

2. **イベント実装の統一**
   - すべてのイベントクラスでName/Categoryプロパティをオーバーライド
   - UIイベントはUIEventBaseを継承するように変更

### 3.3 ReactiveCommandFactoryの整理

- `Baketa.UI.Framework.ReactiveCommandFactory`を非推奨として空実装
- 全ての参照を`Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory`に変更

### 3.4 AccessibilityHelperの修正

- `WithAutomationControlType`メソッドを`AutomationProperties.SetControlType`を使用するように修正

## 4. 修正の影響範囲

### 4.1 コード変更量
- 10以上のファイルに修正
- 主にUI層のビューモデルとイベント関連クラス

### 4.2 アーキテクチャへの影響
- イベントシステムが統一され、CoreとUIの間で一貫性のある使用が可能に
- 将来的にはUIイベントの一部をCoreイベントとして再設計する余地がある

## 5. ドキュメント整備

- `E:\dev\Baketa\docs\3-architecture\events\event-system-integration.md`を作成
  - イベントシステムの統合アプローチの説明
  - 実装例と標準パターン
  - 既存コードの移行ガイドライン
  - よくある問題とその解決策

## 6. 残存する課題

1. **イベントの役割の整理**
   - CoreとUIのイベントの責任境界の明確化

2. **イベントハンドラーの標準化**
   - イベントハンドラーの命名と実装パターンの統一

3. **テスト環境の整備**
   - イベントシステムのユニットテストの強化

## 7. 推奨される対応

1. イベントバスの設計見直しを検討（将来的なリファクタリング課題）
2. イベントハンドラーの登録/解除パターンの標準化
3. イベントのカテゴリと命名規則の文書化
4. Issue #60での「テーマと国際化対応の基盤実装」で本格的なイベント活用を検討