# OPUS-MT SentencePieceモデルファイル取得ガイド

このドキュメントでは、実際のOPUS-MT SentencePieceモデルファイルを取得してBaketaプロジェクトで使用する方法を説明します。

## 📋 概要

OPUS-MTプロジェクトは、Helsinki-NLPによって開発されたオープンソースの機械翻訳モデルです。これらのモデルはHugging Faceプラットフォームで配布されており、SentencePieceトークナイザーを使用しています。

## 🎯 必要なモデル

Baketaプロジェクトでは以下のモデルが推奨されます：

### 基本言語ペア
1. **日本語 → 英語**: `Helsinki-NLP/opus-mt-ja-en`
2. **英語 → 日本語**: `Helsinki-NLP/opus-mt-en-jap`

### 拡張言語ペア（オプション）
3. **中国語(簡体字) → 英語**: `Helsinki-NLP/opus-mt-zh-en`
4. **英語 → 中国語(簡体字)**: `Helsinki-NLP/opus-mt-en-zh`

## 🔧 取得方法

### 方法1: Hugging Face Hub CLI使用（推奨）

```bash
# Hugging Face Hub CLIをインストール
pip install huggingface_hub

# 日本語→英語モデルを取得
huggingface-cli download Helsinki-NLP/opus-mt-ja-en --include="*.model" --cache-dir ./models --local-dir ./Models/SentencePiece

# 英語→日本語モデルを取得
huggingface-cli download Helsinki-NLP/opus-mt-en-jap --include="*.model" --cache-dir ./models --local-dir ./Models/SentencePiece
```

### 方法2: Python transformersライブラリ使用

```python
from transformers import AutoTokenizer
import shutil
import os

def download_sentencepiece_model(model_name, output_dir):
    """OPUS-MTモデルからSentencePieceファイルを抽出"""
    
    # トークナイザーをダウンロード
    tokenizer = AutoTokenizer.from_pretrained(model_name)
    
    # キャッシュディレクトリからSentencePieceモデルファイルを探す
    cache_dir = tokenizer.name_or_path
    if os.path.exists(cache_dir):
        for file in os.listdir(cache_dir):
            if file.endswith('.model'):
                source_path = os.path.join(cache_dir, file)
                dest_path = os.path.join(output_dir, f"{model_name.replace('/', '-')}.model")
                shutil.copy2(source_path, dest_path)
                print(f"コピー完了: {dest_path}")
                return dest_path
    
    # 代替方法: tokenizer.save_pretrained()を使用
    temp_dir = f"./temp_{model_name.replace('/', '-')}"
    tokenizer.save_pretrained(temp_dir)
    
    for file in os.listdir(temp_dir):
        if file.endswith('.model'):
            source_path = os.path.join(temp_dir, file)
            dest_path = os.path.join(output_dir, f"{model_name.replace('/', '-')}.model")
            shutil.copy2(source_path, dest_path)
            print(f"コピー完了: {dest_path}")
            
            # 一時ディレクトリを削除
            shutil.rmtree(temp_dir)
            return dest_path
    
    raise FileNotFoundError(f"SentencePieceモデルファイルが見つかりません: {model_name}")

# 使用例
if __name__ == "__main__":
    os.makedirs("./Models/SentencePiece", exist_ok=True)
    
    models = [
        "Helsinki-NLP/opus-mt-ja-en",
        "Helsinki-NLP/opus-mt-en-jap",
    ]
    
    for model in models:
        try:
            path = download_sentencepiece_model(model, "./Models/SentencePiece")
            print(f"✅ {model} → {path}")
        except Exception as e:
            print(f"❌ {model}: {e}")
```

### 方法3: 直接ダウンロード

以下のURLから直接ダウンロードできます：

```bash
# 日本語→英語モデル
curl -L "https://huggingface.co/Helsinki-NLP/opus-mt-ja-en/resolve/main/source.spm" -o "./Models/SentencePiece/opus-mt-ja-en.model"

# 英語→日本語モデル
curl -L "https://huggingface.co/Helsinki-NLP/opus-mt-en-jap/resolve/main/source.spm" -o "./Models/SentencePiece/opus-mt-en-jap.model"
```

## 📁 ファイル配置

ダウンロードしたSentencePieceモデルファイルは以下のディレクトリに配置してください：

```
E:\dev\Baketa\Models\SentencePiece\
├── opus-mt-ja-en.model      # 日本語→英語
├── opus-mt-en-jap.model     # 英語→日本語
├── opus-mt-zh-en.model      # 中国語→英語（オプション）
└── opus-mt-en-zh.model      # 英語→中国語（オプション）
```

## ⚙️ 設定ファイル更新

`appsettings.json`でモデルの設定を行います：

```json
{
  "SentencePiece": {
    "ModelsDirectory": "Models/SentencePiece",
    "DefaultModel": "opus-mt-ja-en",
    "DownloadUrl": "https://huggingface.co/Helsinki-NLP/{0}/resolve/main/source.spm",
    "ModelCacheDays": 30,
    "MaxDownloadRetries": 3,
    "DownloadTimeoutMinutes": 5,
    "EnableChecksumValidation": false,
    "EnableAutoCleanup": true,
    "CleanupThresholdDays": 90
  }
}
```

## 🔍 モデルファイル検証

ダウンロードしたファイルの整合性を確認するためのスクリプト：

```python
import os
import hashlib

def verify_model_file(file_path):
    """SentencePieceモデルファイルの基本検証"""
    
    if not os.path.exists(file_path):
        return False, "ファイルが存在しません"
    
    # ファイルサイズチェック
    size = os.path.getsize(file_path)
    if size < 1000:  # 1KB未満は異常
        return False, f"ファイルサイズが小さすぎます: {size} bytes"
    
    # バイナリファイルかチェック（SentencePieceは通常バイナリ）
    try:
        with open(file_path, 'rb') as f:
            header = f.read(16)
            # SentencePieceモデルファイルは通常バイナリ
            if header.startswith(b'#') or header.startswith(b'trainer_spec'):
                return False, "テキストファイルのようです（バイナリである必要があります）"
    except Exception as e:
        return False, f"ファイル読み込みエラー: {e}"
    
    return True, f"検証成功 (サイズ: {size:,} bytes)"

# 検証実行
model_dir = "./Models/SentencePiece"
for file_name in os.listdir(model_dir):
    if file_name.endswith('.model'):
        file_path = os.path.join(model_dir, file_name)
        is_valid, message = verify_model_file(file_path)
        status = "✅" if is_valid else "❌"
        print(f"{status} {file_name}: {message}")
```

## 🚀 自動ダウンロードスクリプト

Baketaプロジェクト用の統合ダウンロードスクリプト：

```bash
#!/bin/bash
# download_opus_models.sh

set -e

# 設定
MODELS_DIR="./Models/SentencePiece"
MODELS=(
    "Helsinki-NLP/opus-mt-ja-en"
    "Helsinki-NLP/opus-mt-en-jap"
)

# ディレクトリ作成
mkdir -p "$MODELS_DIR"

echo "🚀 OPUS-MT SentencePieceモデルダウンロード開始"

# 各モデルをダウンロード
for model in "${MODELS[@]}"; do
    echo "📥 ダウンロード中: $model"
    
    # モデル名からファイル名を生成
    filename=$(echo "$model" | sed 's|Helsinki-NLP/||' | sed 's|/|-|g')
    output_path="$MODELS_DIR/${filename}.model"
    
    # Hugging Face Hub CLIを使用
    if command -v huggingface-cli &> /dev/null; then
        huggingface-cli download "$model" source.spm --cache-dir ./temp --local-dir ./temp
        mv "./temp/source.spm" "$output_path"
        rm -rf ./temp
    else
        # curlでダウンロード
        url="https://huggingface.co/$model/resolve/main/source.spm"
        curl -L "$url" -o "$output_path"
    fi
    
    if [ -f "$output_path" ]; then
        size=$(stat -f%z "$output_path" 2>/dev/null || stat -c%s "$output_path")
        echo "✅ 完了: $output_path (${size} bytes)"
    else
        echo "❌ 失敗: $model"
    fi
done

echo "🎉 ダウンロード完了"
echo "📁 モデルディレクトリ: $MODELS_DIR"
ls -la "$MODELS_DIR"
```

## 📝 トラブルシューティング

### よくある問題と解決方法

1. **ファイルが見つからない**
   ```
   エラー: SentencePieceモデルファイルが見つかりません
   解決: ファイル名とパスを確認。source.spmを*.modelにリネーム
   ```

2. **ダウンロードに失敗する**
   ```
   エラー: HTTP 404 または接続エラー
   解決: インターネット接続確認、URLの確認、プロキシ設定
   ```

3. **ファイルサイズが異常**
   ```
   エラー: ファイルサイズが小さすぎる（HTMLエラーページなど）
   解決: 直接ブラウザでURLを確認、認証トークンが必要な場合がある
   ```

4. **モデルが読み込めない**
   ```
   エラー: Microsoft.ML.Tokenizersで読み込みエラー
   解決: ファイル形式確認、バイナリファイルかチェック
   ```

## 📚 参考リンク

- [OPUS-MT Project](https://github.com/Helsinki-NLP/Opus-MT)
- [Helsinki-NLP Models on Hugging Face](https://huggingface.co/Helsinki-NLP)
- [SentencePiece Documentation](https://github.com/google/sentencepiece)
- [Microsoft.ML.Tokenizers Documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.tokenizers)

## 📄 ライセンス

OPUS-MTモデルは**CC-BY 4.0ライセンス**で提供されています。商用利用可能ですが、適切なクレジット表記が必要です。

```
OPUS-MT models by Helsinki-NLP
Licensed under CC-BY 4.0
https://github.com/Helsinki-NLP/Opus-MT
```
