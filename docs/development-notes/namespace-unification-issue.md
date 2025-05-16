# 翻訳モデルの名前空間統一計画

## 1. 概要

現在、翻訳関連のデータモデルが2つの異なる名前空間に分散しています：

- `Baketa.Core.Models.Translation`
- `Baketa.Core.Translation.Models`

これにより、コード内で型の曖昧参照が発生し、名前空間エイリアスによる一時的な回避策を使用している状態です。この問題を根本的に解決するため、すべての翻訳関連モデルを一つの標準名前空間に統一します。

## 2. 目標

- すべての翻訳関連モデルを `Baketa.Core.Translation.Models` に統一
- 重複するクラス定義を削除
- 型参照の曖昧さを完全に排除
- 名前空間エイリアスを使用しなくても良いクリーンな状態を実現

## 3. 影響範囲

以下のコンポーネントが影響を受けます：

1. 翻訳エンジンインターフェース (`ITranslationEngine`)
2. 翻訳基底クラス (`TranslationEngineBase`)
3. 翻訳エンジン実装クラス
4. 翻訳キャッシュシステム
5. 翻訳結果管理システム
6. 翻訳イベントシステム

## 4. 移行対象のクラス

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
| Baketa.Core.Translation.Models | Rectangle |
| Baketa.Core.Translation.Models | CacheStatistics |
| Baketa.Core.Translation.Models | TranslationRecord |
| Baketa.Core.Translation.Models | TranslationSearchQuery |
| Baketa.Core.Translation.Models | TranslationStatistics |
| Baketa.Core.Translation.Models | StatisticsOptions |
| Baketa.Core.Translation.Models | CacheClearOptions |
| Baketa.Core.Translation.Models | MergeStrategy |

## 5. 目標名前空間構造

翻訳関連のすべてのモデルとインターフェースを以下の名前空間構造に統一します：

```
Baketa.Core.Translation                 // ルート名前空間
├── Baketa.Core.Translation.Abstractions // 基本インターフェース定義
├── Baketa.Core.Translation.Models       // モデル定義
│   ├── Common                           // 共通モデル
│   ├── Requests                         // リクエスト関連
│   ├── Responses                        // レスポンス関連
│   └── Events                           // イベント関連
├── Baketa.Core.Translation.Services     // サービス実装
└── Baketa.Core.Translation.Common       // 共通ユーティリティ
```

## 6. 移行計画

### 6.1 フェーズ1: 準備と一時的な対応（現在）

- 移行対象の全クラスと参照箇所を特定
- 移行計画の詳細を文書化
- 名前空間エイリアスによる一時的な対応
  ```csharp
  using CoreModels = Baketa.Core.Models.Translation;
  using TransModels = Baketa.Core.Translation.Models;
  ```
- モデル間の変換ユーティリティ作成

### 6.2 フェーズ2: 新規実装の標準化（2週間以内）

- 新規のモデル定義はすべて目標名前空間に追加
- 重複している型の不要なプロパティを特定・削除
- インターフェース定義の統一（ITranslationEngine、ITranslationManager）

### 6.3 フェーズ3: コードベースの段階的移行（1ヶ月以内）

- ファイル単位での名前空間参照の一括更新
- 各コンポーネントのテストによる機能検証
- 互換レイヤーの導入によるスムーズな移行

### 6.4 フェーズ4: 廃止と最終化（3ヶ月以内）

- 廃止予定名前空間に`Obsolete`属性を付加
- すべての依存関係の最終確認と修正
- 最終テストとドキュメント更新
- 廃止予定名前空間の削除

## 7. 標準モデル定義

移行後のモデル定義例を以下に示します。これを標準として使用します：

### 7.1 Language（言語）モデル

```csharp
namespace Baketa.Core.Translation.Models
{
    public class Language
    {
        // 必須プロパティ
        public required string Code { get; set; }
        public required string Name { get; set; } 
        public required string DisplayName { get; set; }
        
        // 省略可能プロパティ
        public string? LocalName { get; set; }
        public bool IsRightToLeft { get; set; }
        
        // 静的ヘルパーインスタンス
        public static Language Auto => new() { 
            Code = "auto", Name = "Auto", DisplayName = "Auto-detect" 
        };
        
        public static Language Unknown => new() { 
            Code = "und", Name = "Unknown", DisplayName = "Unknown Language" 
        };
    }
}
```

### 7.2 TranslationError（翻訳エラー）モデル

```csharp
namespace Baketa.Core.Translation.Models
{
    public class TranslationError
    {
        // 必須プロパティ
        public required string ErrorCode { get; set; }
        public required string Message { get; set; }
        
        // 省略可能プロパティ
        public TranslationErrorType ErrorType { get; set; } = TranslationErrorType.Unknown;
        public string? Details { get; set; }
        public Exception? Exception { get; set; }
        public bool IsRetryable { get; set; }
    }

    public enum TranslationErrorType
    {
        Unknown = 0,
        Network = 1,
        Authentication = 2,
        QuotaExceeded = 3,
        Engine = 4,
        UnsupportedLanguage = 5,
        InvalidInput = 6,
        Timeout = 7,
        Exception = 8
    }
}
```

## 8. リスクと緩和策

### 8.1 リスク

1. **既存機能の損失**: 名前空間変更によるバグ混入
2. **開発の遅延**: 移行中の混乱による生産性低下
3. **テスト不足**: 参照変更によるすべてのケースのテスト困難

### 8.2 緩和策

1. **段階的移行**: 一度にすべての変更を行わず、モジュール単位で移行
2. **自動テスト強化**: 移行前に単体テストとシナリオテストの充実
3. **インターフェース安定**: 公開APIは変更せず、内部実装のみ修正
4. **ドキュメント更新**: 移行ガイドの提供と開発者間の情報共有

## 9. 実施タイミング

**実装中の機能（#63, #64, #65, #53）が完了した後に実施します。**

理由：
- 現在進行中の実装の中断を避ける
- 名前空間エイリアスによる一時的な対応で現状問題なく開発を進められる
- 機能実装完了後にまとめてリファクタリングする方がリスクが少ない
- テスト済みコードに対してリファクタリングを行う方が安全

## 10. 注意点

- 名前空間の変更は広範囲にわたるため、慎重に実施する必要があります
- すべての参照箇所を漏れなく更新するために、検索ツールや静的解析ツールを活用します
- 変更前後でのテスト結果を比較し、挙動の変化がないことを確認します
- プルリクエストを小さな単位に分割し、レビューを容易にします

## 11. 名前空間設計原則

今後の開発では以下の原則を守り、名前空間の混乱を防止します：

1. **一貫性**: 機能ごとに単一の名前空間を使用
2. **命名規則**: 
   - Models: データモデル
   - Events: イベント定義
   - Services: 具体的な実装
   - Abstractions: インターフェース定義

## 12. 参考資料

- [翻訳実装エラー修正ガイド](E:\dev\Baketa\docs\development-notes\translation-implementation-error-fix-guide.md)
- [名前空間移行ガイドライン](E:\dev\Baketa\docs\2-development\guidelines\namespace-migration.md)
- [翻訳基盤実装ノート](E:\dev\Baketa\docs\development-notes\translation-implementation-notes.md)
