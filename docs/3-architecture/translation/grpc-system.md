# gRPC翻訳システム設計

**最終更新**: 2025-11-17
**Phase**: 5.2D完了

## 概要

Baketaのgp翻訳システムは、C# ↔ Python間の高性能通信を実現するHTTP/2ベースのgRPC実装です。NLLB-200多言語モデルをPythonサーバー側で実行し、C#クライアントからgRPC経由でアクセスします。

---

## アーキテクチャ概要

### 全体構成

```
┌──────────────────────────────────────────────────────────────────┐
│                    Baketa.Infrastructure (C#)                      │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │  GrpcTranslationEngineAdapter                               │  │
│  │  - ITranslationEngine実装                                   │  │
│  │  - バッチ翻訳サポート（最大32テキスト）                     │  │
│  └────────────────────┬────────────────────────────────────────┘  │
│                       │                                            │
│  ┌────────────────────▼────────────────────────────────────────┐  │
│  │  GrpcTranslationClient                                      │  │
│  │  - HTTP/2 gRPCチャネル管理                                  │  │
│  │  - Keep-Alive: 10秒間隔                                     │  │
│  │  - WithWaitForReady(true): TCP接続待機                      │  │
│  │  - Timeout: 30秒/リクエスト                                 │  │
│  └────────────────────┬────────────────────────────────────────┘  │
│                       │                                            │
│  ┌────────────────────▼────────────────────────────────────────┐  │
│  │  PythonServerManager                                        │  │
│  │  - 自動サーバー起動                                         │  │
│  │  - HealthCheck（5秒タイムアウト）                          │  │
│  │  - Ready状態監視                                            │  │
│  └─────────────────────────────────────────────────────────────┘  │
└────────────────────────┬───────────────────────────────────────────┘
                         │ gRPC (HTTP/2, port 50051)
                         │ Keep-Alive: 10s interval
                         ▼
┌────────────────────────────────────────────────────────────────────┐
│                   grpc_server/ (Python)                            │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │  start_server.py                                            │  │
│  │  - gRPCサーバー起動エントリーポイント                       │  │
│  │  - Port: 50051                                              │  │
│  │  - UTF-8 encoding設定                                       │  │
│  └────────────────────┬────────────────────────────────────────┘  │
│                       │                                            │
│  ┌────────────────────▼────────────────────────────────────────┐  │
│  │  translation_server.py - TranslationServicer                │  │
│  │  - Translate(): 単一テキスト翻訳                            │  │
│  │  - TranslateBatch(): バッチ翻訳（最大32）                  │  │
│  │  - HealthCheck(): サーバー状態確認                         │  │
│  │  - IsReady(): モデル準備状態確認                           │  │
│  └────────────────────┬────────────────────────────────────────┘  │
│                       │                                            │
│  ┌────────────────────▼────────────────────────────────────────┐  │
│  │  engines/ctranslate2_engine.py                              │  │
│  │  - NLLB-200: facebook/nllb-200-distilled-600M               │  │
│  │  - CTranslate2最適化: 80%メモリ削減（2.4GB→500MB）         │  │
│  │  - 200+言語対応                                             │  │
│  └─────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────┘
```

---

## C#側実装

### 1. GrpcTranslationClient

**場所**: `Baketa.Infrastructure/Translation/Clients/GrpcTranslationClient.cs`

**責務**:
- HTTP/2 gRPCチャネル管理
- Keep-Alive設定（10秒間隔）
- 自動再接続機能
- タイムアウト管理（30秒）

**主要機能**:

```csharp
public class GrpcTranslationClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly TranslationService.TranslationServiceClient _client;

    public GrpcTranslationClient(string serverAddress = "http://127.0.0.1:50051")
    {
        // HTTP/2チャネル作成 + Keep-Alive設定
        _channel = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(10),  // 10秒間隔でPing
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
                EnableMultipleHttp2Connections = true
            }
        });

        _client = new TranslationService.TranslationServiceClient(_channel);
    }

    public async Task<TranslateResponse> TranslateAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var request = new TranslateRequest
        {
            SourceText = sourceText,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            RequestId = Guid.NewGuid().ToString()
        };

        // WithWaitForReady(true): TCP接続確立を待つ（初回UNAVAILABLE解消）
        var callOptions = new CallOptions(
            deadline: DateTime.UtcNow.AddSeconds(30),
            cancellationToken: cancellationToken
        ).WithWaitForReady(true);

        return await _client.TranslateAsync(request, callOptions);
    }
}
```

**Phase 5.2D修正点**:
- **問題**: 初回翻訳リクエストが`StatusCode.Unavailable`で失敗
- **原因**: gRPCチャネルは遅延初期化を行うため、最初のRPC時にTCP接続が未確立
- **解決策**: `CallOptions.WithWaitForReady(true)`を追加し、TCP接続確立を待機

### 2. GrpcTranslationEngineAdapter

**場所**: `Baketa.Infrastructure/Translation/Adapters/GrpcTranslationEngineAdaptercs`

**責務**:
- `ITranslationEngine`インターフェース実装
- Pythonサーバー自動起動
- バッチ翻訳サポート

**主要機能**:

```csharp
public class GrpcTranslationEngineAdapter : ITranslationEngine
{
    private readonly GrpcTranslationClient _grpcClient;
    private readonly PythonServerManager _serverManager;
    private readonly ILogger<GrpcTranslationEngineAdapter> _logger;

    public async Task<TranslationResult> TranslateAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        // 初回翻訳時にPythonサーバー自動起動
        await _serverManager.EnsureServerStartedAsync(cancellationToken);

        try
        {
            var response = await _grpcClient.TranslateAsync(
                sourceText,
                sourceLanguage,
                targetLanguage,
                cancellationToken
            );

            return new TranslationResult
            {
                TranslatedText = response.TranslatedText,
                IsSuccess = response.IsSuccess,
                ConfidenceScore = response.ConfidenceScore
            };
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC translation failed: {StatusCode}", ex.StatusCode);
            return TranslationResult.Failure(ex.Message);
        }
    }

    public async Task<IEnumerable<TranslationResult>> TranslateBatchAsync(
        IEnumerable<string> sourceTexts,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var request = new BatchTranslateRequest
        {
            BatchId = Guid.NewGuid().ToString()
        };

        foreach (var text in sourceTexts.Take(32)) // 最大32テキスト
        {
            request.Requests.Add(new TranslateRequest
            {
                SourceText = text,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            });
        }

        var response = await _grpcClient.TranslateBatchAsync(request, cancellationToken);

        return response.Responses.Select(r => new TranslationResult
        {
            TranslatedText = r.TranslatedText,
            IsSuccess = r.IsSuccess
        });
    }
}
```

### 3. PythonServerManager

**場所**: `Baketa.Infrastructure/Translation/Services/PythonServerManager.cs`

**責務**:
- Pythonサーバー自動起動
- HealthCheck（5秒タイムアウト）
- Ready状態監視

**Phase 5.2C修正点**:
- **問題**: Pythonサーバーが60秒ハングアップ
- **原因**: C#がPythonプロセスの`stdout`を監視していなかったため、バッファ満杯でデッドロック
- **解決策**: `process.BeginOutputReadLine()`を追加し、`stdout`非同期監視を開始

```csharp
public class PythonServerManager
{
    private Process? _serverProcess;
    private readonly ILogger<PythonServerManager> _logger;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "py",
            Arguments = "grpc_server/start_server.py --use-ctranslate2",
            UseShellExecute = false,
            RedirectStandardOutput = true,  // 重要: stdoutリダイレクト
            RedirectStandardError = true,   // 重要: stderrリダイレクト
            CreateNoWindow = true
        };

        _serverProcess = Process.Start(startInfo);

        // ⚠️ 重要: stdout/stderr非同期監視（デッドロック防止）
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        // Ready状態待機（最大30秒）
        await WaitForServerReadyAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    private async Task WaitForServerReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await _grpcClient.IsReadyAsync(cancellationToken);
                if (response.IsReady)
                {
                    _logger.LogInformation("Python gRPC server is ready");
                    return;
                }
            }
            catch (RpcException)
            {
                // サーバー起動中、リトライ
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"Python server did not become ready within {timeout.TotalSeconds}s");
    }
}
```

---

## Python側実装

### 1. start_server.py

**場所**: `grpc_server/start_server.py`

**責務**:
- gRPCサーバー起動エントリーポイント
- UTF-8エンコーディング設定
- CTranslate2エンジン初期化

```python
import sys
import grpc
from concurrent import futures
from translation_server import TranslationServicer
from engines.ctranslate2_engine import CTranslate2Engine

def main():
    # ⚠️ 重要: UTF-8エンコーディング設定（UnicodeEncodeError防止）
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

    # NLLB-200モデル初期化
    engine = CTranslate2Engine(
        model_name="facebook/nllb-200-distilled-600M",
        device="cpu"
    )

    # gRPCサーバー起動
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    translation_pb2_grpc.add_TranslationServiceServicer_to_server(
        TranslationServicer(engine), server
    )
    server.add_insecure_port('[::]:50051')
    server.start()

    print("gRPC server started on port 50051")
    server.wait_for_termination()

if __name__ == '__main__':
    main()
```

### 2. translation_server.py

**場所**: `grpc_server/translation_server.py`

**責務**:
- 4つのRPCメソッド実装

```python
class TranslationServicer(translation_pb2_grpc.TranslationServiceServicer):
    def __init__(self, engine):
        self._engine = engine
        self._is_ready = False

        # モデル初期化（非同期）
        threading.Thread(target=self._initialize_engine, daemon=True).start()

    def _initialize_engine(self):
        """バックグラウンドでモデル読み込み"""
        self._engine.initialize()
        self._is_ready = True
        print("NLLB-200 model loaded successfully")

    def Translate(self, request, context):
        """単一テキスト翻訳"""
        try:
            translated_text = self._engine.translate(
                request.source_text,
                request.source_language,
                request.target_language
            )

            return translation_pb2.TranslateResponse(
                translated_text=translated_text,
                is_success=True,
                confidence_score=0.95
            )
        except Exception as e:
            return translation_pb2.TranslateResponse(
                is_success=False,
                error=str(e)
            )

    def TranslateBatch(self, request, context):
        """バッチ翻訳（最大32テキスト）"""
        responses = []
        for req in request.requests[:32]:  # 最大32制限
            resp = self.Translate(req, context)
            responses.append(resp)

        return translation_pb2.BatchTranslateResponse(
            responses=responses,
            success_count=sum(1 for r in responses if r.is_success)
        )

    def HealthCheck(self, request, context):
        """サーバー状態確認"""
        return translation_pb2.HealthCheckResponse(
            status="SERVING",
            timestamp=int(time.time())
        )

    def IsReady(self, request, context):
        """モデル準備状態確認"""
        return translation_pb2.IsReadyResponse(
            is_ready=self._is_ready,
            model_name="facebook/nllb-200-distilled-600M"
        )
```

### 3. engines/ctranslate2_engine.py

**場所**: `grpc_server/engines/ctranslate2_engine.py`

**責務**:
- NLLB-200モデル管理
- CTranslate2最適化推論

```python
import ctranslate2
from transformers import AutoTokenizer

class CTranslate2Engine:
    def __init__(self, model_name, device="cpu"):
        self._model_name = model_name
        self._device = device
        self._translator = None
        self._tokenizer = None

    def initialize(self):
        """モデル読み込み（2.4GB → 500MB最適化）"""
        # CTranslate2形式モデル読み込み（80%メモリ削減）
        self._translator = ctranslate2.Translator(
            model_path=f"models/{self._model_name}_ct2",
            device=self._device,
            compute_type="int8"  # 量子化
        )

        # Tokenizerロード
        self._tokenizer = AutoTokenizer.from_pretrained(self._model_name)

    def translate(self, source_text, source_lang, target_lang):
        """テキスト翻訳（200+言語対応）"""
        # 言語コード変換（例: ja → jpn_Jpan, en → eng_Latn）
        src_flores = self._get_flores_code(source_lang)
        tgt_flores = self._get_flores_code(target_lang)

        # Tokenize
        self._tokenizer.src_lang = src_flores
        tokens = self._tokenizer(source_text, return_tensors="pt")

        # 翻訳実行
        results = self._translator.translate_batch(
            [tokens.input_ids[0].tolist()],
            target_prefix=[[self._tokenizer.convert_tokens_to_ids(tgt_flores)]]
        )

        # Detokenize
        translated_tokens = results[0].hypotheses[0]
        translated_text = self._tokenizer.decode(translated_tokens, skip_special_tokens=True)

        return translated_text

    def _get_flores_code(self, lang_code):
        """言語コード変換（ISO 639-1 → FLORES-200）"""
        mapping = {
            "ja": "jpn_Jpan",
            "en": "eng_Latn",
            "zh": "zho_Hans",
            "ko": "kor_Hang",
            # ... 200+言語マッピング
        }
        return mapping.get(lang_code, lang_code)
```

---

## gRPC Protocol Buffers定義

**場所**: `Baketa.Infrastructure/Translation/Protos/translation.proto`

```protobuf
syntax = "proto3";

option csharp_namespace = "Baketa.Infrastructure.Translation.Grpc";

package translation;

service TranslationService {
  rpc Translate (TranslateRequest) returns (TranslateResponse);
  rpc TranslateBatch (BatchTranslateRequest) returns (BatchTranslateResponse);
  rpc HealthCheck (HealthCheckRequest) returns (HealthCheckResponse);
  rpc IsReady (IsReadyRequest) returns (IsReadyResponse);
}

message TranslateRequest {
  string source_text = 1;
  string source_language = 2;
  string target_language = 3;
  string request_id = 4;
}

message TranslateResponse {
  string translated_text = 1;
  float confidence_score = 2;
  bool is_success = 3;
  string error = 4;
}

message BatchTranslateRequest {
  repeated TranslateRequest requests = 1;
  string batch_id = 2;
}

message BatchTranslateResponse {
  repeated TranslateResponse responses = 1;
  int32 success_count = 2;
}

message HealthCheckRequest {}

message HealthCheckResponse {
  string status = 1;
  int64 timestamp = 2;
}

message IsReadyRequest {}

message IsReadyResponse {
  bool is_ready = 1;
  string model_name = 2;
}
```

---

## パフォーマンス特性

### メモリ使用量

- **標準NLLB-200**: 2.4GB（Transformers形式）
- **CTranslate2最適化**: 500MB（**80%削減**）
  - int8量子化
  - モデル圧縮

### レイテンシ

- **初回翻訳**: 最大30秒（モデルロード時間含む）
- **2回目以降**: < 1秒/テキスト
- **バッチ翻訳**: < 5秒/32テキスト

### スループット

- **単一リクエスト**: ~20 requests/sec
- **バッチリクエスト**: ~50 texts/sec

---

## トラブルシューティング

### 問題1: 初回翻訳でUNAVAILABLEエラー

**症状**: 最初の翻訳リクエストが`StatusCode.Unavailable`で失敗

**原因**: gRPCチャネルの遅延初期化により、TCP接続が未確立

**解決策**: `CallOptions.WithWaitForReady(true)`を使用

### 問題2: Pythonサーバーが60秒ハングアップ

**症状**: サーバー起動時に60秒間フリーズ

**原因**: `stdout`バッファ満杯によるデッドロック

**解決策**: `process.BeginOutputReadLine()`で非同期監視

### 問題3: UnicodeEncodeError

**症状**: Python側で`cp932 codec can't encode character`エラー

**原因**: Windowsデフォルトエンコーディングが`cp932`

**解決策**: `sys.stdout.reconfigure(encoding='utf-8')`

---

## 関連ドキュメント

- `E:\dev\Baketa\CLAUDE.md` - gRPCシステム概要
- `E:\dev\Baketa\grpc_server\README.md` - Pythonサーバー起動方法
- `E:\dev\Baketa\Baketa.Infrastructure\Translation\Protos\translation.proto` - Protocol Buffers定義
- `E:\dev\Baketa\docs\3-architecture\clean-architecture.md` - Clean Architecture設計

---

**Last Updated**: 2025-11-17
**Status**: Phase 5.2D完了、プロダクション運用中
