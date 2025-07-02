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
- [x] `IEvent`基本インターフェースの設計と実装
  - [x] イベントID（Guid）プロパティの定義
  - [x] タイムスタンププロパティの定義
  - [x] メタデータプロパティ（Name、Category）の定義
- [x] `IEventHandler<TEvent>`インターフェースの設計と実装
  - [x] ジェネリック型パラメータの制約設定
  - [x] 非同期ハンドラーメソッドの定義
  - [x] 戻り値のTask型の付加
- [x] `IEventAggregator`インターフェースの設計と実装
  - [x] イベント発行メソッドの定義
  - [x] ハンドラー登録メソッドの定義
  - [x] ハンドラー登録解除メソッドの定義
- [x] `EventBase`基本実装クラスの作成
- [x] インターフェースドキュメンテーションの作成

## インターフェース設計案
```csharp
namespace Baketa.Core.Events
{
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
        
        /// <summary>
        /// イベント名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        string Category { get; }
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
        /// <returns>処理の完了を表すTask</returns>
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
        /// <returns>イベント発行の完了を表すTask</returns>
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
    
    /// <summary>
    /// イベント基本実装
    /// </summary>
    public abstract class EventBase : IEvent
    {
        /// <summary>
        /// コンストラクタ
        /// </summary>
        protected EventBase()
        {
            Id = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
        }
        
        /// <inheritdoc />
        public Guid Id { get; }
        
        /// <inheritdoc />
        public DateTime Timestamp { get; }
        
        /// <inheritdoc />
        public abstract string Name { get; }
        
        /// <inheritdoc />
        public abstract string Category { get; }
    }
}
```

## 関連Issue/参考
- 親Issue: #4 実装: イベント集約機構の構築
- 関連: #1.4 改善: その他のコアインターフェースの移行
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (7. イベント集約機構の設計)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.3 Task.Runの適切な使用)

## 実装済みファイル
- `E:\dev\Baketa\Baketa.Core\Events\IEvent.cs`
- `E:\dev\Baketa\Baketa.Core\Events\IEventHandler.cs`
- `E:\dev\Baketa\Baketa.Core\Events\IEventAggregator.cs`
- `E:\dev\Baketa\Baketa.Core\Events\EventBase.cs`

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: feature`
- `priority: high`
- `component: core`