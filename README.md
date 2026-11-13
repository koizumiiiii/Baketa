# Baketa

[![CI Build and Test](https://github.com/koizumiiiii/Baketa/actions/workflows/ci.yml/badge.svg)](https://github.com/koizumiiiii/Baketa/actions/workflows/ci.yml)
[![CodeQL](https://github.com/koizumiiiii/Baketa/actions/workflows/codeql.yml/badge.svg)](https://github.com/koizumiiiii/Baketa/actions/workflows/codeql.yml)
[![GitHub Release](https://img.shields.io/github/v/release/koizumiiiii/Baketa)](https://github.com/koizumiiiii/Baketa/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

**Baketa（バケタ）** は、ゲーム画面のテキストをリアルタイムで翻訳し、透過オーバーレイとして表示するWindows専用アプリケーションです。

## 主要機能

| カテゴリ | 機能 |
|---------|------|
| **OCR** | Surya OCR (Detection + Recognition)、Windows Graphics Capture API |
| **翻訳** | 標準翻訳 (NLLB-200ローカル)、EX翻訳 (高精度クラウド)、gRPC通信 |
| **ROI Manager** | ゲーム別テキスト位置調整、動的閾値制御、部分OCR最適化 |
| **統合AIサーバー** | OCR+翻訳を単一プロセスで実行、VRAM 50%削減 |
| **UI** | Avalonia + ReactiveUI、透過オーバーレイ、ダーク/ライトテーマ |
| **認証** | Supabase Auth (OAuth対応)、Patreon連携 |

### ROI Manager（自動調整機能）

ゲーム画面のテキスト出現位置を**自動調整**し、OCR処理を最適化する目玉機能です。

```
調整前: 全画面OCR (~6秒)  →  調整後: 変化領域のみOCR (<1秒)
```

- **ヒートマップ最適化**: 16x16グリッドでテキスト頻出位置を記録
- **動的閾値制御**: 頻出領域は厳格に、背景領域は緩和して検出
- **ゲーム別プロファイル**: 実行ファイルごとに調整データを自動保存
- **部分OCR実行**: 変化した領域のみを処理し、処理時間を大幅短縮

## クイックスタート

### エンドユーザー向け

1. [最新リリース](https://github.com/koizumiiiii/Baketa/releases/latest)からZIPをダウンロード
2. 任意のフォルダに展開
3. `Baketa.exe` を実行

### コンポーネントを手動配置する場合

自動取得が利用できない場合は、アプリを終了し、以下の固定ファイルを取得してください。配置先は `Baketa.exe` のあるフォルダを基準とします。

| ファイル（取得元） | SHA-256 |
|---|---|
| [CPU版](https://github.com/koizumiiiii/Baketa/releases/download/models-v3/BaketaUnifiedServer-cpu.zip) | `206de3f54cd2eb7ac19bb6c1969bf4f8cb046ae6a67c3b6c84d97ffd69582660` |
| [CUDA版 .001](https://github.com/koizumiiiii/Baketa/releases/download/models-v3/BaketaUnifiedServer-cuda.zip.001) | `06389f7adf0c8439f82b71ef5beb84bb3a880738c44c000289932bd0a9ab4b21` |
| [CUDA版 .002](https://github.com/koizumiiiii/Baketa/releases/download/models-v3/BaketaUnifiedServer-cuda.zip.002) | `5ccb1dcb3f3fc2001584c6e17c904ca472de430f27bfeb1be326ba3814b6a1fc` |
| [NLLBモデル](https://github.com/koizumiiiii/Baketa/releases/download/models-v3/nllb-200-onnx-int8.zip) | `3c9a53969f3153f390f3acf57df4c1262495eb3a57fb6cb1af058b1bb6bae639` |
| [tokenizer.json（固定コミット）](https://huggingface.co/facebook/nllb-200-distilled-600M/resolve/f8d333a098d19b4fd9a8b18f94170487ad3f821d/tokenizer.json) | `e316b82de11d0f951f370943b3c438311629547285129b0b81dadabd01bca665` |

PowerShellの `Get-FileHash -Algorithm SHA256 <ファイル名>` で各ファイルを検証し、不一致なら配置しないでください。CPU版またはCUDA版のどちらかを選びます。CUDA版は両パーツを検証した後、同じフォルダで**コマンドプロンプト**から結合します（結合後全体のハッシュは未取得）。

```cmd
copy /b BaketaUnifiedServer-cuda.zip.001+BaketaUnifiedServer-cuda.zip.002 BaketaUnifiedServer-cuda.zip
```

- サーバーZIPの内容を `grpc_server\BaketaUnifiedServer` に展開し、直下に `BaketaUnifiedServer.exe` がある状態にします。
- モデルZIPの内容を `Models\nllb-200-onnx-int8` に展開し、直下に `encoder_model_quantized.onnx` がある状態にします。
- `tokenizer.json` を `Models\nllb-200-onnx-int8\tokenizer.json` に配置します。ZIP内のフォルダ名を二重に重ねないでください。

上記の検証ファイルが存在すればインストール済みとして扱われます。`component-metadata/*.meta.json` の作成は不要で、起動時の更新確認や再取得は行いません。`tokenizer.json` が不足している場合のみ、別途補完取得されます。

#### Surya OCR モデル重みの手動配置

Surya のモデル重みは初回 OCR 時に `https://models.datalab.to` から自動取得されます。標準のキャッシュ先は `%LOCALAPPDATA%\datalab\datalab\Cache\models` です。

- 検出: `text_detection\2025_05_07`（約74MB）
- 認識: `text_recognition\2025_09_23`（約1.4GB）

取得できない環境では、同じバージョンで OCR が動作している PC と移行先の Baketa を終了し、動作している PC の上記 `models` フォルダを移行先の同じパスへコピーしてください。フォルダ構造と付随ファイルを含めてコピーします。サイズは目安です。`MODEL_CACHE_DIR` などを環境設定で変更している場合は、実際のキャッシュ先を使用してください。

### 開発者向け

```bash
# クローン
git clone https://github.com/koizumiiiii/Baketa.git
cd Baketa

# ビルド
dotnet build

# テスト実行
dotnet test

# アプリ起動
dotnet run --project Baketa.UI
```

**詳細な開発環境セットアップは [CLAUDE.md](CLAUDE.md) を参照してください。**

## アーキテクチャ

5層クリーンアーキテクチャを採用：

```
Baketa.UI (Avalonia)
    ↓
Baketa.Application (ビジネスロジック)
    ↓
Baketa.Infrastructure / Baketa.Infrastructure.Platform
    ↓
Baketa.Core (抽象化・イベント)
```

### OCR処理フロー

```mermaid
graph LR
    Capture[Windows Graphics Capture] --> Filter[OpenCV Filters]
    Filter --> OCR[Surya OCR Server]
    OCR --> Det[Detection]
    OCR --> Rec[Recognition]
    Rec --> Chunks[Text Chunks]
```

- **キャプチャ**: Windows Graphics Capture API (C++/WinRT)
- **前処理**: OpenCV モルフォロジー・ガウシアンフィルタ
- **OCR**: Surya OCR (gRPC、GPU/CUDA対応)

### gRPC翻訳システム

```mermaid
graph LR
    UI[Baketa.UI] --> App[Application]
    App --> Adapter[GrpcTranslationEngineAdapter]
    Adapter --> Client[GrpcTranslationClient]
    Client -->|HTTP/2| Server[Python gRPC Server]
    Server --> NLLB[NLLB-200]
```

- **プロトコル**: HTTP/2 (gRPC)、ポート 50051
- **モデル**: NLLB-200-distilled-600M (CTranslate2 int8量子化、~1GB)
- **最適化**: CTranslate2 + int8量子化 (メモリ80%削減)

## システム要件

| 項目 | 最低要件 | 推奨要件 |
|------|----------|----------|
| **OS** | Windows 10 (64-bit) | Windows 11 (64-bit) |
| **CPU** | 4コア以上 | 6コア以上 |
| **RAM** | 8GB | 16GB以上 |
| **GPU VRAM** | 4GB | 6GB以上 |
| **ストレージ** | 10GB | 20GB以上 |

> **Note**: CUDA対応GPU（NVIDIA GeForce GTX 1060以上）があると、OCR処理が約59倍高速化されます。
> 起動時にハードウェアチェックが実行され、要件を満たさない場合は警告が表示されます。

## プロジェクト構成

```
Baketa/
├── Baketa.Core/                 # コア機能・抽象化
├── Baketa.Infrastructure/       # OCR・翻訳エンジン
├── Baketa.Infrastructure.Platform/ # Windows専用実装
├── Baketa.Application/          # ビジネスロジック
├── Baketa.UI/                   # Avalonia UI
├── BaketaCaptureNative/         # C++/WinRT ネイティブDLL
├── grpc_server/                 # Python翻訳サーバー
├── tests/                       # テスト (2,100+ケース)
└── docs/                        # ドキュメント
```

## ドキュメント

- **[docs/README.md](docs/README.md)** - ドキュメントインデックス
- **[CLAUDE.md](CLAUDE.md)** - 開発ガイド・コーディング規約

## コントリビューション

1. [Issues](https://github.com/koizumiiiii/Baketa/issues)で課題を確認
2. リポジトリをフォーク
3. クリーンアーキテクチャとコーディング規約に従って実装
4. テストを追加・実行
5. プルリクエストを作成

### コーディング規約

- C# 12 最新機能を活用
- クリーンアーキテクチャの依存関係を厳守
- 新機能にはテストコードを必須で追加
- `ConfigureAwait(false)` をライブラリコードで使用

## ライセンス

[MIT License](LICENSE)

## 謝辞

- [Surya OCR](https://github.com/VikParuchuri/surya) - テキスト検出・認識
- [Meta NLLB-200](https://github.com/facebookresearch/fairseq/tree/nllb) - 多言語翻訳
- [Avalonia](https://avaloniaui.net/) - UIフレームワーク
- [OpenCV](https://opencv.org/) - 画像処理

---

**Baketa** - ゲーム翻訳をもっと身近に
