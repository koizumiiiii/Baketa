import google.generativeai as genai
import os
import sys

# UTF-8 encoding fix for Windows
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# API key configuration  
api_key = os.environ.get('GEMINI_API_KEY')
if not api_key:
    print('Error: GEMINI_API_KEY environment variable not set')
    sys.exit(1)

genai.configure(api_key=api_key)

# Inline analysis content
analysis_content = '''# UltraThink調査結果: 画面変化検知スキップ問題の根本原因特定

## 🎯 問題概要

**ユーザー報告**:
1. 画面変化がないにもかかわらず翻訳処理が実行され続ける
2. 画面変化検知が動作していないのでは？
3. OCR実施前に画面変化検知を行うことはできないのか？

## 🔥 根本原因100%特定

**SmartProcessingPipelineService設計**:
- ImageChangeDetection → OcrExecution → TextChangeDetection → TranslationExecution の順序で実行される設計

**実際のコード**:
```csharp
// CoordinateBasedTranslationService.ProcessWithCoordinateBasedTranslationAsync
var textChunks = await _processingFacade.OcrProcessor.ProcessBatchAsync(
    image, windowHandle, cancellationToken).ConfigureAwait(false);
```

**問題**: SmartProcessingPipelineServiceを経由せず、直接BatchOcrProcessorを呼び出している
→ ImageChangeDetectionStageStrategyが実行されない

## 💡 解決策提案: SmartProcessingPipelineService統合

**修正後のアーキテクチャ**:
```
CoordinateBasedTranslationService
  → SmartProcessingPipelineService.ExecuteAsync()
    → ImageChangeDetectionStageStrategy ← 自動実行
    → OcrExecutionStageStrategy
    → TranslationExecutionStageStrategy
```

**実装タスク**:
1. ProcessingPipelineInput構築
2. SmartProcessingPipelineService呼び出し
3. 早期終了ハンドリング（画面変化なし時）

**期待効果**:
- 画面変化検知: スキップ → 自動実行
- 不要なOCR: 常に実行 → 画面変化時のみ
- 処理削減率: 0% → 90%削減（3段階フィルタリング）
- Clean Architecture: 違反 → 準拠

**実装時間**: 3-4時間
**リスク**: ⭐⭐ (低〜中)
'''

# Create the model
model = genai.GenerativeModel('gemini-2.0-flash-exp')

# Prepare the prompt
prompt = f'''あなたは.NET/C#とClean Architectureの専門家です。以下のUltraThink調査結果を詳細にレビューし、フィードバックを提供してください。

{analysis_content}

以下の観点から包括的なレビューをお願いします:

1. **アーキテクチャ設計レビュー**
   - SmartProcessingPipelineService統合のアプローチは妥当か？
   - Clean Architecture原則への準拠性は？

2. **パフォーマンス影響分析**
   - パイプライン経由に変更することでのパフォーマンス影響は？
   - 3段階フィルタリング（90%削減）の効果は座標ベース翻訳でも有効か？

3. **実装優先度の妥当性**
   - この修正はP0/P1/P2のどれに該当するか？
   - 他の問題（2回目翻訳失敗、gRPC接続問題）との優先順位は？

4. **実装戦略の改善提案**
   - より良いアプローチはあるか？
   - 段階的実装の推奨は？

**フィードバック形式**:
- 各質問に対する明確な回答
- P0/P1/P2の優先度判定
- 総合評価（5段階）

日本語で回答してください。
'''

# Generate response
print('🤖 Gemini 2.0 Flash Experimental にレビュー依頼中...\n')
response = model.generate_content(prompt)
print(response.text)
