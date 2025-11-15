# Baketa gRPC Translation Server

**Phase 2.2**: Python gRPCサーバー実装
Meta NLLB-200モデルベースの高品質翻訳サービス

✅ **Phase 2.2.1完了**: CTranslate2統合により**74.6%メモリ削減達成**（2.4GB→610MB）
- デフォルト: NllbEngine（transformers、2.4GB）
- 推奨: CTranslate2Engine（int8量子化、610MB）← `--use-ctranslate2` フラグで有効化

---

## 📋 概要

Baketa翻訳システムのPython gRPCサーバー実装です。NLLB-200モデルを使用して、以下の機能を提供します：

- **単一翻訳** (`Translate` RPC)
- **バッチ翻訳** (`TranslateBatch` RPC) - GPU最適化対応
- **ヘルスチェック** (`HealthCheck` RPC)
- **準備状態確認** (`IsReady` RPC)

---

## 🚀 セットアップ手順

### 1. Python環境準備

Python 3.10以上が必要です。

```bash
# pyenv-winで環境確認（Windowsの場合）
pyenv global 3.10.9

# Python バージョン確認
python --version
# または
py --version
```

### 2. 依存関係インストール

```bash
cd grpc_server
pip install -r requirements.txt
```

**主要パッケージ**:
- `grpcio` >= 1.60.0 - gRPC実行環境
- `grpcio-tools` >= 1.60.0 - Protoコンパイラ
- `transformers` >= 4.30.0 - NLLB-200モデル
- `torch` >= 2.0.0 - PyTorch
- `sentencepiece` >= 0.1.99 - トークナイザー

### 3. translation.protoコンパイル

```bash
cd protos

# Pythonコード生成
python -m grpc_tools.protoc \
  -I. \
  --python_out=. \
  --grpc_python_out=. \
  --pyi_out=. \
  translation.proto
```

**生成されるファイル**:
- `translation_pb2.py` - メッセージクラス
- `translation_pb2_grpc.py` - サービススタブ・サーバー基底クラス
- `translation_pb2.pyi` - 型ヒント（VSCode補完用）

### 4. translation_server.pyの有効化

`translation_server.py` と `start_server.py` のコメントアウトされたインポートとコードを有効化してください：

```python
# コメントアウトを解除:
from protos import translation_pb2, translation_pb2_grpc

# gRPCサービス登録のコメントアウトを解除:
translation_pb2_grpc.add_TranslationServiceServicer_to_server(servicer, server)
```

---

## 🎯 サーバー起動

### 推奨起動（CTranslate2エンジン）

```bash
cd grpc_server
python start_server.py --use-ctranslate2
```

**CTranslate2エンジン設定**:
- ホスト: `0.0.0.0` (全インターフェース)
- ポート: `50051`
- モデル: `../models/nllb-200-ct2` (int8量子化、610MB)
- デバイス: GPU利用可能ならCUDA、なければCPU
- **メモリ削減**: 74.6%（2.4GB → 610MB）

### 基本起動（NllbEngine）

```bash
cd grpc_server
python start_server.py
```

**NllbEngine設定**:
- ホスト: `0.0.0.0` (全インターフェース)
- ポート: `50051`
- モデル: `facebook/nllb-200-distilled-600M` (transformers、2.4GB)
- デバイス: GPU利用可能ならCUDA、なければCPU

### オプション付き起動

```bash
# CTranslate2エンジン使用（推奨）
python start_server.py --use-ctranslate2

# ポート指定
python start_server.py --port 50052

# ホスト指定（ローカルのみ）
python start_server.py --host localhost --port 50051

# CTranslate2 + カスタムポート
python start_server.py --use-ctranslate2 --port 50052

# 重いモデル使用（1.3B、約5GB、NllbEngineのみ）
python start_server.py --heavy-model

# デバッグモード
python start_server.py --debug
```

### 起動確認

**CTranslate2エンジン起動時**:

```
================================================================================
gRPC Translation Server is running on 0.0.0.0:50051
   Engine: CTranslate2Engine
   Model: CTranslate2 (int8)
   Device: cuda
   Supported languages: en, ja, zh, zh-cn, zh-tw, ko, es, fr, de, ru, ar
================================================================================
Press Ctrl+C to stop the server
```

**NllbEngine起動時**:

```
================================================================================
gRPC Translation Server is running on 0.0.0.0:50051
   Engine: NllbEngine
   Model: facebook/nllb-200-distilled-600M
   Device: cuda
   Supported languages: en, ja, zh, zh-cn, zh-tw, ko, es, fr, de, ru, ar
================================================================================
Press Ctrl+C to stop the server
```

---

## 🔧 トラブルシューティング

### ImportError: cannot import name 'translation_pb2'

**原因**: translation.protoがコンパイルされていません。

**解決**:
```bash
cd protos
python -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. translation.proto
```

### ModuleNotFoundError: No module named 'grpc_tools'

**原因**: grpcio-toolsがインストールされていません。

**解決**:
```bash
pip install grpcio-tools
```

### CUDA out of memory

**原因**: GPUメモリ不足

**解決**:
1. 軽量モデル（600M）使用（デフォルト）
2. CPU実行にフォールバック（自動）
3. バッチサイズ削減（自動調整）

### Model download timeout

**原因**: NLLB-200モデル初回ダウンロード（約2.4GB）

**解決**: ネットワーク接続を確認し、ダウンロード完了まで待機

### ModelNotLoadedError: モデルが見つかりません: ../models/nllb-200-ct2

**原因**: CTranslate2変換済みモデルが存在しません。

**解決**:
```bash
# モデル変換を実行（既存スクリプト使用）
cd scripts
python convert_nllb_to_ctranslate2.py
```

または、`--use-ctranslate2` フラグを外してNllbEngineを使用してください。

### ModuleNotFoundError: No module named 'ctranslate2'

**原因**: ctranslate2パッケージがインストールされていません。

**解決**:
```bash
pip install ctranslate2>=3.20.0
```

---

## 📚 サポート言語

| 言語 | コード | NLLB-200コード |
|------|--------|----------------|
| 英語 | `en` | `eng_Latn` |
| 日本語 | `ja` | `jpn_Jpan` |
| 中国語（簡体） | `zh`, `zh-cn` | `zho_Hans` |
| 中国語（繁体） | `zh-tw` | `zho_Hant` |
| 韓国語 | `ko` | `kor_Hang` |
| スペイン語 | `es` | `spa_Latn` |
| フランス語 | `fr` | `fra_Latn` |
| ドイツ語 | `de` | `deu_Latn` |
| ロシア語 | `ru` | `rus_Cyrl` |
| アラビア語 | `ar` | `arb_Arab` |

---

## 🎯 次のステップ（Phase 2.3）

gRPCサーバーが起動したら、次はC#クライアント実装（Phase 2.3）に進みます：

1. `Grpc.Net.Client` パッケージ追加 ✅ (Phase 2.1で完了)
2. `GrpcTranslationClient.cs` 実装
3. C# ↔ Python gRPC通信確認
4. 既存の `StdinStdoutTranslationClient` と並行稼働確認

---

## ✅ Phase 2.2.1完了: CTranslate2統合

**実装完了**: 2025-10-06

**実装内容**:
```python
# engines/ctranslate2_engine.py (430行)
import ctranslate2
from transformers import AutoTokenizer

class CTranslate2Engine(TranslationEngine):
    def __init__(self, model_path="../models/nllb-200-ct2", compute_type="int8"):
        self.translator = ctranslate2.Translator(
            str(model_path),
            device="cuda" if torch.cuda.is_available() else "cpu",
            compute_type=compute_type,
            inter_threads=4
        )
        self.tokenizer = AutoTokenizer.from_pretrained("facebook/nllb-200-distilled-600M")
        # ...

    async def translate(self, text, source_lang, target_lang):
        # CTranslate2推論（int8量子化、GPU最適化）
        # メモリ: 2.4GB → 610MB (74.6%削減)
        # ロード時間: 3.79秒
        # ...
```

**達成効果**:
- ✅ **メモリ削減**: 2.4GB → 610MB（74.6%削減）
- ✅ **int8量子化**: GPU使用、compute_type=int8_float32
- ✅ **ロード時間**: 3.79秒（NllbEngineと同等）
- ✅ **エンジン切り替え**: `--use-ctranslate2` フラグで選択可能

**参照**:
- `grpc_server/engines/ctranslate2_engine.py` - 実装ファイル
- `scripts/nllb_translation_server_ct2.py` - 参照実装
- `docs/CTRANSLATE2_INTEGRATION_COMPLETE.md` - 既存統合手順

### Gemini API統合（Phase X）

現在のアーキテクチャは、将来のGemini API統合を見据えた設計になっています：

```python
# engines/gemini_engine.py (将来実装)
class GeminiEngine(TranslationEngine):
    async def translate(self, text, source_lang, target_lang):
        # Gemini API呼び出し
        pass
```

エンジン切り替えは設定ファイルまたは環境変数で制御予定。

---

## 📝 ログ

サーバーログは以下に出力されます：

- **標準出力**: コンソールに表示
- **ファイル**: `translation_server.log` (UTF-8エンコーディング)

---

## 🛠️ 開発者向け情報

### プロジェクト構造

```
grpc_server/
├── __init__.py                  # パッケージ初期化
├── README.md                    # このファイル
├── requirements.txt             # Python依存関係
├── start_server.py              # サーバー起動スクリプト
├── translation_server.py        # gRPCサービス実装
├── engines/
│   ├── __init__.py
│   ├── base.py                  # TranslationEngine抽象クラス
│   ├── nllb_engine.py           # NLLB-200実装（transformers）
│   └── ctranslate2_engine.py    # CTranslate2実装（int8量子化、推奨）
└── protos/
    ├── __init__.py
    ├── translation.proto        # gRPCサービス定義
    ├── translation_pb2.py       # (生成)メッセージクラス
    ├── translation_pb2_grpc.py  # (生成)サービススタブ
    └── translation_pb2.pyi      # (生成)型ヒント
```

### テスト実行

```bash
cd grpc_server
pytest tests/
```

---

## 📖 参考資料

- [NLLB-200 Model Card](https://huggingface.co/facebook/nllb-200-distilled-600M)
- [gRPC Python Quickstart](https://grpc.io/docs/languages/python/quickstart/)
- [Protocol Buffers Documentation](https://protobuf.dev/)

---

## 🐛 トラブル報告

問題が発生した場合は、以下の情報とともに報告してください：

1. エラーメッセージ全文
2. `translation_server.log` の内容
3. Python バージョン (`python --version`)
4. 依存パッケージバージョン (`pip freeze`)
5. GPU有無 (`torch.cuda.is_available()`)
