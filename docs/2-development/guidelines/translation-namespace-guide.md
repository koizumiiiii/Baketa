# 翻訳モデル名前空間移行ガイド

*最終更新: 2025年5月18日*

## 1. 名前空間統一の背景

Baketaプロジェクトでは、翻訳関連のデータモデルが以下の2つの名前空間に分散していました：

- `Baketa.Core.Models.Translation`
- `Baketa.Core.Translation.Models`

この状況により、コード内での型の曖昧参照が発生し、名前空間エイリアスによる一時的な回避策を使用していました。名前空間統一プロジェクトでは、すべての翻訳関連モデルを一つの標準名前空間に統一し、コードの一貫性と保守性を向上させました。

## 2. 統一名前空間構造

翻訳関連のすべてのモデルとインターフェースは、以下の名前空間構造に統一されました：

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

## 3. 統一されたモデルクラス

以下のクラスが標準名前空間 `Baketa.Core.Translation.Models` に統一されました：

| 旧名前空間 | クラス名 | 新名前空間 |
|-------------|---------|------------|
| Baketa.Core.Models.Translation | TranslationRequest | Baketa.Core.Translation.Models |
| Baketa.Core.Models.Translation | TranslationResponse | Baketa.Core.Translation.Models |
| Baketa.Core.Models.Translation | Language | Baketa.Core.Translation.Models |
| Baketa.Core.Models.Translation | LanguagePair | Baketa.Core.Translation.Models |
| Baketa.Core.Models.Translation | TranslationError | Baketa.Core.Translation.Models |
| Baketa.Core.Models.Translation | TranslationErrorType | Baketa.Core.Translation.Models |

## 4. 主要な変更点

### 4.1 `Language` クラスの変更

**旧実装:**
```csharp
namespace Baketa.Core.Models.Translation
{
    public class Language
    {
        public string Code { get; set; }      // 言語コード (e.g. "en")
        public string RegionCode { get; set; } // 地域コード (e.g. "US")
        public string NativeName { get; set; } // ネイティブ言語名
        public bool IsAutoDetect { get; set; } // 自動検出フラグ
    }
}
```

**新実装:**
```csharp
namespace Baketa.Core.Translation.Models
{
    public class Language
    {
        public string Code { get; set; }         // 言語コード (e.g. "en-US")
        public string DisplayName { get; set; }  // 表示名
        public string NativeName { get; set; }   // ネイティブ言語名
        public bool IsAutoDetect { get; set; }   // 自動検出フラグ
        public bool IsRightToLeft { get; set; }  // 右から左への言語かどうか

        // 言語コードから新しいインスタンスを作成
        public static Language FromCode(string code)
        {
            // 実装
        }
    }
}
```

**主な変更点:**
- 地域コードの表現が `Code="zh", RegionCode="CN"` から `Code="zh-CN"` 形式に変更
- `IsRightToLeft` プロパティの追加
- `FromCode()` ファクトリメソッドの追加

### 4.2 `LanguagePair` クラスの変更

**旧実装:**
```csharp
namespace Baketa.Core.Models.Translation
{
    public class LanguagePair
    {
        public Language Source { get; set; }
        public Language Target { get; set; }

        public static LanguagePair Create(string sourceCode, string targetCode)
        {
            // 実装
        }
    }
}
```

**新実装:**
```csharp
namespace Baketa.Core.Translation.Models
{
    public class LanguagePair
    {
        public Language Source { get; set; }
        public Language Target { get; set; }

        public static LanguagePair Create(string sourceCode, string targetCode)
        {
            // 実装
        }

        public static LanguagePair FromString(string pairString)
        {
            // "en-US:ja-JP" 形式の文字列からペアを作成
        }

        public override bool Equals(object obj)
        {
            // 実装
        }

        public override int GetHashCode()
        {
            // 実装
        }
    }
}
```

**主な変更点:**
- `FromString()` メソッドの追加
- 適切な `Equals()` と `GetHashCode()` の実装

### 4.3 `TranslationRequest` クラスの変更

**旧実装:**
```csharp
namespace Baketa.Core.Models.Translation
{
    public class TranslationRequest
    {
        public string Text { get; set; }
        public LanguagePair Languages { get; set; }

        public static TranslationRequest Create(string text, LanguagePair languages)
        {
            // 実装
        }
    }
}
```

**新実装:**
```csharp
namespace Baketa.Core.Translation.Models
{
    public class TranslationRequest
    {
        public string Text { get; set; }
        public LanguagePair Languages { get; set; }
        public DateTime Timestamp { get; set; }
        public TranslationContext Context { get; set; }

        public static TranslationRequest Create(string text, LanguagePair languages)
        {
            // 実装
        }

        public static TranslationRequest CreateWithContext(
            string text, LanguagePair languages, TranslationContext context)
        {
            // 実装
        }

        public TranslationRequest Clone()
        {
            // 実装
        }

        public string GenerateCacheKey()
        {
            // キャッシュキー生成ロジック
        }
    }
}
```

**主な変更点:**
- `Timestamp` プロパティの追加
- `Context` プロパティの追加
- `Clone()` メソッドの追加
- `GenerateCacheKey()` メソッドの追加
- `CreateWithContext()` ファクトリメソッドの追加

## 5. 移行ガイド

### 5.1 コード更新

以下の手順に従ってコードを更新してください：

1. **名前空間参照の更新:**
   ```csharp
   // 旧参照
   using Baketa.Core.Models.Translation;
   
   // 新参照
   using Baketa.Core.Translation.Models;
   ```

2. **エイリアス定義の削除:**
   ```csharp
   // 削除対象
   using CoreModels = Baketa.Core.Models.Translation;
   using TransModels = Baketa.Core.Translation.Models;
   ```

3. **言語コード形式の更新:**
   ```csharp
   // 旧形式
   var language = new Language { Code = "zh", RegionCode = "CN" };
   
   // 新形式
   var language = new Language { Code = "zh-CN" };
   ```

### 5.2 自動テスト更新

テストコードを更新する際は、以下の点に注意してください：

1. 言語コードの形式が変更されたため、テストデータを更新
2. `Language` クラスのプロパティアクセスを新しい構造に合わせて更新
3. 新しいプロパティやメソッドを利用したテストケースの追加

### 5.3 移行時の注意点

1. `Language` オブジェクトの作成時は `FromCode()` メソッドの使用を検討
2. 翻訳リクエストの作成には `Create()` または `CreateWithContext()` ファクトリメソッドを使用
3. キャッシュキーが変更されているため、キャッシュシステムを利用している場合は注意

## 6. 新規実装のベストプラクティス

### 6.1 クラス設計のガイドライン

1. **不変オブジェクトの推奨:**
   ```csharp
   // 推奨パターン
   public class TranslationEntity
   {
       public TranslationEntity(string text, LanguagePair languages)
       {
           Text = text;
           Languages = languages;
           Timestamp = DateTime.UtcNow;
       }

       public string Text { get; }
       public LanguagePair Languages { get; }
       public DateTime Timestamp { get; }
   }
   ```

2. **ファクトリメソッドの活用:**
   ```csharp
   // 推奨パターン
   public static class TranslationFactory
   {
       public static TranslationRequest Create(string text, string sourceCode, string targetCode)
       {
           var languages = LanguagePair.Create(sourceCode, targetCode);
           return TranslationRequest.Create(text, languages);
       }
   }
   ```

3. **拡張メソッドの活用:**
   ```csharp
   // 推奨パターン
   public static class TranslationExtensions
   {
       public static TranslationResponse AsSuccessResponse(this TranslationRequest request, string translatedText)
       {
           return TranslationResponse.CreateSuccess(request, translatedText);
       }
   }
   ```

### 6.2 コーディングスタイル

1. **プロパティ初期化子の活用:**
   ```csharp
   // 推奨パターン
   var request = new TranslationRequest
   {
       Text = "Hello, world!",
       Languages = LanguagePair.Create("en", "ja"),
       Context = new TranslationContext
       {
           ApplicationName = "TestApp",
           RequestId = Guid.NewGuid()
       }
   };
   ```

2. **null 許容参照型の活用:**
   ```csharp
   // 推奨パターン
   public class TranslationMetadata
   {
       public string? Category { get; set; }
       public string? Domain { get; set; }
       public required string RequestOrigin { get; set; }
   }
   ```

## 7. まとめ

名前空間統一プロジェクトの完了により、翻訳関連のデータモデルがすべて `Baketa.Core.Translation.Models` 名前空間に統一されました。これにより、コードの一貫性と保守性が向上し、名前空間エイリアスが不要になりました。また、言語コードの表現が標準化され、新しい機能が追加されました。

今後の開発では、このガイドに記載されたベストプラクティスに従うことで、一貫性のあるコードベースを維持できます。

---

*注: このガイドは名前空間統一プロジェクトの完了に伴い作成されました。今後の仕様変更により内容が更新される可能性があります。*