# 改善: IImage関連インターフェースの移行と拡張

## 概要
ゲーム画面キャプチャの基盤となる`IImage`関連インターフェースを新名前空間に移行し、機能拡張します。

## 目的・理由
`IImage`インターフェースはOCRやキャプチャ機能の基盤となる重要なコンポーネントです。移行と同時に機能拡張を行うことで、今後の開発をスムーズに進めるための土台を整備します。

## 詳細
- `IImage`インターフェースを`Baketa.Core.Abstractions.Imaging`名前空間へ移行
- 画像処理機能を強化した拡張インターフェースの追加
- プラットフォーム固有画像実装との連携を強化

## タスク分解
- [ ] 基本`IImage`インターフェースの移行
- [ ] `IAdvancedImage`インターフェースの設計と実装
- [ ] 画像変換用`IImageConverter<TSource, TTarget>`インターフェースの設計
- [ ] 画像処理用`IImageProcessor`インターフェースの設計
- [ ] 画像ファクトリーインターフェース`IImageFactory`の移行と拡張
- [ ] 旧インターフェースの非推奨化と互換性維持

## 関連Issue/参考
- 親Issue: #1 改善: 新インターフェース構造への移行
- 関連: #5 実装: 画像処理抽象化レイヤーの拡張
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (5.1 画像処理インターフェース階層)
- 参照: E:\dev\Baketa\docs\3-architecture\core\image-abstraction.md
- 参照: 現在の`Baketa.Core.Interfaces.IImage`実装

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: refactor`
- `priority: high`
- `component: core`
