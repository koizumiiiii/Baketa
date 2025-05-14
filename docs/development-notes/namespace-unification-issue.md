# 翻訳モデルの名前空間統一

## 概要

現在、翻訳関連のデータモデルが2つの異なる名前空間に分散しています：

- `Baketa.Core.Models.Translation`
- `Baketa.Core.Translation.Models`

これにより、コード内で型の曖昧参照が発生し、名前空間エイリアスによる一時的な回避策を使用している状態です。この問題を根本的に解決するため、すべての翻訳関連モデルを一つの標準名前空間に統一します。

## 目標

- すべての翻訳関連モデルを `Baketa.Core.Translation.Models` に統一
- 重複するクラス定義を削除
- 型参照の曖昧さを完全に排除
- 名前空間エイリアスを使用しなくても良いクリーンな状態を実現

## 影響範囲

以下のコンポーネントが影響を受けます：

1. 翻訳エンジンインターフェース (`ITranslationEngine`)
2. 翻訳基底クラス (`TranslationEngineBase`)
3. 翻訳エンジン実装クラス
4. 翻訳キャッシュシステム
5. 翻訳結果管理システム
6. 翻訳イベントシステム

## 移行対象のクラス

以下のクラスが主な移行対象となります：

| 現在の名前空間 | クラス名 |
|-------------|---------|
| Baketa.Core.Models.Translation | TranslationRequest |
| Baketa.Core.Models.Translation | TranslationResponse |
| Baketa.Core.Models.Translation | Language |
| Baketa.Core.Models.Translation | LanguagePair |
| Baketa.Core.Models.Translation | TranslationError |
| Baketa.Core.Translation.Models | TranslationContext |
| Baketa.Core.Translation.Models | TranslationCacheEntry |
| Baketa.Core.Translation.Models | TranslationManagementOptions |
| Baketa.Core.Translation.Models | Rectangle
| Baketa.Core.Translation.Models | CacheStatistics
| Baketa.Core.Translation.Models | TranslationRecord
| Baketa.Core.Translation.Models | TranslationSearchQuery
| Baketa.Core.Translation.Models | TranslationStatistics
| Baketa.Core.Translation.Models | StatisticsOptions
| Baketa.Core.Translation.Models | CacheClearOptions
| Baketa.Core.Translation.Models | MergeStrategy

## 実装手順

1. **準備フェーズ**
   - 移行対象の全クラスと参照箇所を特定
   - 移行計画の詳細を文書化
   - 必要なテストケースの作成

2. **移行フェーズ**
   - 標準名前空間を `Baketa.Core.Translation.Models` に決定
   - `Baketa.Core.Models.Translation` に存在するクラスを標準名前空間に移植・統合
   - 重複クラスの削除と参照の修正
   - 名前空間エイリアスの削除

3. **検証フェーズ**
   - 単体テストの実行
   - 統合テストの実行
   - コンパイルエラーがないことの確認
   - 実行時のエラーがないことの確認

4. **完了フェーズ**
   - ドキュメントの更新
   - 開発ガイドラインの更新

## 移行手順の詳細

### 1. インターフェースの更新

```csharp
// 旧インターフェース参照
using Baketa.Core.Models.Translation;

// 新インターフェース参照
using Baketa.Core.Translation.Models;
```

### 2. モデルクラスの統合

```csharp
// 移行対象クラス（Baketa.Core.Models.Translation → Baketa.Core.Translation.Models）
public class TranslationRequest { ... }
public class TranslationResponse { ... }
public class Language { ... }
public class LanguagePair { ... }
public class TranslationError { ... }
```

### 3. 依存関係の修正

```csharp
// 名前空間エイリアス削除前
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;

public Task<CoreModels.TranslationResponse> TranslateAsync(
    CoreModels.TranslationRequest request)
{ ... }

// 名前空間エイリアス削除後
using Baketa.Core.Translation.Models;

public Task<TranslationResponse> TranslateAsync(
    TranslationRequest request)
{ ... }
```

## 実施タイミング

**実装中の機能（#63, #64, #65, #53）が完了した後に実施します。**

理由：
- 現在進行中の実装の中断を避ける
- 名前空間エイリアスによる一時的な対応で現状問題なく開発を進められる
- 機能実装完了後にまとめてリファクタリングする方がリスクが少ない
- テスト済みコードに対してリファクタリングを行う方が安全

## 注意点

- 名前空間の変更は広範囲にわたるため、慎重に実施する必要があります
- すべての参照箇所を漏れなく更新するために、検索ツールや静的解析ツールを活用します
- 変更前後でのテスト結果を比較し、挙動の変化がないことを確認します
- プルリクエストを小さな単位に分割し、レビューを容易にします

## 参考資料

- [新名前空間構造 開発者ガイド](E:\dev\Baketa\docs\2-development\guidelines\new-namespace-guide.md)
- [名前空間移行ガイドライン](E:\dev\Baketa\docs\2-development\guidelines\namespace-migration.md)
- [翻訳基盤実装ノート](E:\dev\Baketa\docs\development-notes\translation-implementation-notes.md)
