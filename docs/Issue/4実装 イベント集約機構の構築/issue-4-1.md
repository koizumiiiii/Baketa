# 実装: イベント関連インターフェースの設計と実装

## 概要
イベント集約機構の基盤となる基本インターフェースを設計・実装します。

## 目的・理由
イベント集約システムの中核となるインターフェースを適切に設計することで、型安全で拡張性の高いイベント処理基盤を確立します。これにより、モジュール間の疎結合なコミュニケーションが可能になり、アプリケーション全体の保守性と拡張性が向上します。

## 詳細
- `IEvent`インターフェースの設計と実装
- `IEventHandler<TEvent>`インターフェースの設計と実装
- `IEventAggregator`インターフェースの設計と実装
- インターフェース階層とジェネリック設計の最適化

## タスク分解
- [ ] `IEvent`基本インターフェースの設計と実装
  - [ ] イベントID（Guid）プロパティの定義
  - [ ] タイムスタンププロパティの定義
  - [ ] 必要に応じたメタデータプロパティの定義
- [ ] `IEventHandler<TEvent>`インターフェースの設計と実装
  - [ ] ジェネリック型パラメータの制約設定
  - [ ] 非同期ハンドラーメソッドの定義
  - [ ] プライオリティ制御の検討
- [ ] `IEventAggregator`インターフェースの設計と実装
  - [ ] イベント発行メソッドの定義
  - [ ] ハンドラー登録メソッドの定義
  - [ ] ハンドラー登録解除メソッドの定義
  - [ ] 条件付き購読機能の検討
- [ ] インターフェースドキュメンテーションの作成
- [ ] サンプル実装の作成

## インターフェース設計案
```csharp
/// <summary>
/// 基本イベントインターフェース
/// </summary>
public interface IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    Guid Id { get; }
    
    /// <summary>
    /// イベント発生時刻
    /// </summary>
    DateTime Timestamp { get; }
}

/// <summary>
/// イベントハンドラインターフェース
/// </summary>
/// <typeparam name="TEvent">イベント型</typeparam>
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    /// <summary>
    /// イベント処理
    /// </summary>
    /// <param name="event">イベント</param>
    Task HandleAsync(TEvent @event);
}

/// <summary>
/// イベント集約インターフェース
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// イベントの発行
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="event">イベント</param>
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
    
    /// <summary>
    /// イベントハンドラの登録
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="handler">ハンドラ</param>
    void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    
    /// <summary>
    /// イベントハンドラの登録解除
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="handler">ハンドラ</param>
    void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
}
```

## 関連Issue/参考
- 親Issue: #4 実装: イベント集約機構の構築
- 関連: #1.4 改善: その他のコアインターフェースの移行
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (7. イベント集約機構の設計)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.3 Task.Runの適切な使用)

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
