# OPUS-MT モデル配置・実行手順

## 📋 概要

このガイドでは、BaketaプロジェクトでOPUS-MTモデルを使用するためのSentencePieceモデルファイルの取得と配置手順を説明します。

## 🚀 自動実行（推奨）

### 1. モデルファイルの自動ダウンロード

```powershell
# PowerShellを管理者権限で開き、プロジェクトルートに移動
cd E:\dev\Baketa

# ダウンロードスクリプトを実行
.\scripts\download_opus_mt_models.ps1

# 強制再ダウンロード（必要な場合）
.\scripts\download_opus_mt_models.ps1 -Force

# 詳細ログ付き実行
.\scripts\download_opus_mt_models.ps1 -Verbose
```

### 2. モデルファイルの検証

```powershell
# モデルファイルの整合性チェック
.\scripts\verify_opus_mt_models.ps1

# 詳細情報付きで検証
.\scripts\verify_opus_mt_models.ps1 -Detailed
```

### 3. 統合テストの実行

```powershell
# 基本テスト実行
.\scripts\run_sentencepiece_tests.ps1

# 全テスト（パフォーマンステスト含む）
.\scripts\run_sentencepiece_tests.ps1 -RunPerformanceTests

# 詳細ログ付き実行
.\scripts\run_sentencepiece_tests.ps1 -Verbose
```

## 🛠️ 手動実行

### 1. ディレクトリ作成

```bash
mkdir -p Models/SentencePiece
```

### 2. モデルファイルの手動ダウンロード

以下のファイルをHuggingFaceから直接ダウンロード：

| モデル | URL | 保存先 |
|--------|-----|--------|
| 日本語→英語 | https://huggingface.co/Helsinki-NLP/opus-mt-ja-en/resolve/main/tokenizer.model | `Models/SentencePiece/opus-mt-ja-en.model` |
| 英語→日本語 | https://huggingface.co/Helsinki-NLP/opus-mt-en-ja/resolve/main/tokenizer.model | `Models/SentencePiece/opus-mt-en-ja.model` |
| 中国語→英語 | https://huggingface.co/Helsinki-NLP/opus-mt-zh-en/resolve/main/tokenizer.model | `Models/SentencePiece/opus-mt-zh-en.model` |
| 英語→中国語 | https://huggingface.co/Helsinki-NLP/opus-mt-en-zh/resolve/main/tokenizer.model | `Models/SentencePiece/opus-mt-en-zh.model` |

**PowerShellでの手動ダウンロード例:**
```powershell
# TLS 1.2 有効化
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# 日本語→英語モデル
Invoke-WebRequest -Uri "https://huggingface.co/Helsinki-NLP/opus-mt-ja-en/resolve/main/tokenizer.model" -OutFile "Models/SentencePiece/opus-mt-ja-en.model"

# 英語→日本語モデル
Invoke-WebRequest -Uri "https://huggingface.co/Helsinki-NLP/opus-mt-en-ja/resolve/main/tokenizer.model" -OutFile "Models/SentencePiece/opus-mt-en-ja.model"
```

### 3. ファイル確認

```powershell
# ファイルサイズと存在チェック
Get-ChildItem Models/SentencePiece/*.model | Format-Table Name, Length, LastWriteTime
```

## ⚙️ 設定ファイルの更新

### appsettings.json に以下の設定を追加

```json
{
  "SentencePiece": {
    "ModelsDirectory": "Models/SentencePiece",
    "DefaultModel": "opus-mt-ja-en",
    "MaxInputLength": 10000,
    "EnableChecksumValidation": true
  },
  "Translation": {
    "DefaultEngine": "OPUS-MT",
    "LanguagePairs": {
      "ja-en": {
        "Engine": "OPUS-MT",
        "ModelName": "opus-mt-ja-en"
      },
      "en-ja": {
        "Engine": "OPUS-MT", 
        "ModelName": "opus-mt-en-ja"
      }
    }
  }
}
```

**または、用意済みの設定ファイルをコピー:**
```powershell
Copy-Item appsettings.SentencePiece.json appsettings.json
```

## 🧪 動作確認

### 1. ビルドテスト

```powershell
dotnet build --configuration Release
```

### 2. 単体テスト実行

```powershell
# SentencePiece関連テスト
dotnet test tests/Baketa.Infrastructure.Tests/Translation/Local/Onnx/SentencePiece/

# 特定のテストクラス
dotnet test --filter "ClassName~RealSentencePieceTokenizerTests"
```

### 3. 統合テスト実行

```powershell
# 統合テスト
dotnet test --filter "ClassName~SentencePieceIntegrationTests"

# パフォーマンステスト
dotnet test --filter "Category=Performance"
```

## 📊 期待される結果

### ✅ 成功時の表示例

```
=== OPUS-MT モデル検証スクリプト ===
🔍 検証中: opus-mt-ja-en (日本語→英語)
  ✅ 有効 - サイズ: 792.5 KB

🔍 検証中: opus-mt-en-ja (英語→日本語)  
  ✅ 有効 - サイズ: 801.2 KB

=== 検証結果サマリー ===
✅ 有効なモデル: 2/2
🎉 すべてのモデルが正常です！
```

### ✅ テスト成功例

```
🧪 実行中: RealSentencePieceTokenizer
✅ RealSentencePieceTokenizer - 成功

🧪 実行中: 統合テスト
✅ 統合テスト - 成功

=== テスト結果サマリー ===
✅ 成功: 5/5 テストスイート
🎉 すべてのテストが成功しました！
```

## 🔧 トラブルシューティング

### ❌ ダウンロードエラー

**原因:** ネットワーク接続またはHuggingFaceへのアクセス問題

**解決策:**
1. インターネット接続を確認
2. ファイアウォール設定を確認
3. `-Force` オプションで再実行
4. 手動ダウンロードを試行

### ❌ ファイル検証エラー

**原因:** 不完全なダウンロードまたは破損ファイル

**解決策:**
1. モデルファイルを削除
2. ダウンロードスクリプトを `-Force` で再実行
3. ファイルサイズを確認（通常500KB-1MB程度）

### ❌ テスト失敗

**原因:** モデルファイル不存在または設定エラー

**解決策:**
1. `verify_opus_mt_models.ps1` でモデルファイルを確認
2. `appsettings.json` の設定を確認
3. `-Verbose` オプションで詳細ログを確認

## 🎯 次のステップ

モデル配置完了後の次のタスク：

1. **実際のBaketaアプリケーションでの動作確認**
2. **ゲーム画面でのOCR→翻訳フロー検証**
3. **長時間動作テスト**
4. **UI統合テスト**
5. **Gemini API統合開始**

## 📞 サポート

問題が発生した場合：

1. **ログファイルを確認** - `logs/` ディレクトリ
2. **詳細実行** - 各スクリプトに `-Verbose` オプション追加
3. **環境確認** - PowerShell バージョン、.NET SDK バージョン
4. **手動確認** - モデルファイルの存在とサイズチェック

---

*最終更新: 2025年5月28日*