# SentencePiece統合実装

## 概要

BaketaプロジェクトにおけるSentencePieceトークナイザーの実装です。Microsoft.ML.Tokenizers (v0.21.0-preview) を使用して、OPUS-MT翻訳モデルのためのトークン化機能を提供します。

## 実装済みコンポーネント

### 1. RealSentencePieceTokenizer
- **場所**: `RealSentencePieceTokenizer.cs`
- **機能**: Microsoft.ML.TokenizersのSentencePieceTokenizerをラップし、ITokenizerインターフェースを実装
- **特徴**:
  - NFKC正規化サポート
  - 特殊トークン管理
  - 最大入力長制限
  - 詳細なログ記録

### 2. SentencePieceModelManager
- **場所**: `SentencePieceModelManager.cs`
- **機能**: モデルファイルの自動ダウンロードとキャッシュ管理
- **特徴**:
  - プログレス付きダウンロード
  - SHA256チェックサム検証
  - メタデータによるバージョン管理
  - 自動クリーンアップ機能

### 3. 設定クラス
- **SentencePieceOptions**: 設定オプション
- **ModelMetadata**: モデルメタデータ
- **TokenizationException**: カスタム例外
- **SpecialTokens**: 特殊トークン情報

### 4. DI統合
- **SentencePieceServiceCollectionExtensions**: サービス登録拡張メソッド
- **SentencePieceTokenizerFactory**: トークナイザー作成ファクトリ

## 使用方法

### 1. 設定（appsettings.json）

```json
{
  "SentencePiece": {
    "ModelsDirectory": "Models/SentencePiece",
    "DefaultModel": "opus-mt-ja-en",
    "DownloadUrl": "https://your-storage.blob.core.windows.net/models/{0}.model",
    "ModelCacheDays": 30,
    "MaxDownloadRetries": 3,
    "DownloadTimeoutMinutes": 5,
    "MaxInputLength": 10000,
    "EnableChecksumValidation": true,
    "EnableAutoCleanup": true,
    "CleanupThresholdDays": 90
  }
}
```

### 2. DI登録（Program.cs）

```csharp
// 設定ファイルから読み込み
services.AddSentencePieceTokenizer(configuration);

// または、カスタム設定
services.AddSentencePieceTokenizer(options =>
{
    options.ModelsDirectory = "CustomModels";
    options.DefaultModel = "my-model";
});

// 名前付きトークナイザー
services.AddNamedSentencePieceTokenizer("japanese", "opus-mt-ja-en", configuration);
```

### 3. 使用例

```csharp
public class TranslationService
{
    private readonly ITokenizer _tokenizer;
    
    public TranslationService(ITokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }
    
    public async Task<string> TranslateAsync(string text)
    {
        // トークン化
        var tokens = _tokenizer.Tokenize(text);
        
        // 翻訳処理...
        
        // デコード
        var result = _tokenizer.Decode(outputTokens);
        return result;
    }
}
```

## モデルファイル

### 必要なファイル
- `{model-name}.model`: SentencePieceモデルファイル
- `{model-name}.metadata.json`: 自動生成されるメタデータ

### モデル配置方法
1. 手動配置: `ModelsDirectory`に直接配置
2. 自動ダウンロード: `DownloadUrl`を設定して自動取得

## 移行ガイド

### 暫定実装からの移行

1. **OpusMtOnnxEngine**での使用:
```csharp
// 旧: 暫定実装
var tokenizer = new SentencePieceTokenizer(path, name, logger);

// 新: ファクトリ経由
var tokenizer = SentencePieceTokenizerFactory.Create(path, name, loggerFactory);
```

2. **DIコンテナ**での使用:
```csharp
// 旧: 暫定実装の直接登録
services.AddSingleton<ITokenizer, SentencePieceTokenizer>();

// 新: 拡張メソッド使用
services.AddSentencePieceTokenizer(configuration);
```

## テスト

### 単体テスト（予定）
- RealSentencePieceTokenizerTest
- SentencePieceModelManagerTest
- TokenizationExceptionTest

### 統合テスト（予定）
- OpusMtOnnxEngineとの統合
- エンドツーエンド翻訳テスト

## パフォーマンス

### ベンチマーク項目
- トークン化速度: 目標 50ms/文以下
- メモリ使用量: 目標 500MB以下
- 精度: Python版との一致率 99.9%以上

## トラブルシューティング

### エラー: "モデルに<unk>トークンが定義されていません"
- **原因**: モデルファイルに必須の特殊トークンが含まれていない
- **対策**: OPUS-MT互換のモデルファイルを使用

### エラー: "入力テキストが最大長を超えています"
- **原因**: MaxInputLength設定を超える長さのテキスト
- **対策**: 設定値を調整するか、テキストを分割

### エラー: "モデルのダウンロードに失敗しました"
- **原因**: ネットワーク接続またはURL設定の問題
- **対策**: DownloadUrlの確認、プロキシ設定の確認

## 今後の予定

1. **OPUS-MTモデル統合**
   - 実際のモデルファイルの配置
   - モデルダウンロードサーバーの設定

2. **テスト実装**
   - 単体テストの作成
   - パフォーマンステストの実施

3. **最適化**
   - キャッシュ機構の実装
   - バッチ処理の最適化

## 参考資料

- [Microsoft.ML.Tokenizers ドキュメント](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.tokenizers)
- [SentencePiece GitHub](https://github.com/google/sentencepiece)
- [OPUS-MT モデル](https://github.com/Helsinki-NLP/Opus-MT)

---

*作成日: 2025年5月23日*
