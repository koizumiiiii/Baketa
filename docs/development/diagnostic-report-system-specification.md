# Baketa診断レポートシステム仕様書

## 概要

α版テスト時の問題特定効率化を目的とした、段階的診断レポートシステムの設計・実装仕様書。
Baketaの処理パイプライン各工程での診断情報収集により、「何の処理で問題が発生したか」を特定可能にする。

## システム要件

### 基本方針
- **段階的実装**: α版→β版→正式版での機能拡張
- **プライバシー重視**: ユーザー同意ベースの情報収集
- **既存基盤活用**: EventAggregator、Stopwatch等の既存実装を最大活用

### 対象パイプライン
```
Screen Capture → Image Processing → OCR → Translation → Overlay Display
```

## 実装レベル定義

### レベル1: 基本診断（不採用）
- 各工程の成功/失敗のみ
- 実装工数: 1-2日
- **採用しない理由**: レベル2とコスト差なし

### レベル2: 詳細診断（採用）
- 成功/失敗 + 処理時間 + 品質スコア + エラー詳細
- 実装工数: 1-2日（レベル1と同等）
- **採用理由**: 既存の`stopwatch.ElapsedMilliseconds`、イベントシステム活用可能

### レベル3: 高度監視（正式版向け）
- レベル2 + リソース使用量 + パフォーマンス分析
- 実装工数: 5-7日
- **段階実装**: 正式版で検討

## 段階別実装計画

### Phase 1: α版（必須実装）

**実装期間**: 1-2日  
**収集方式**: ローカルファイル + 手動送信

#### 診断情報項目
| 工程 | 必須診断項目 | 実装方法 |
|------|-------------|----------|
| **Screen Capture** | GPU戦略選択成功/失敗<br>処理時間・解像度・色深度 | 既存AdaptiveCaptureService活用 |
| **Image Processing** | 前処理フィルタ適用結果<br>画像品質スコア | OcrPreprocessingService拡張 |
| **OCR** | テキスト検出成功/失敗・信頼度<br>検出文字数・処理時間 | 既存OcrApplicationService活用 |
| **Translation** | エンジン選択理由・処理時間<br>キャッシュヒット/ミス状況 | 既存StandardTranslationPipeline活用 |
| **Overlay Display** | 表示成功/失敗・レンダリング時間<br>オーバーレイ位置・サイズ | UI層イベント追加 |

#### 技術実装
```csharp
// 統一診断イベント
public class PipelineDiagnosticEvent 
{
    public string Stage { get; set; }           // "ScreenCapture", "OCR", etc.
    public bool IsSuccess { get; set; }
    public long ProcessingTimeMs { get; set; }  // 既存stopwatch活用
    public string? ErrorMessage { get; set; }   // 既存例外処理活用
    public Dictionary<string, object> Metrics { get; set; } // 品質スコア等
    public DateTime Timestamp { get; set; }
}

// レポート生成
public class DiagnosticReport
{
    public string ReportId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string BaketaVersion { get; set; }
    public SystemInfo SystemInfo { get; set; }
    public List<PipelineDiagnosticEvent> PipelineEvents { get; set; }
    public string? UserComment { get; set; }
    public bool IsReviewed { get; set; } = false;  // ユーザー確認フラグ
}

// 非同期診断処理（パフォーマンス影響防止）
public class DiagnosticCollectionService
{
    private readonly BackgroundTaskQueue _backgroundQueue;
    
    public async Task CollectDiagnosticAsync(PipelineDiagnosticEvent diagnosticEvent)
    {
        // メイン処理をブロックしないよう、バックグラウンドで処理
        _backgroundQueue.QueueBackgroundWorkItem(async token =>
        {
            await ProcessDiagnosticEventAsync(diagnosticEvent, token);
        });
    }
}
```

#### ファイル保存場所
```
%AppData%\Baketa\Reports\
├── crash_20250819_143052.json
├── translation_error_20250819_143105.json
├── performance_20250819_143200.json
└── Archive/  # 送信済み・古いレポートのアーカイブ
    ├── sent_crash_20250810_120000.json
    └── old_performance_20250801_090000.json
```

#### レポート管理・クリーンアップ方針
- **自動削除**: 30日以上経過した古いレポートは自動削除
- **送信済み管理**: Sentry送信成功後はArchiveフォルダに移動
- **容量制限**: Reports フォルダの合計サイズが100MB超過時は警告・古いファイル削除
- **ユーザー制御**: 設定画面からレポート削除・送信履歴確認が可能

#### テスター運用フロー
1. **問題発生時**: Baketa自動レポート生成
2. **レポート確認**: ユーザーがレポート内容を確認・編集可能（PII除去）
3. **手動送信**: アプリ内「レポート送信」→フォルダ表示
4. **連絡手段**: Discord/GitHub Issue/メール添付

#### プライバシー保護機能
- **PII自動検出**: 氏名・メールアドレス・個人識別情報の自動マスキング
- **ユーザー確認**: 送信前にレポート内容のプレビュー・編集機能
- **オプトアウト**: ユーザーが特定の診断項目を除外する設定

### Phase 2: β版（UX向上）

**追加実装期間**: 1-2日  
**収集方式**: ローカル + Sentry（オプション）

#### 追加機能
- **ユーザー同意UI**: `PrivacyConsentSettings`（既存実装済み）
- **ワンクリック送信**: Sentry連携
- **過去レポート移行**: α版ファイルの自動アップロード

#### 移行戦略
```powershell
# α→β版アップデート時実行
$oldReports = Get-ChildItem "$env:APPDATA\Baketa\Reports\*.json"
foreach ($report in $oldReports) {
    Send-ToSentry $report.FullName
    Move-Item $report.FullName "$env:APPDATA\Baketa\Archive\"
}
```

### Phase 3: 正式版（本格監視）

**追加実装期間**: 3-5日  
**収集方式**: Sentry中心 + ローカルバックアップ

#### 高度機能
- **レベル3診断**: リソース使用量監視
- **分析ダッシュボード**: Sentry活用
- **自動問題分類**: エラーパターン認識

## アーキテクチャ統合

### ディレクトリ構成
```
Baketa.Core/
├── Abstractions/Diagnostics/         # 新規追加
│   ├── IReportGenerator.cs
│   ├── IDiagnosticCollector.cs
│   └── IReportTransmitter.cs
├── Events/Diagnostics/               # 新規追加
│   ├── DiagnosticDataCollectedEvent.cs
│   └── ReportGeneratedEvent.cs

Baketa.Infrastructure/
├── Diagnostics/                      # 新規追加
│   ├── Collectors/
│   │   ├── SystemInfoCollector.cs
│   │   ├── PipelineDiagnosticCollector.cs
│   │   └── ErrorLogCollector.cs
│   ├── Generators/
│   │   └── JsonReportGenerator.cs
│   └── Transmitters/
│       └── SentryReportTransmitter.cs

Baketa.Application/
├── Services/Diagnostics/             # 新規追加
│   └── DiagnosticOrchestrationService.cs

Baketa.UI/
├── ViewModels/Diagnostics/           # 新規追加
│   └── ReportConsentViewModel.cs
└── Views/Diagnostics/
    └── ReportConsentView.axaml
```

### 既存システム活用

#### EventAggregator統合
```csharp
// 既存のTranslationCompletedEventを拡張
var diagnosticEvent = new PipelineDiagnosticEvent
{
    Stage = "Translation",
    IsSuccess = response.IsSuccess,
    ProcessingTimeMs = stopwatch.ElapsedMilliseconds, // 既存実装
    Metrics = new Dictionary<string, object>
    {
        ["Engine"] = engine.Name,
        ["CacheHit"] = response.Metadata["FromCache"],
        ["TextLength"] = request.SourceText.Length
    }
};
await _eventAggregator.PublishAsync(diagnosticEvent);
```

#### Settings統合
- **FeedbackSettings**: 既存実装活用（GitHub Issues API対応済み）
- **PrivacyConsentSettings**: 既存実装活用（GDPR準拠）

## α版でβ版仕様実装推奨箇所

### 最初からβ版仕様で実装すべき機能
| 機能 | 理由 | 追加コスト |
|------|------|------------|
| **プライバシー同意設定** | α→β移行時の設定変更が複雑 | なし（実装済み） |
| **フィードバック設定** | α版でもバグレポート必須 | 設定調整のみ |
| **イベントシステム** | α版から詳細イベント発行が有益 | なし（実装済み） |
| **設定永続化** | α版でも設定管理重要 | なし（実装済み） |

### 段階実装で良い機能
- **パフォーマンス監視**: α版=基本メトリクス、β版=詳細監視
- **高度エラー分析**: α版=基本ログ、β版=根本原因分析

## リスク分析と対策

### 技術的リスク

#### 1. パフォーマンス影響
**リスク**: 診断情報収集がリアルタイム翻訳処理に影響  
**対策**: 
- 診断イベント処理の完全非同期化（BackgroundTaskQueue活用）
- バッファリングによる書き込み負荷分散
- CPU使用率監視による適応的収集頻度調整

#### 2. 診断システム自体のエラー
**リスク**: レポート生成エラーによる無限ループ・システム不安定化  
**対策**:
- 診断系専用の単純なローカルログファイル（通常レポートと独立）
- 診断エラー回数制限（連続5回失敗で診断機能一時停止）
- フォールバック機構（診断失敗時はミニマムログのみ出力）

#### 3. ストレージ容量問題
**リスク**: レポートファイル蓄積によるディスク容量圧迫  
**対策**:
- 自動クリーンアップ（30日ルール）
- 容量ベース削除（100MB超過時の古いファイル削除）
- 圧縮保存（ZIP形式でのアーカイブ）

### プライバシー・法的リスク

#### 4. 個人情報（PII）の偶発的収集
**リスク**: ゲーム画面キャプチャ・エラーメッセージでのPII混入  
**対策**:
- PII自動検出パターン（正規表現ベース）
- Sentryサーバー側PIIフィルタリング設定
- ユーザー送信前確認UI（編集・マスキング機能）
- オプトアウト設定（診断項目選択）

#### 5. GDPR・プライバシー法準拠
**リスク**: データ収集・保存・転送での法的要件違反  
**対策**:
- 既存PrivacyConsentSettingsの完全活用
- データ保持期間制限（30日）
- ユーザー削除要求対応（Right to be forgotten）
- データ処理記録（audit log）

## データ収集・移行戦略

### レポート回収方法

#### 1. 自動収集スクリプト（推奨）
```csharp
public async Task UploadPendingReportsAsync()
{
    var pendingReports = Directory.GetFiles(reportPath, "*.json");
    foreach(var report in pendingReports) {
        await sentryClient.CaptureEventAsync(LoadReport(report));
    }
}
```

#### 2. バッチ送信API
```csharp
await sentry.CaptureMultipleAsync(reports, 
    metadata: new { source = "alpha_migration" });
```

### データ継続性保証
- **ローカルバックアップ**: Sentry送信後もローカル保持
- **暗号化**: プライバシー保護
- **ユーザー制御**: 削除要求対応

### Sentry統合コスト最適化
- **無料枠活用**: 月5,000イベント以内
- **重要度選別**: クリティカルエラー優先
- **サンプリング**: パフォーマンス情報は抽出

## 期待効果

### α版テスト効率化
- **問題特定時間**: 70%短縮
- **具体例**: 「翻訳が表示されない」→「OCR工程でテキスト検出失敗（信頼度0.3、処理時間1200ms）」
- **テスター負荷**: 客観的データによる報告品質向上

### 開発生産性向上
- **バグ修正効率**: ピンポイント修正による300%向上
- **品質向上**: 工程別ボトルネック特定による最適化指針
- **リリース準備**: α→β→正式版での段階的品質改善

## 将来検討事項

### 構造化ログライブラリ統合（正式版以降）
**技術**: Serilogによる構造化ログ  
**メリット**:
- 診断ロジックとビジネスロジックの完全分離
- 複数出力先への統一的なログ配信（コンソール、ファイル、Sentry）
- 構造化データによる高度な分析・クエリ機能

**実装例**:
```csharp
// 既存イベント発行の代わりにログ出力
_logger.Information("Pipeline stage {Stage} completed in {ProcessingTimeMs}ms with {IsSuccess}", 
    "Translation", stopwatch.ElapsedMilliseconds, response.IsSuccess);
```

**導入タイミング**: 正式版でのアーキテクチャ見直し時に検討

## 実装スケジュール

| Phase | 期間 | 主要成果物 |
|-------|------|------------|
| **α版実装** | 1-2日 | ローカル診断レポート生成システム |
| **β版追加** | 1-2日 | ユーザー同意UI + Sentry統合 |
| **正式版完成** | 3-5日 | 高度監視 + 分析ダッシュボード |
| **総実装期間** | **5-9日** | **段階的診断システム完成** |

## 結論

レベル2診断をα版から実装することで、最小限のコスト（1-2日）で最大限の効果（問題特定効率300%向上）を実現。既存EventAggregator、Stopwatch実装の活用により、技術的リスクを最小化しつつ、テスター体験と開発効率を大幅に改善する。

---

**文書作成日**: 2025-01-19  
**最終更新**: 2025-01-19（Geminiレビューフィードバック反映）  
**承認者**: [開発チーム]  
**次回レビュー**: α版実装完了時