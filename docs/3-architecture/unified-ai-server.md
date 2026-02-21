# Baketa Unified AI Server

Issue #292: OCR + 翻訳統合AIサーバー

## 概要

BaketaUnifiedServerは、**Surya OCR**と**NLLB-200翻訳**を単一プロセスで実行する統合AIサーバーです。
従来の2プロセス構成（OCRサーバー + 翻訳サーバー）を統合し、VRAMとメモリを大幅に削減しています。

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│                    BaketaUnifiedServer                          │
│                     (単一Python プロセス)                        │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐      ┌─────────────────────────────────┐   │
│  │   Surya OCR     │      │        NLLB-200                 │   │
│  │  ┌───────────┐  │      │  ┌─────────────────────────┐    │   │
│  │  │ Detection │  │      │  │ ONNX Runtime Engine     │    │   │
│  │  │ (Native)  │  │      │  │ (nllb-200-onnx-int8)    │    │   │
│  │  └───────────┘  │      │  │                         │    │   │
│  │  ┌───────────┐  │      │  └─────────────────────────┘    │   │
│  │  │Recognition│  │      │                                 │   │
│  │  │ (Native)  │  │      │                                 │   │
│  │  └───────────┘  │      │                                 │   │
│  └─────────────────┘      └─────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│                      gRPC Server (HTTP/2)                       │
│                         Port: 50051                             │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ gRPC
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Baketa C# Client                           │
│  ┌─────────────────┐         ┌─────────────────────────────┐   │
│  │ GrpcOcrClient   │         │ GrpcTranslationClient       │   │
│  └─────────────────┘         └─────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

## 主な特徴

### VRAM/メモリ最適化

| 構成 | VRAM使用量 | メモリ使用量 |
|------|-----------|-------------|
| 2プロセス構成（旧） | ~1GB | ~2GB |
| 統合サーバー（新） | ~500MB | ~1GB |
| **削減率** | **50%** | **50%** |

### CUDAコンテキスト共有
- 単一プロセスでCUDAコンテキストを共有
- GPU初期化オーバーヘッドの削減
- より効率的なVRAM割り当て

### 並列モデルロード
- OCRとNLLBモデルを並列でロード
- 起動時間の短縮（約40%改善）

## 配布形態

統合AIサーバーは `models-v3` リリースで配布されます。

**リリースURL**: https://github.com/koizumiiiii/Baketa/releases/tag/models-v3

| アセット | 説明 | サイズ |
|---------|------|--------|
| `BaketaUnifiedServer-cpu.zip` | CPU版 | ~300MB |
| `BaketaUnifiedServer-cuda.zip.001/.002` | CUDA版（分割） | ~2.7GB |

### CUDA版の結合方法
```cmd
copy /b BaketaUnifiedServer-cuda.zip.001+BaketaUnifiedServer-cuda.zip.002 BaketaUnifiedServer-cuda.zip
```

## gRPC API

### OCR API

| RPC | 説明 |
|-----|------|
| `RecognizeText` | 画像からテキストを検出・認識 |
| `DetectTextRegions` | テキスト領域のみ検出 |
| `HealthCheck` | サーバー状態確認 |
| `IsReady` | モデル準備完了確認 |

### Translation API

| RPC | 説明 |
|-----|------|
| `Translate` | 単一テキスト翻訳 |
| `TranslateBatch` | バッチ翻訳（最大32件） |
| `HealthCheck` | サーバー状態確認 |
| `IsReady` | モデル準備完了確認 |

## 起動方法

### 開発時
```bash
# 統合サーバー起動
python grpc_server/unified_server.py

# カスタムポート指定
python grpc_server/unified_server.py --port 50052

# CUDAデバイス指定
CUDA_VISIBLE_DEVICES=0 python grpc_server/unified_server.py
```

### 本番環境（exe）
```cmd
# CPU版
BaketaUnifiedServer\BaketaUnifiedServer.exe

# CUDA版
BaketaUnifiedServer\BaketaUnifiedServer.exe
```

## C#クライアント連携

### 自動起動
`PythonServerManager` がアプリ起動時に統合サーバーを自動起動します。

```csharp
// appsettings.json
{
  "OCR": {
    "UseGrpcClient": true,
    "GrpcServerAddress": "http://127.0.0.1:50051"
  },
  "Translation": {
    "UseGrpcClient": true,
    "GrpcServerAddress": "http://127.0.0.1:50051"
  }
}
```

### Keep-Alive設定
```csharp
// GrpcTranslationClient.cs
var channelOptions = new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler
    {
        KeepAlivePingDelay = TimeSpan.FromSeconds(10),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
        EnableMultipleHttp2Connections = true
    }
};
```

## ビルド方法

### PyInstallerでのビルド

```bash
cd grpc_server

# CPU版
.\venv_build\Scripts\pyinstaller BaketaUnifiedServer.spec

# CUDA版
.\venv_build_cuda\Scripts\pyinstaller BaketaUnifiedServer.spec
```

### CUDA版ビルド環境構築
```bash
cd grpc_server
py -3.10 -m venv venv_build_cuda
.\venv_build_cuda\Scripts\pip install -r requirements.txt pyinstaller
.\venv_build_cuda\Scripts\pip install torch==2.9.1 --index-url https://download.pytorch.org/whl/cu126
```

## 関連ファイル

| ファイル | 説明 |
|---------|------|
| `grpc_server/unified_server.py` | 統合サーバー実装 |
| `grpc_server/BaketaUnifiedServer.spec` | PyInstaller設定 |
| `Baketa.Infrastructure/Translation/Services/PythonServerManager.cs` | サーバー起動管理 |
| `Baketa.Infrastructure/OCR/Clients/GrpcOcrClient.cs` | OCR gRPCクライアント |
| `Baketa.Infrastructure/Translation/Clients/GrpcTranslationClient.cs` | 翻訳gRPCクライアント |

## トラブルシューティング

### サーバーが起動しない
```bash
# ポート使用状況確認
netstat -an | findstr :50051

# 依存関係確認
pip install -r requirements.txt
```

### CUDA関連エラー
```bash
# CUDAバージョン確認
nvidia-smi

# CPU版にフォールバック
# BaketaUnifiedServer-cpu.zip を使用
```

### モデルロードエラー
```bash
# モデルファイル確認（Suryaモデルは初回起動時にS3から自動ダウンロード）
# NLLBモデル確認
ls Models/nllb-200-onnx-int8/
```

---

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2026-01-25 | 初版作成 |
| 2026-01-15 | Issue #292 統合AIサーバー実装完了 |
