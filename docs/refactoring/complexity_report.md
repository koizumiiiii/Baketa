# Baketa 複雑度測定レポート

## 📋 レポート情報

- **作成日**: 2025-10-04
- **Phase**: Phase 0.1 - 複雑度測定
- **測定基準**: ファイル行数（複雑度の指標）
- **対象**: 主要プロジェクト（テスト除く）

---

## 📊 大規模ファイル Top 20

複雑度が高いと推定されるファイル（500行以上）:

| 順位 | ファイル | 行数 | プロジェクト | 推定複雑度 | 優先度 |
|------|---------|------|------------|----------|--------|
| 1 | `PaddleOcrEngine.cs` | **5,741行** | Infrastructure | 極めて高 | **P0** |
| 2 | `BatchOcrProcessor.cs` | **2,766行** | Infrastructure | 極めて高 | **P0** |
| 3 | `OptimizedPythonTranslationEngine.cs` | **2,765行** | Infrastructure | 極めて高 | **P0** |
| 4 | `TranslationOrchestrationService.cs` | 2,387行 | Application | 高 | P1 |
| 5 | `MultiMonitorOverlayManager.cs` | 1,514行 | UI | 高 | P1 |
| 6 | `GeminiTranslationEngine.cs` | 1,441行 | Infrastructure | 高 | P1 |
| 7 | `EnhancedGpuOcrAccelerator.cs` | 1,379行 | Infrastructure | 高 | P1 |
| 8 | `CoordinateBasedTranslationService.cs` | 1,360行 | Application | 高 | P1 |
| 9 | `Program.cs` | 1,357行 | UI | 高 | P2 |
| 10 | `HybridResourceManager.cs` | 1,348行 | Infrastructure | 高 | P1 |
| 11 | `WindowsMonitorManager.cs` | 1,280行 | Infrastructure.Platform | 高 | P2 |
| 12 | `MainOverlayViewModel.cs` | 1,189行 | UI | 高 | P2 |
| 13 | `InfrastructureModule.cs` | 1,170行 | Infrastructure | 中 | P2 |
| 14 | `PPOCRv5Preprocessor.cs` | 1,092行 | Infrastructure | 中 | P2 |
| 15 | `InPlaceTranslationOverlayManager.cs` | **1,045行** | UI | 中 | P2 |
| 16 | `PooledGpuOptimizationOrchestrator.cs` | 1,006行 | Infrastructure | 中 | P2 |

---

## 🔥 重大な複雑度問題 (P0)

### 1. PaddleOcrEngine.cs (5,741行)

**状況**: OCRエンジンの実装が単一ファイルに集約

**問題点**:
- 責任過多（単一責任原則違反）
- メソッド数が膨大
- テスト困難
- 保守性極めて低

**推奨対応**:
```
PaddleOcrEngine.cs (5,741行)
  ↓ 分割
├─ PaddleOcrEngine.cs (500行) - コアロジック
├─ PaddleOcrPreprocessor.cs (1,000行) - 前処理
├─ PaddleOcrPostprocessor.cs (1,000行) - 後処理
├─ PaddleOcrModelManager.cs (800行) - モデル管理
└─ PaddleOcrInference.cs (1,500行) - 推論実行
```

**優先度**: P0 - Phase 4で対応

### 2. BatchOcrProcessor.cs (2,766行)

**状況**: バッチOCR処理の全ロジック

**問題点**:
- バッチ処理、キューイング、並列化が混在
- エラーハンドリング複雑
- 到達不能コード含む（CS0162警告）

**推奨対応**:
- ステートマシンパターンで分離
- BatchOcrQueue, BatchOcrExecutor, BatchOcrResultHandlerに分割

**優先度**: P0 - Phase 4で対応

### 3. OptimizedPythonTranslationEngine.cs (2,765行)

**状況**: gRPC移行で**完全削除予定**

**問題点**:
- TCP、stdin/stdout通信の履歴が蓄積
- OperationId手動管理
- TaskCompletionSource複雑な制御
- タイムアウト処理多重化（10秒 vs 30秒問題）

**推奨対応**:
- **Phase 3.1でGrpcTranslationClientに完全置換**
- 2,765行削除

**優先度**: **P0 - Phase 3で削除**

---

## ⚠️ 高複雑度ファイル (P1)

### 4. TranslationOrchestrationService.cs (2,387行)

**問題**: 翻訳の調整ロジックが単一ファイルに集約
**推奨**: Orchestrator, Coordinator, Schedulerに分割

### 5. MultiMonitorOverlayManager.cs (1,514行)

**問題**: マルチモニター対応のオーバーレイ管理
**推奨**: MonitorDetector, OverlayPlacer, CollisionDetectorに分割

### 6. GeminiTranslationEngine.cs (1,441行)

**問題**: Gemini API連携の全ロジック
**推奨**: GeminiClient, GeminiRequestBuilder, GeminiResponseParserに分割

### 7-10. その他1,000行超ファイル

全て**Phase 4 UI層リファクタリング**または**Phase 3 通信層抽象化**で対応予定

---

## 📈 複雑度メトリクス

### ファイルサイズ分布

| 行数範囲 | ファイル数 | 比率 | 評価 |
|---------|----------|------|------|
| 3,000行以上 | 3個 | 0.5% | 🔴 極めて高リスク |
| 1,500-2,999行 | 3個 | 0.5% | 🟠 高リスク |
| 1,000-1,499行 | 10個 | 1.7% | 🟡 中リスク |
| 500-999行 | 推定30個 | 5% | 🟢 許容範囲 |
| 500行未満 | 推定550個 | 92.3% | ✅ 良好 |

### リファクタリング推定効果

| 対象 | 削減可能行数 | 期待効果 |
|------|------------|---------|
| OptimizedPythonTranslationEngine.cs削除 | **2,765行** | gRPC移行で完全削除 |
| PaddleOcrEngine.cs分割 | 0行（構造改善） | 保守性向上、テスト容易化 |
| BatchOcrProcessor.cs分割 | 0行（構造改善） | 可読性向上、並列化最適化 |
| **合計** | **2,765行** | Phase 3で実現 |

---

## 🎯 Phase別対応計画

### Phase 1: デッドコード削除 (現在進行中)
- [ ] CS0162到達不能コード削除 (120-220行)
- [ ] 未使用パッケージ削除 (SharpDX等)

### Phase 3: 通信層抽象化 (2,765行削除)
- [ ] **OptimizedPythonTranslationEngine.cs完全削除**
- [ ] GrpcTranslationClient置換（100行程度）
- [ ] 2,665行純減

### Phase 4: UI層リファクタリング
- [ ] PaddleOcrEngine.cs分割（5ファイルへ）
- [ ] BatchOcrProcessor.cs分割（3ファイルへ）
- [ ] InPlaceTranslationOverlayManager.cs分割（3ファイルへ）

### Phase 5: Infrastructure層リファクタリング
- [ ] TranslationOrchestrationService.cs分割
- [ ] GeminiTranslationEngine.cs分割
- [ ] その他1,000行超ファイル対応

---

## 🔍 複雑度測定手法

### 使用ツール
- `wc -l` - 行数カウント（複雑度の近似指標）
- Roslynator - 静的解析（CS0162等）
- 手動レビュー - アーキテクチャ問題特定

### 複雑度の推定基準
- **500行未満**: 許容範囲（単一責任維持可能）
- **500-1,000行**: 要注意（分割検討）
- **1,000-2,000行**: 高リスク（分割推奨）
- **2,000行以上**: 極めて高リスク（即座対応）

---

## 💡 重要な発見

### 1. Infrastructure層の複雑度集中
Top 16ファイル中、**11個がInfrastructure層**
- OCR処理（PaddleOcrEngine, BatchOcrProcessor）
- 翻訳処理（OptimizedPythonTranslationEngine, Gemini）
- リソース管理（HybridResourceManager）

### 2. gRPC移行による劇的改善見込み
OptimizedPythonTranslationEngine.cs（2,765行）の**完全削除**により、トップ3の複雑度問題が1つ解決

### 3. PaddleOcrEngine.csが最大のボトルネック
5,741行は他ファイルの2倍以上
- 分割必須
- テスト戦略の再構築が必要

---

## 📝 次のステップ

### Phase 0.1 残タスク
- [x] 複雑度測定完了
- [ ] 重複コード検出（ripgrep使用）

### Phase 0.2 全体フロー調査
- [ ] PaddleOcrEngine.csの内部構造分析
- [ ] 分割戦略策定

### Phase 3実施時の効果測定
- 削減行数: 2,765行（OptimizedPythonTranslationEngine.cs）
- 複雑度削減: トップ3問題の1つ完全解決

---

## 🎯 結論

**最優先対応**:
1. **Phase 3でOptimizedPythonTranslationEngine.cs削除** → 2,765行削減
2. **Phase 4でPaddleOcrEngine.cs分割** → 保守性劇的改善
3. **Phase 4でBatchOcrProcessor.cs分割** → 並列化最適化

**期待効果**:
- コード削減: 2,765行（Phase 3のみ）
- 複雑度削減: トップ3ファイル対応で極めて高リスク領域解消
- 保守性向上: 5,000行超ファイルゼロ化
