# NLLB-200並列処理改善 実装チェックリスト

## 📋 実装前チェック

### 環境準備
- [ ] Visual Studio 2022 または VS Code が利用可能
- [ ] .NET 8 SDK がインストール済み
- [ ] Git作業ブランチの準備完了
- [ ] Baketa.sln がビルド可能な状態

### ファイル確認
- [ ] `OcrCompletedHandler_Improved.cs` が作成済み
- [ ] `NLLB200_並列処理改善設計.md` が作成済み
- [ ] `NLLB200_CONCURRENCY_SOLUTION.md` が作成済み
- [ ] `implement_nllb200_fix.ps1` が作成済み

## 🔧 実装手順

### Phase 1: 基盤準備

#### 1.1 NuGetパッケージ追加
```bash
cd E:\dev\Baketa
dotnet add Baketa.Core package System.Threading.Tasks.Dataflow --version 8.0.0
```

**確認方法**:
```xml
<!-- Baketa.Core.csproj に以下が追加されていること -->
<PackageReference Include="System.Threading.Tasks.Dataflow" Version="8.0.0" />
```

#### 1.2 ビルド確認
```bash
dotnet build Baketa.sln --configuration Debug
```
- [ ] ビルドが成功することを確認
- [ ] 警告やエラーがないことを確認

### Phase 2: コア実装

#### 2.1 BatchTranslationRequestEvent サポート追加

**対象ファイル**: `Baketa.Core\Events\Handlers\TranslationRequestHandler.cs`

**追加コード**:
```csharp
// IEventProcessor<BatchTranslationRequestEvent> を実装に追加
public class TranslationRequestHandler : 
    IEventProcessor<TranslationRequestEvent>, 
    IEventProcessor<BatchTranslationRequestEvent>
{
    // 既存メソッドは保持

    // 新規追加
    public async Task HandleAsync(BatchTranslationRequestEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        if (!eventData.Requests.Any())
        {
            return;
        }

        // バッチ内の各翻訳要求を処理
        var tasks = eventData.Requests.Select(request => 
            HandleAsync(request) // 既存の個別処理メソッドを再利用
        );

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
```

#### 2.2 サービス登録の更新

**対象ファイル**: `Baketa.Core\DI\Modules\ServiceModuleCore.cs`

**変更内容**:
```csharp
// 既存登録をコメントアウト
// services.AddTransient<IEventProcessor<OcrCompletedEvent>, OcrCompletedHandler>();

// 改善版を登録
services.AddTransient<IEventProcessor<OcrCompletedEvent>, OcrCompletedHandlerImproved>();

// バッチイベント処理の登録も追加
services.AddTransient<IEventProcessor<BatchTranslationRequestEvent>, TranslationRequestHandler>();
```

#### 2.3 名前空間とusing追加

**OcrCompletedHandler_Improved.cs** の先頭に必要な using を確認:
```csharp
using System.Threading.Tasks.Dataflow;
```

### Phase 3: テスト実行

#### 3.1 ビルドテスト
```bash
dotnet build Baketa.sln --configuration Debug
```
- [ ] エラーなしでビルド完了
- [ ] 新しい依存関係が正常に解決

#### 3.2 単体テスト（オプション）
```bash
dotnet test tests/Baketa.Core.Tests/ --filter "OcrCompletedHandler"
```

#### 3.3 統合テスト
- [ ] アプリケーションの起動確認
- [ ] OCR実行時のエラーログなし
- [ ] 翻訳結果の正常表示

### Phase 4: パフォーマンステスト

#### 4.1 エラー率測定
**測定項目**:
- NLLB-200 "Already borrowed" エラーの発生頻度
- 翻訳要求の失敗率
- システム全体の安定性

**目標値**:
- エラー率 < 5%
- 翻訳成功率 > 95%

#### 4.2 レスポンス時間測定
**測定項目**:
- OCR完了から翻訳結果表示までの時間
- バッチ処理の平均待ち時間
- 並列処理のスループット

**目標値**:
- 初回表示 < 100ms
- 平均処理時間の30%改善

## 🔍 トラブルシューティング

### よくある問題と対策

#### 問題1: System.Threading.Tasks.Dataflow が見つからない
**解決策**:
```bash
dotnet restore
dotnet clean
dotnet build
```

#### 問題2: バッチイベントが処理されない
**確認点**:
- [ ] `IEventProcessor<BatchTranslationRequestEvent>` の実装
- [ ] サービス登録の追加
- [ ] イベント発行側のコード

#### 問題3: パフォーマンスが改善されない
**確認点**:
- [ ] 設定パラメーターの調整（BatchSize, MaxParallelism）
- [ ] NLLB-200サーバーの起動状態
- [ ] ネットワーク接続の安定性

## 📊 検証方法

### ログ監視
**監視対象**:
```bash
# アプリケーション実行中に以下のログを監視
grep -i "already borrowed" logs/application.log
grep -i "batch" logs/application.log
grep -i "translation.*complete" logs/application.log
```

### メトリクス収集
**収集項目**:
- 翻訳要求数 vs 成功数
- 平均処理時間
- エラー発生パターン
- リソース使用量（CPU, Memory）

## ✅ 完了条件

### 必須条件
- [ ] ビルドエラーなし
- [ ] NLLB-200エラー率 < 5%
- [ ] 翻訳結果の正常表示
- [ ] 既存機能の動作保証

### 品質条件
- [ ] コードレビュー完了
- [ ] 単体テストの追加（可能であれば）
- [ ] パフォーマンステスト結果記録
- [ ] ドキュメント更新

---

**実装担当者**: _______________  
**レビュー担当者**: _______________  
**完了予定日**: _______________  
**実際完了日**: _______________