# Baketaリファクタリング計画

## 📋 ドキュメント情報

- **作成日**: 2025-10-03
- **対象バージョン**: Phase 12.5完了後
- **推定期間**: 20-28日
- **ステータス**: 計画中

---

## ✅ 実施タスクリスト

### Phase 0: 現状分析・調査 (3-4日) - 🔴 未着手

#### 0.1 静的解析実施 (1日)
- [ ] Roslyn Analyzerセットアップ
- [ ] Visual Studio Code Metrics実行
- [ ] デッドコード検出レポート作成
- [ ] 循環依存検出
- [ ] 複雑度測定（Cyclomatic Complexity > 15）
- [ ] 重複コード検出
- [ ] 成果物: `analysis_report.md` 作成
- [ ] 成果物: `deadcode_list.md` 作成
- [ ] 成果物: `refactoring_targets.md` 作成

#### 0.2 全体フロー調査 (1-2日)
- [ ] キャプチャフロー調査・文書化
- [ ] BaketaCaptureNative.dll連携詳細確認
- [ ] P/Invoke宣言の正確性検証
- [ ] SafeImageAdapter統合状況確認
- [ ] ObjectDisposedException根本原因特定
- [ ] OCRフロー調査・文書化
- [ ] ProximityGrouping実装確認
- [ ] 段階的フィルタリングシステム統合状況確認
- [ ] 翻訳フロー調査・文書化
- [ ] StreamingTranslationService実装状況確認
- [ ] オーバーレイ表示フロー調査・文書化
- [ ] METHOD_ENTRYログが出ない原因特定 ⚠️ 重要
- [ ] 継続監視フロー調査・文書化
- [ ] 成果物: `flow_analysis.md` 作成
- [ ] 成果物: `architecture_issues.md` 作成
- [ ] 成果物: `unused_components.md` 作成

#### 0.3 依存関係マッピング (1日)
- [ ] プロジェクト間依存関係可視化
- [ ] NuGetパッケージ整理
- [ ] 未使用パッケージ特定
- [ ] バージョン不整合確認
- [ ] 循環参照確認
- [ ] 成果物: `dependency_graph.png` 作成
- [ ] 成果物: `unused_packages.md` 作成

---

### Phase 1: デッドコード削除 (2-3日) - 🔴 未着手

#### 1.1 Phase 16関連コード完全削除 (0.5日)
- [ ] Phase16UIOverlayModule.cs 削除確認
- [ ] AvaloniaOverlayRenderer.cs 使用状況確認
- [ ] 関連イベント・ハンドラー削除
- [ ] ビルド成功確認
- [ ] テストケース実行（削除後）

#### 1.2 未使用TranslationEngine削除 (0.5日)
- [ ] MockTranslationEngine使用状況確認
- [ ] 旧TCP通信コード完全削除確認
- [ ] 未使用ファクトリーメソッド削除
- [ ] ビルド成功確認

#### 1.3 未使用キャプチャ・OCRコード削除 (0.5日)
- [ ] PrintWindow fallback使用状況確認
- [ ] Phase 3.1関連の中途半端なコード削除
- [ ] 未使用画像フィルター削除
- [ ] ビルド成功確認

#### 1.4 未使用NuGetパッケージ削除 (0.5日)
- [ ] 旧バージョン依存パッケージ削除
- [ ] 未使用ライブラリ削除
- [ ] ビルド成功確認
- [ ] コード削減量測定（目標: 2,000-3,000行）

---

### Phase 2: gRPC基盤構築 (5-7日) - 🔴 未着手

#### 2.1 Protoファイル設計 (1日)
- [ ] translation.proto設計
- [ ] TranslationService定義
- [ ] TranslateRequest/Response定義
- [ ] BatchTranslateRequest/Response定義
- [ ] CancelRequest/Response定義
- [ ] HealthCheckRequest/Response定義
- [ ] Protoファイルレビュー・確定

#### 2.2 Python gRPCサーバー実装 (2-3日)
- [ ] プロジェクト構造作成（grpc_server/）
- [ ] translation_server.py実装
- [ ] nllb_translator.py実装
- [ ] gemini_translator.py実装
- [ ] requirements.txt作成
- [ ] start_server.py実装
- [ ] HealthCheck動作確認
- [ ] 単体翻訳動作確認
- [ ] バッチ翻訳動作確認

#### 2.3 C# gRPCクライアント実装 (2-3日)
- [ ] ITranslationClient.cs設計
- [ ] TranslationResult record定義
- [ ] GrpcTranslationClient.cs実装
- [ ] TranslateAsync実装
- [ ] TranslateBatchAsync実装
- [ ] CancelTranslationAsync実装
- [ ] IsHealthyAsync実装
- [ ] StdinStdoutTranslationClient.cs並行稼働確認
- [ ] 単体テスト作成
- [ ] 統合テスト実行

---

### Phase 3: 通信層抽象化・クリーンアップ (3-4日) - 🔴 未着手

#### 3.1 OptimizedPythonTranslationEngine削除 (1日)
- [ ] GrpcTranslationEngineAdapter.cs実装
- [ ] DI登録切り替え（ITranslationClient使用）
- [ ] OptimizedPythonTranslationEngine.cs削除（1,500行）
- [ ] OperationId手動管理コード削除
- [ ] TaskCompletionSource複雑な制御削除
- [ ] ビルド成功確認
- [ ] 翻訳機能動作確認

#### 3.2 TranslationService階層整理 (1日)
- [ ] DefaultTranslationService責任明確化
- [ ] StreamingTranslationService責任明確化
- [ ] 重複コード削除
- [ ] 統合可能性検討
- [ ] リファクタリング実施

#### 3.3 stdin/stdout完全削除 (1日)
- [ ] StdinStdoutTranslationClient.cs削除
- [ ] Python stdin/stdoutサーバーコード削除
- [ ] 関連設定ファイル削除
- [ ] テストコード削除
- [ ] ビルド成功確認
- [ ] 全テストケース実行

---

### Phase 4: UI層リファクタリング (5-7日) - 🔴 未着手

#### 4.1 InPlaceTranslationOverlayManager分割 (3-4日)
- [ ] IInPlaceOverlayFactory.cs設計
- [ ] InPlaceOverlayFactory.cs実装
- [ ] CreateOverlay実装
- [ ] ConfigureOverlay実装（WIDTH_FIX含む）
- [ ] IOverlayPositioningService統合
- [ ] InPlaceTranslationOverlayManager.cs簡素化（1,067行 → 300行）
- [ ] ビルド成功確認
- [ ] オーバーレイ表示動作確認

#### 4.2 WIDTH_FIX問題の完全解決 (1日)
- [ ] METHOD_ENTRYログが出ない原因完全解明
- [ ] 実際の実行パス確認
- [ ] FactoryでWIDTH_FIX確実適用
- [ ] ログ出力確認
- [ ] 目視確認（幅固定されているか）
- [ ] 複数チャンクで動作確認

#### 4.3 イベントハンドラー整理 (1-2日)
- [ ] AggregatedChunksReadyEventHandler責任明確化
- [ ] CaptureCompletedHandler責任明確化
- [ ] TranslationCompletedHandler責任明確化
- [ ] 不要なログ削除
- [ ] エラーハンドリング強化
- [ ] ビルド成功確認

---

### Phase 5: 統合テスト・検証 (2-3日) - 🔴 未着手

#### 5.1 機能テスト (1日)
- [ ] キャプチャ → OCR → 翻訳 → オーバーレイ表示
- [ ] キャンセル動作確認
- [ ] タイムアウト動作確認
- [ ] WIDTH_FIX動作確認
- [ ] エラーハンドリング確認

#### 5.2 パフォーマンステスト (1日)
- [ ] gRPC vs stdin/stdout比較測定
- [ ] メモリリーク確認
- [ ] CPU使用率測定
- [ ] レスポンスタイム測定
- [ ] パフォーマンスレポート作成

#### 5.3 回帰テスト (1日)
- [ ] 既存1,300+テストケース実行
- [ ] 新規テストケース追加
- [ ] テストカバレッジ測定
- [ ] 全テスト成功確認

---

### Phase 6: ドキュメント整備・完了 (1日) - 🔴 未着手

#### 6.1 ドキュメント更新
- [ ] CLAUDE.md更新（gRPC移行反映）
- [ ] CLAUDE.local.md更新（Phase 12.2問題解決記録）
- [ ] README.md更新
  - [ ] プロジェクト概要の最新化
  - [ ] gRPCアーキテクチャ図追加
  - [ ] セットアップ手順更新（gRPCサーバー起動）
  - [ ] トラブルシューティング追加
- [ ] アーキテクチャ図作成（Mermaid）
- [ ] API仕様書作成（gRPC）

#### 6.2 最終確認
- [ ] 全ドキュメント整合性確認
- [ ] コード削減量最終測定
- [ ] 技術的負債解消確認
- [ ] リファクタリング完了宣言

---

## 🔥 Geminiレビュー結果

**レビュー日**: 2025-10-03
**総合評価**: 🚨 クリティカルなリスクは限定的だが、重大な見落としあり

### ✅ 高評価ポイント
1. **Phase 0（静的解析）から着手する優先順位付け** - リスク管理として理想的
2. **gRPC移行判断** - 業界標準プロトコルで「最後の通信層変更」にする方針は完全に正しい
3. **5つのフェーズ構成** - 段階的リスク管理が効果的
4. **工数見積もり（20-28日）** - 現実的な範囲

### 🚨 クリティカルなリスク: BaketaCaptureNative連携の見落とし

**Gemini指摘**:
> 🚨 **最重要リスク: `BaketaCaptureNative` (C++/WinRT) との連携**
> - **現状**: このネイティブDLLは、Windows Graphics Capture APIを利用し、キャプチャ機能の心臓部です。
> - **問題**: gRPCへの移行後、このネイティブDLLとC#アプリケーション本体がどのように通信するのかが全く考慮されていません。Pythonサーバーとの通信はgRPCに置き換わりますが、キャプチャプロセスとの連携はどうなるのでしょうか？
> - **潜在的な影響**: もしネイティブDLLとの通信にも問題があれば、アプリケーションの根幹機能が動作しなくなる可能性があります。

**対応方針**:
- Phase 0.2の調査対象に「BaketaCaptureNative.dll連携の詳細確認」を追加
- ネイティブDLLとの通信は**P/Invoke経由**であり、gRPC移行の影響を受けない
- しかし、SafeImageAdapter統合問題（ObjectDisposedException）が残存しており、これを優先解決する必要がある

---

## 🚨 現状の問題点

### 1. 技術的負債の蓄積

短期間での技術スタック変更により、対症療法的な修正が積み重なっている。

| 時期 | 変更内容 | 結果 | 残存する問題 |
|------|---------|------|------------|
| Phase 12.5 | TCP → stdin/stdout | SocketException解消 | タイムアウト制御困難（30秒問題） |
| Phase 12.2.1 | Task.Delayタイムアウト修正 | 10秒タイムアウト実装 | 上位層CancellationTokenと干渉 |
| Phase 12.2 | バッチ翻訳問題 | 個別処理フォールバック | 応答が来ない原因不明 |
| Phase 3.2 | SafeImageAdapter統合 | メモリ効率化 | InvalidCastException |
| Phase 3.1 | WindowsImageFactory修正 | 統合不完全 | ObjectDisposedException継続 |

**共通パターン**: 問題発生 → 局所的修正 → 新たな問題発生 → さらに局所的修正 → ...

### 2. OptimizedPythonTranslationEngine.cs (1,500行)

**責任過多**:
- TCP接続管理（廃止済み）
- stdin/stdout通信（現在）
- JSON serialization/deserialization
- リクエスト/応答マッピング（OperationId手動管理）
- タイムアウト制御（10秒 vs 30秒問題）
- サーキットブレーカー
- リソース管理（HybridResourceManager）
- バッチ処理
- 個別処理フォールバック

**問題**: 通信プロトコルが変わるたびに、このファイル全体を書き換え。テスト困難。

### 3. InPlaceTranslationOverlayManager.cs (1,067行)

**責任過多**:
- オーバーレイライフサイクル管理
- 位置調整
- 衝突回避
- イベント処理
- 重複防止
- WIDTH_FIX実装（実行フロー不明）

**問題**: UI問題（WIDTH_FIX）を修正しようとしても、実行フローが追えない。METHOD_ENTRYログが出力されない謎の動作。

### 4. 通信レイヤーの抽象化欠如

**現状**:
```
TranslationService → OptimizedPythonTranslationEngine → stdin/stdout直接操作
```

**問題**:
- 通信プロトコル変更時の影響範囲が広すぎる
- 単体テスト困難（Pythonサーバー起動必須）
- gRPC移行時に大規模書き換えが必要

### 5. 静的解析未実施

**推定される問題**:
- デッドコード（Phase 16関連、旧TCP実装など）
- 未使用NuGetパッケージ
- 循環依存
- 複雑度の高いメソッド（Cyclomatic Complexity > 15）
- 重複コード

### 6. stdin/stdoutの限界

**grpc.md指摘との一致**:
- ✅ メッセージ区切り手動管理必須
- ✅ リクエスト/応答対応付け手動管理（TaskCompletionSource複雑化）
- ✅ キャンセル処理自前実装（CancellationTokenが効かない）
- ✅ CPU/GC負荷（JSON serialization/deserialization）
- ✅ 複雑化でスパゲッティ化（1,500行ファイル）

**実際の問題**:
```csharp
// 現在: 200行以上の複雑な制御
var readTask = connection.Reader.ReadLineAsync(cancellationToken).AsTask();
var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
var completedTask = await Task.WhenAny(readTask, timeoutTask);
if (completedTask == timeoutTask) {
    connection.TcpClient?.Close(); // タイムアウト処理
    throw new TimeoutException(...);
}
// + OperationId管理
// + TaskCompletionSource管理
// + JSON parse/serialize
```

---

## 🎯 解決したいこと

### 1. 技術的負債の完全清算

- **デッドコード削除**: Phase 16関連、旧TCP実装、未使用フィルター等
- **コード削減**: 推定2,000-3,000行削除
- **複雑度削減**: 500行以上のクラスを200-300行に分割

### 2. 通信層の抽象化と安定化

- **gRPC移行**: 業界標準プロトコルで「最後の通信層変更」にする
- **抽象化**: ITranslationClientインターフェース導入でプロトコル非依存
- **簡素化**: 1,500行 → 100行程度のシンプルな通信層

### 3. UI層の責任分離

- **ファクトリーパターン導入**: オーバーレイ生成と設定を分離
- **WIDTH_FIX問題解決**: 確実にTextBlock.MaxWidthを設定する設計
- **実行フロー可視化**: なぜMETHOD_ENTRYログが出ないか解明・修正

### 4. テスト容易性の向上

- **Mock可能な設計**: ITranslationClientでMockClient使用可能
- **単体テスト追加**: 通信層、UI層の単体テスト
- **統合テスト整備**: gRPC vs stdin/stdout比較テスト

### 5. 保守性の向上

- **Clean Architecture準拠**: 各層の責任明確化
- **ドキュメント整備**: 全体フロー図、アーキテクチャ図
- **技術スタック固定**: gRPCで通信層を安定化

---

## 📋 実施内容

### Phase 0: 現状分析・調査 (3-4日)

#### 0.1 静的解析実施 (1日)

**使用ツール**:
- Roslyn Analyzer（C#）
- Visual Studio Code Metrics
- ReSharper（利用可能なら）

**実施項目**:
- デッドコード検出
  - 使用されていないメソッド
  - 使用されていないクラス
  - 使用されていないプロパティ
  - 未参照のプライベートフィールド
- 循環依存検出
  - プロジェクト間循環依存
  - クラス間循環依存
- 複雑度測定
  - Cyclomatic Complexity > 15 のメソッド特定
  - 行数 > 500 のクラス特定
- 重複コード検出
  - コピペコード
  - 類似ロジック

**成果物**:
- `analysis_report.md` - 静的解析レポート
- `deadcode_list.md` - 削除対象コードリスト
- `refactoring_targets.md` - リファクタリング優先度リスト

#### 0.2 全体フロー調査 (1-2日)

**調査対象**:

**① キャプチャフロー**
```
ユーザー操作（Startボタン）
  ↓
StartCaptureCommand (MainWindowViewModel)
  ↓
ICaptureService.StartCaptureAsync()
  ↓
Windows Graphics Capture API (BaketaCaptureNative.dll) ← P/Invoke経由
  ↓
CaptureCompletedEvent発行
```

**調査ポイント**:
- ICaptureServiceの実装クラス特定
- SafeImageAdapter統合状況確認
- 使用されていないキャプチャ方式の特定（PrintWindow fallback等）
- **🚨 [GEMINI_CRITICAL] BaketaCaptureNative.dll連携の詳細確認**:
  - P/Invoke宣言（NativeWindowsCapture.cs）の正確性
  - メモリ管理（SafeHandle使用状況）
  - ObjectDisposedException根本原因の特定
  - SafeImageAdapter統合問題の解決方針策定

**② OCRフロー**
```
CaptureCompletedEvent
  ↓
OcrRequestHandler
  ↓
SmartProcessingPipelineService
  ↓
PaddleOcrEngine
  ↓
TimedChunkAggregator
  ↓
AggregatedChunksReadyEvent発行
```

**調査ポイント**:
- ProximityGrouping実装確認
- 段階的フィルタリングシステム統合状況
- 使用されていないフィルター特定

**③ 翻訳フロー**
```
AggregatedChunksReadyEvent
  ↓
AggregatedChunksReadyEventHandler
  ↓
StreamingTranslationService / DefaultTranslationService
  ↓
OptimizedPythonTranslationEngine (stdin/stdout)
  ↓
Python NLLB-200サーバー
```

**調査ポイント**:
- StreamingTranslationServiceの実装状況
- DefaultTranslationServiceとの関係
- 使用されていないTranslationEngine特定（MockEngine等）

**④ オーバーレイ表示フロー**
```
TranslationWithBoundsCompletedEvent
  ↓
AggregatedChunksReadyEventHandler.DisplayTranslationOverlayAsync()
  ↓
IInPlaceTranslationOverlayManager.ShowInPlaceOverlayAsync()
  ↓
CreateAndShowNewInPlaceOverlayAsync()
  ↓
InPlaceTranslationOverlayWindow.ShowInPlaceOverlayAsync()
  ↓
WIDTH_FIX実装（のはず）
```

**調査ポイント**:
- **なぜMETHOD_ENTRYログが出ないか特定** ← 重要
- AvaloniaOverlayRendererの使用状況確認
- Phase16UIOverlayModuleの完全削除確認

**⑤ 継続監視フロー**
```
StartCaptureCommand
  ↓
Timer or Task-based polling
  ↓
定期的にキャプチャ実行
  ↓
画像変化検知
  ↓
変化があればOCR実行
```

**調査ポイント**:
- 画像変化検知システムの実装状況（P0タスク）
- ポーリング間隔の最適値
- メモリリーク確認

**成果物**:
- `flow_analysis.md` - 全体フロー図（Mermaid形式）
- `architecture_issues.md` - アーキテクチャ問題リスト
- `unused_components.md` - 未使用コンポーネントリスト

#### 0.3 依存関係マッピング (1日)

**実施内容**:
```bash
# プロジェクト間依存関係可視化
dotnet list package --include-transitive > dependencies.txt

# NuGetパッケージ整理
# 使用されていないパッケージ特定
```

**調査ポイント**:
- 未使用NuGetパッケージ
- バージョン不整合
- 循環参照

**成果物**:
- `dependency_graph.png` - 依存関係図
- `unused_packages.md` - 削除可能パッケージリスト

---

### Phase 1: デッドコード削除 (2-3日)

#### 1.1 Phase 16関連コード完全削除 (0.5日)

**削除対象**:
- `Baketa.UI/DI/Modules/Phase16UIOverlayModule.cs` （確認済み無効化）
- `Baketa.UI/Services/Overlay/AvaloniaOverlayRenderer.cs` （使用状況確認後）
- 関連するPhase 16イベント・ハンドラー

**確認事項**:
- AvaloniaOverlayRendererが本当に使用されていないか
- 削除後のビルド・テスト成功確認

#### 1.2 未使用TranslationEngine削除 (0.5日)

**削除候補**:
- MockTranslationEngine（テスト用以外で使用されていない場合）
- 旧TCP通信コード（完全削除確認）
- 使用されていないファクトリーメソッド

#### 1.3 未使用キャプチャ・OCRコード削除 (0.5日)

**削除候補**:
- PrintWindow fallback（使用されていない場合）
- 旧Phase実装（Phase 3.1関連の中途半端なコード）
- 使用されていない画像フィルター

#### 1.4 未使用NuGetパッケージ削除 (0.5日)

**削除候補**:
- 旧バージョンの依存パッケージ
- 使用されていないライブラリ

**期待効果**:
- コード削減量: **2,000-3,000行**
- ビルド時間短縮
- デバッグ容易性向上

---

### Phase 2: gRPC基盤構築 (5-7日)

#### 2.1 Protoファイル設計 (1日)

**translation.proto**:
```protobuf
syntax = "proto3";

package baketa.translation;

service TranslationService {
  rpc Translate (TranslateRequest) returns (TranslateResponse);
  rpc TranslateBatch (stream BatchTranslateRequest) returns (stream BatchTranslateResponse);
  rpc CancelTranslation (CancelRequest) returns (CancelResponse);
  rpc HealthCheck (HealthCheckRequest) returns (HealthCheckResponse);
}

message TranslateRequest {
  string text = 1;
  string source_lang = 2;
  string target_lang = 3;
  string operation_id = 4;
}

message TranslateResponse {
  string translated_text = 1;
  string operation_id = 2;
  bool is_success = 3;
  string error_message = 4;
  int64 processing_time_ms = 5;
}

message BatchTranslateRequest {
  repeated string texts = 1;
  string source_lang = 2;
  string target_lang = 3;
  string batch_id = 4;
}

message BatchTranslateResponse {
  repeated string translated_texts = 1;
  string batch_id = 2;
  bool is_success = 3;
}

message CancelRequest {
  string operation_id = 1;
}

message CancelResponse {
  bool is_cancelled = 1;
}

message HealthCheckRequest {}

message HealthCheckResponse {
  bool is_healthy = 1;
  string version = 2;
}
```

#### 2.2 Python gRPCサーバー実装 (2-3日)

**ディレクトリ構成**:
```
python/
├── grpc_server/
│   ├── translation_server.py  # gRPCサーバー本体
│   ├── nllb_translator.py     # NLLB-200ラッパー
│   ├── gemini_translator.py   # Gemini APIラッパー
│   └── __init__.py
├── protos/
│   └── translation.proto
├── requirements.txt
└── start_server.py
```

**主要ファイル**: translation_server.py
```python
import grpc
from concurrent import futures
import translation_pb2
import translation_pb2_grpc
from nllb_translator import NLLBTranslator

class TranslationServicer(translation_pb2_grpc.TranslationServiceServicer):
    def __init__(self):
        self.translator = NLLBTranslator()

    def Translate(self, request, context):
        try:
            result = self.translator.translate(
                request.text,
                request.source_lang,
                request.target_lang
            )
            return translation_pb2.TranslateResponse(
                translated_text=result,
                operation_id=request.operation_id,
                is_success=True
            )
        except Exception as e:
            return translation_pb2.TranslateResponse(
                operation_id=request.operation_id,
                is_success=False,
                error_message=str(e)
            )

    def TranslateBatch(self, request_iterator, context):
        for request in request_iterator:
            yield self._translate_single(request)

    def HealthCheck(self, request, context):
        return translation_pb2.HealthCheckResponse(
            is_healthy=True,
            version="1.0.0"
        )

def serve():
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))
    translation_pb2_grpc.add_TranslationServiceServicer_to_server(
        TranslationServicer(), server
    )
    server.add_insecure_port('[::]:50051')
    server.start()
    print("gRPC server started on port 50051")
    server.wait_for_termination()
```

#### 2.3 C# gRPCクライアント実装 (2-3日)

**ディレクトリ構成**:
```
Baketa.Infrastructure/Translation/
├── Clients/
│   ├── ITranslationClient.cs          # 抽象インターフェース
│   ├── GrpcTranslationClient.cs       # gRPC実装
│   └── StdinStdoutTranslationClient.cs # 既存（移行期間中のみ）
├── Protos/
│   └── translation.proto
└── Factories/
    └── TranslationClientFactory.cs
```

**ITranslationClient.cs**:
```csharp
namespace Baketa.Infrastructure.Translation.Clients;

public interface ITranslationClient : IDisposable
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<TranslationResult>> TranslateBatchAsync(
        IEnumerable<string> texts,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default);

    Task<bool> CancelTranslationAsync(string operationId);

    Task<bool> IsHealthyAsync();
}

public record TranslationResult(
    string TranslatedText,
    string OperationId,
    bool IsSuccess,
    string? ErrorMessage = null,
    long ProcessingTimeMs = 0);
```

**GrpcTranslationClient.cs**:
```csharp
using Grpc.Net.Client;
using Grpc.Core;

namespace Baketa.Infrastructure.Translation.Clients;

public class GrpcTranslationClient : ITranslationClient
{
    private readonly GrpcChannel _channel;
    private readonly TranslationService.TranslationServiceClient _client;

    public GrpcTranslationClient(string serverAddress = "http://localhost:50051")
    {
        _channel = GrpcChannel.ForAddress(serverAddress);
        _client = new TranslationService.TranslationServiceClient(_channel);
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new TranslateRequest
            {
                Text = text,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                OperationId = Guid.NewGuid().ToString()
            };

            var response = await _client.TranslateAsync(
                request,
                deadline: DateTime.UtcNow.AddSeconds(10),
                cancellationToken: cancellationToken
            );

            return new TranslationResult(
                response.TranslatedText,
                response.OperationId,
                response.IsSuccess,
                response.ErrorMessage,
                response.ProcessingTimeMs
            );
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            return new TranslationResult(
                string.Empty,
                string.Empty,
                false,
                "Translation timeout"
            );
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _client.HealthCheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(2)
            );
            return response.IsHealthy;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
```

**期待効果**:
- stdin/stdoutと並行稼働可能なgRPCパイプライン
- OptimizedPythonTranslationEngineを完全削除可能な準備完了

---

### Phase 3: 通信層抽象化・クリーンアップ (3-4日)

#### 3.1 OptimizedPythonTranslationEngine削除 (1日)

**置き換え手順**:
```csharp
// Before
services.AddSingleton<ITranslationEngine, OptimizedPythonTranslationEngine>();

// After
services.AddSingleton<ITranslationClient, GrpcTranslationClient>();
services.AddSingleton<ITranslationEngine, GrpcTranslationEngineAdapter>();
```

**GrpcTranslationEngineAdapter.cs** (軽量アダプター):
```csharp
public class GrpcTranslationEngineAdapter : ITranslationEngine
{
    private readonly ITranslationClient _client;

    public GrpcTranslationEngineAdapter(ITranslationClient client)
    {
        _client = client;
    }

    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        var result = await _client.TranslateAsync(text, "ja", "en", ct);
        return result.IsSuccess ? result.TranslatedText : string.Empty;
    }
}
```

**削除対象**:
- `OptimizedPythonTranslationEngine.cs` (1,500行)
- `StdinStdoutTranslationClient.cs`
- TCP関連の全コード
- OperationId手動管理コード
- TaskCompletionSource複雑な制御

**期待効果**:
- コード削減: **1,500-2,000行**
- シンプルな通信層（100行程度）

#### 3.2 TranslationService階層整理 (1日)

**現状**:
- DefaultTranslationService
- StreamingTranslationService

**実施内容**:
- 両者の責任を明確化
- 重複コード削除
- 統合可能か検討

#### 3.3 stdin/stdout完全削除 (1日)

**削除対象**:
- `StdinStdoutTranslationClient.cs`
- Python stdin/stdoutサーバーコード
- 関連設定ファイル
- テストコード

---

### Phase 4: UI層リファクタリング (5-7日)

#### 4.1 InPlaceTranslationOverlayManager分割 (3-4日)

**現状**: 1,067行の単一クラス

**分割後の設計**:

**① IInPlaceOverlayFactory** (新規作成)
```csharp
public interface IInPlaceOverlayFactory
{
    InPlaceTranslationOverlayWindow CreateOverlay(TextChunk textChunk);
    void ConfigureOverlay(InPlaceTranslationOverlayWindow overlay, TextChunk textChunk);
}

public class InPlaceOverlayFactory : IInPlaceOverlayFactory
{
    public InPlaceTranslationOverlayWindow CreateOverlay(TextChunk textChunk)
    {
        return new InPlaceTranslationOverlayWindow
        {
            ChunkId = textChunk.ChunkId,
            OriginalText = textChunk.CombinedText,
            TranslatedText = textChunk.TranslatedText,
        };
    }

    public void ConfigureOverlay(
        InPlaceTranslationOverlayWindow overlay,
        TextChunk textChunk)
    {
        var overlaySize = textChunk.GetOverlaySize();

        // 🔧 [WIDTH_FIX] ここで確実に設定
        var textBlock = overlay.FindControl<TextBlock>("InPlaceTranslatedTextBlock");
        if (textBlock != null)
        {
            textBlock.MaxWidth = overlaySize.Width - 8; // Border Padding考慮
        }

        overlay.Width = overlaySize.Width;
        overlay.Position = textChunk.GetBasicOverlayPosition();
    }
}
```

**② IInPlaceOverlayManager** (簡素化)
```csharp
public class InPlaceTranslationOverlayManager : IInPlaceTranslationOverlayManager
{
    private readonly IInPlaceOverlayFactory _factory;
    private readonly IOverlayPositioningService _positioning;
    private readonly ConcurrentDictionary<int, InPlaceTranslationOverlayWindow> _overlays = new();

    public async Task ShowInPlaceOverlayAsync(
        TextChunk textChunk,
        CancellationToken ct)
    {
        // シンプルな処理フロー
        var overlay = _factory.CreateOverlay(textChunk);
        _factory.ConfigureOverlay(overlay, textChunk);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            overlay.Show();
            _overlays[textChunk.ChunkId] = overlay;
        });
    }
}
```

**期待効果**:
- InPlaceOverlayFactory.cs (200行)
- InPlaceTranslationOverlayManager.cs (300行) ← 簡素化
- IOverlayPositioningService統合強化

#### 4.2 WIDTH_FIX問題の完全解決 (1日)

**調査内容**:
1. なぜMETHOD_ENTRYログが出ないか特定
2. 実際の実行パスを確認
3. FactoryでWIDTH_FIX確実適用

**検証方法**:
- ログ出力確認
- 実際にオーバーレイ幅が固定されるか目視確認
- 複数チャンクで動作確認

#### 4.3 イベントハンドラー整理 (1-2日)

**対象**:
- AggregatedChunksReadyEventHandler
- CaptureCompletedHandler
- TranslationCompletedHandler

**整理内容**:
- 責任の明確化
- 不要なログ削除
- エラーハンドリング強化

---

### Phase 5: 統合テスト・検証 (2-3日)

#### 5.1 機能テスト (1日)
- キャプチャ → OCR → 翻訳 → オーバーレイ表示
- キャンセル動作確認
- タイムアウト動作確認
- WIDTH_FIX動作確認

#### 5.2 パフォーマンステスト (1日)
- gRPC vs stdin/stdout比較
- メモリリーク確認
- CPU使用率測定

#### 5.3 回帰テスト (1日)
- 既存1,300+テストケース実行
- 新規テストケース追加

---

## 📊 期待効果

| 項目 | 現在 | Phase 5完了後 |
|------|------|--------------|
| **総コード行数** | ~15,000行 | **~10,000行** (-33%) |
| **OptimizedPythonTranslationEngine** | 1,500行 | **削除** |
| **InPlaceTranslationOverlayManager** | 1,067行 | **500行** |
| **デッドコード** | 不明 | **完全削除** |
| **タイムアウト問題** | あり | **解決** |
| **WIDTH_FIX問題** | 不明 | **解決** |
| **技術的負債** | 高 | **極めて低** |
| **テスト容易性** | 困難 | **容易** |
| **保守性** | 低 | **高** |

---

## 🎯 最終成果物

1. ✅ **クリーンなアーキテクチャ**
2. ✅ **gRPC基盤（業界標準）**
3. ✅ **デッドコード完全削除**
4. ✅ **WIDTH_FIX問題解決**
5. ✅ **全フロー最適化**
6. ✅ **ドキュメント完備**

---

## ⚠️ リスク評価とMitigation Strategy

### 1. BaketaCaptureNative.dll連携リスク (Gemini指摘)

**リスク**: ネイティブDLLとの通信がgRPC移行の影響を受ける可能性

**実態**:
- ネイティブDLLとの通信は**P/Invoke経由**であり、gRPC移行と**無関係**
- Pythonサーバーとの通信のみがstdin/stdout → gRPCに変更される
- キャプチャフロー: C# → P/Invoke → C++/WinRT DLL (変更なし)
- 翻訳フロー: C# → gRPC → Python (変更あり)

**Mitigation**:
- Phase 0.2でP/Invoke宣言の正確性を再確認
- SafeImageAdapter統合問題（ObjectDisposedException）を優先解決
- ネイティブDLL連携の単体テストを追加

**リスクレベル**: 🟡 中（実際の影響は限定的だが、確認必須）

### 2. gRPC移行に伴うパフォーマンスリスク

**リスク**: stdin/stdoutより遅い可能性

**Mitigation**:
- Phase 5.2でパフォーマンステスト実施
- Protobufバイナリシリアライゼーションによる高速化期待
- HTTP/2による効率的な通信

**リスクレベル**: 🟢 低（理論上は高速化が期待できる）

### 3. Phase 1デッドコード削除での影響範囲

**リスク**: 意図しない機能削除

**Mitigation**:
- Phase 0.1の静的解析で慎重に特定
- 削除前に全テストケース実行
- 削除後も全テストケース実行

**リスクレベル**: 🟡 中（静的解析とテストで管理可能）

### 4. WIDTH_FIX問題の根本原因不明

**リスク**: Factory Patternで解決できない可能性

**Mitigation**:
- Phase 0.2でMETHOD_ENTRYログが出ない原因を完全特定
- 実行フローを完全に可視化してから実装着手

**リスクレベル**: 🟡 中（調査次第で解決可能）

---

## 🔗 関連ドキュメント

- [grpc.md](C:\Users\suke0\OneDrive\デスクトップ\grpc.md) - gRPC vs stdin/stdout比較
- [CLAUDE.local.md](../../CLAUDE.local.md) - Phase 12.2問題の詳細
- [CLAUDE.md](../../CLAUDE.md) - プロジェクト概要

---

## 📝 備考

- **リリース前**: まだリリースしていないため、ユーザー体験より技術的負債清算を優先
- **技術スタック更新頻度**: 短期間での変更により負債蓄積
- **gRPC移行理由**: 業界標準プロトコルで「最後の通信層変更」にする
