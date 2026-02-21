# Baketa Surya OCR Server

Surya OCRモデルを使用したgRPC OCRサーバー。

[Issue #458] 翻訳機能はC# OnnxTranslationEngineに移行済み。このサーバーはOCR（Surya Detection + Recognition）のみを提供します。

---

## 概要

- **テキスト認識** (`Recognize` RPC) - Detection + Recognition
- **テキスト検出** (`Detect` RPC) - Detection only（ROI学習用高速検出）
- **ヘルスチェック** (`HealthCheck` / `IsReady` RPC)
- **デバイス切り替え** (`SwitchDevice` RPC) - GPU/CPUフォールバック

---

## セットアップ

### Python環境

Python 3.10以上が必要です。

```bash
cd grpc_server
pip install -r requirements.txt
```

### サーバー起動

```bash
python unified_server.py --port 50051
```

### PyInstallerビルド

```bash
# CPU版
.\venv_build\Scripts\pyinstaller BaketaUnifiedServer.spec

# CUDA版
.\venv_build_cuda\Scripts\pyinstaller BaketaUnifiedServer.spec
```

---

## プロジェクト構造

```
grpc_server/
├── unified_server.py            # メインサーバー（Surya OCR）
├── ocr_server_surya.py          # OCR gRPCサービス実装
├── resource_monitor.py          # リソース監視
├── requirements.txt             # Python依存関係
├── BaketaUnifiedServer.spec     # PyInstaller設定
└── protos/
    ├── __init__.py
    ├── ocr.proto                # gRPC OCRサービス定義
    ├── ocr_pb2.py               # (生成)メッセージクラス
    └── ocr_pb2_grpc.py          # (生成)サービススタブ
```

---

## 参考資料

- [Surya OCR](https://github.com/VikParuchuri/surya)
- [gRPC Python Quickstart](https://grpc.io/docs/languages/python/quickstart/)
