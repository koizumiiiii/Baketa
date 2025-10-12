# UltraThink Phase 5.2J - gRPCサーバークラッシュ & 画面変化検知調査

## 🎯 調査目的

### 3つの調査課題
1. **gRPCサーバークラッシュ原因**: PID 31640 (ポート50051) が起動後にクラッシュした理由
2. **PID 3736誤認識**: 全く別のサーバー (ポート5556) が存在した理由
3. **画面変化検知の妥当性**: 2回目翻訳でチャンク数0が6秒かかった理由

---

## 🔍 Phase 1: 証拠の整理

### 1.1 タイムライン完全再構築

```
[08:11:54.047] PID 31640 サーバー起動 (ポート50051)
[08:11:54.067] ✅ 翻訳サーバー準備完了 - StartButton有効化
[08:11:55.933] ✅ 翻訳成功: 'フリッツ「へい！・らっしゃい..' → 'Fritz said, "Hey!'
  ↓
[08:11:58.780] 2回目翻訳開始 (6秒処理時間)
[08:12:05.054] OCR完了 - チャンク数: 0
  ↓
[08:12:07.034] Stopボタン押下
[08:12:07.115] 翻訳停止処理完了（サーバー停止ログなし）
  ↓ 【3分33秒の空白期間】
  ↓
[08:15:56.407] 再翻訳試行 → ❌ UNAVAILABLE (ポート50051接続拒否)
```

### 1.2 PIDとポートの関係

| PID | ポート | 用途 | 状態 |
|-----|--------|------|------|
| 31640 | 50051 | Baketa翻訳サーバー | ❌ クラッシュ（時刻不明） |
| 3736 | 5556 | 別サーバー（用途不明） | ✅ 動作中（Baketaと無関係） |

---

## 🧠 Phase 2: 課題1 - gRPCサーバークラッシュ原因調査 ✅ 完了

### 2.1 クラッシュ発生時刻の推定

**確実な情報**:
- [08:11:55.933] 翻訳成功 → サーバー正常動作
- [08:15:56.407] 接続拒否 → サーバー停止済み

**推定クラッシュ時刻**:
- Option A: [08:12:07~08:15:56] の間（Stopボタン押下後） ← **最も可能性高い**
- Option B: [08:11:56~08:12:05] の間（2回目翻訳中）
- Option C: [08:12:05~08:12:07] の間（2回目翻訳完了後）

### 2.2 Pythonサーバーログ調査結果 ✅

**ファイル**: `E:\dev\Baketa\Baketa.UI\bin\Debug\net8.0-windows10.0.19041.0\python_stderr_port50051.log`

**内容**:
```
[08:11:52.996] [SERVER_START]
```

**結論**:
- ✅ サーバー正常起動
- ❌ クラッシュのスタックトレースやエラーメッセージが**一切記録されていない**
- ❌ stdout側ログファイルは存在しない
- 💡 サーバーは突然終了（クリーンな終了またはサイレントクラッシュ）

### 2.3 C#側ヘルスチェック調査結果 ✅

**調査期間**: [08:12:07] Stopボタン押下 ～ [08:15:56] gRPC接続失敗

**debug_app_logs.txt検索結果**:
```bash
rg "PythonServer|HealthCheck|サーバー|異常|停止|終了|crashed|exited"
```

**結論**:
- ❌ PythonServerManagerのログが一切ない
- ❌ ヘルスチェックのログが一切ない
- ❌ プロセス終了検出のログが一切ない
- ❌ サーバー異常検出のログが一切ない

### 2.4 PythonServerHealthMonitor起動状況調査 ✅

**TranslationSettings.cs確認**:
```csharp
// Line 367
public bool EnableServerAutoRestart { get; set; } = true;
```
- ✅ デフォルト値は`true`（ヘルスチェック有効）

**appsettings.json確認**:
- ❌ `EnableServerAutoRestart`の記載なし
- ✅ デフォルト値`true`が適用されるはず

**debug_app_logs.txt検索結果**:
```bash
rg "HEALTH_MONITOR|PythonServerHealthMonitor|ヘルスチェック"
```
- ❌ **PythonServerHealthMonitorのログが一切ない**

**決定的結論**:
- 🚨 **PythonServerHealthMonitorがそもそも起動していない**
- ✅ DIコンテナに登録済み（InfrastructureModule.cs Line 371-374）
- ❌ StartAsync()が呼ばれていない可能性

### 2.5 Program.cs IHostedService起動処理調査 ✅

**StartHostedServicesAsync()実装確認** (Program.cs Line 760-804):
```csharp
// Line 773-774
var hostedServices = ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
var serviceList = hostedServices.ToList();

Console.WriteLine($"🔍 検出されたIHostedService数: {serviceList.Count}");

foreach (var service in serviceList)
{
    var serviceName = service.GetType().Name;
    Console.WriteLine($"🚀 {serviceName} 起動開始...");

    try
    {
        var startTask = service.StartAsync(cancellationToken);
        startTasks.Add(startTask);
        Console.WriteLine($"✅ {serviceName} StartAsync呼び出し完了");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ {serviceName} 起動エラー: {ex.Message}");
    }
}
```

**ログ確認結果**:
- ❌ `🔍 検出されたIHostedService数` ログが**どこにも記録されていない**
- ❌ `🚀 ServerManagerHostedService 起動開始` ログなし
- ❌ `🚀 PythonServerHealthMonitor 起動開始` ログなし
- ❌ baketa_debug.log、debug_app_logs.txtのどちらにも存在しない

**原因特定**:
- ✅ StartHostedServicesAsync()は**呼び出されている**（ユーザー提供ログで確認）
- ✅ Line 688/694のBaketaLogManager.LogSystemDebug()は記録される
- ❌ Line 770-803のConsole.WriteLine()は**記録されない**
- 💡 Console.WriteLineは標準出力のみ、BaketaLogManager未使用

**重大な問題発見**:
InfrastructureModule.csでPythonServerHealthMonitorを**2回登録**:
```csharp
// Line 371: シングルトン登録
services.AddSingleton<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>();

// Line 374: HostedService登録
services.AddHostedService<PythonServerHealthMonitor>();
```

→ **2つの異なるインスタンス**が作成される可能性
→ IHostedServiceとして登録されたインスタンスのStartAsync()が呼ばれるが、ヘルスチェックが動作しない

### 2.3 クラッシュの可能性原因

| 原因 | 確率 | 根拠 |
|------|------|------|
| **メモリ不足** | 高 | NLLB-200モデル (~2.4GB) + CTranslate2変換後 (~500MB) |
| **例外未処理** | 高 | Pythonスクリプト内のハンドリングされていない例外 |
| **タイムアウト** | 中 | 長時間リクエストによるプロセスハング |
| **ポート競合** | 低 | 起動後にクラッシュしているため低確率 |
| **GC/プロセス破棄** | 低 | C#側からの明示的停止ログなし |

---

## 🧠 Phase 3: 課題2 - PID 3736誤認識の調査

### 3.1 PID 3736の正体

**確認済み事実**:
```powershell
CommandLine: python.exe "grpc_server\start_server.py" --port 5556
```

**推測**:
1. **別アプリケーションのサーバー**: 他の開発プロジェクトまたはテストサーバー
2. **手動起動**: 開発者が手動でテスト用に起動したサーバー
3. **孤立プロセス**: 過去のBaketa実行時の残留プロセス（異なるポート）

### 3.2 なぜPID 3736を確認したか

**調査プロセスの振り返り**:
1. `Get-Process python` でPythonプロセスを検索
2. PID 3736が唯一のpython.exeプロセスとして検出
3. しかし、これは**ポート5556**で動作（Baketaは50051を期待）

**誤認識の理由**:
- PID 31640のプロセスは既に終了していた
- 残っていたPID 3736を「Baketa関連かもしれない」と誤認

---

## 🧠 Phase 4: 課題3 - 画面変化検知の妥当性調査

### 4.1 2回目翻訳の詳細ログ

```
[08:11:58.780] 座標ベース翻訳処理開始 - 画像: 3864x2192, ウィンドウ: 0x230F84
[08:11:58.782] 🔍 [PHASE12.2_TRACE] TRACE-1: メソッド開始 - OCR処理前
  ↓ 【6.3秒の処理時間】
[08:12:05.054] 🔍 [PHASE12.2_TRACE] TRACE-2: OCR完了 - チャンク数: 0
```

### 4.2 画面変化検知の実行結果

**ImageChangeDetectionStageStrategy.cs ログ**:
```
2025-10-12 08:11:58.541 🔥 [STAGE1_HASH] Algo: AverageHash, Prev: FFFFFFFF, Curr: FFFFFFFF, Similarity: 1.0000, Threshold: 0.9200, HasChange: False
```

**結論**:
- **Similarity: 1.0000** = 完全に同一画像
- **HasChange: False** = 画面変化なし

### 4.3 問題の本質: 画面変化検知が機能しているのにOCRが実行された

**矛盾点**:
```
HasChange: False（画面変化なし）
→ しかし OCR処理が実行される
→ 6.3秒かけてチャンク数0という結果
```

**推測される原因**:
1. **画面変化検知のバイパス**: 特定の条件下でHasChange=Falseでも処理が続行される実装
2. **早期リターンの未実装**: ImageChangeDetectionStageStrategyで早期リターンが実装されていない
3. **設定の不整合**: EnableStaging=Trueだが、変化検知結果が無視されている

### 4.4 調査すべきコード箇所

**ImageChangeDetectionStageStrategy.cs**:
```csharp
// Line 536付近 - TryPublishTextDisappearanceEventAsync
if (previousImage != null && changeResult.HasChanged)
{
    // テキスト消失イベント発行
}

// ❓ HasChanged=False時の処理は？
// 早期リターンすべきではないか？
```

**SmartProcessingPipelineService.cs**:
```csharp
// 段階的処理の実行ロジック
// ImageChangeDetection段階でHasChange=Falseの場合、
// 後続のOCR段階をスキップすべきではないか？
```

---

## 📋 Phase 5: 調査実施計画

### 5.1 優先度P0: Pythonサーバーログ確認

**実施内容**:
1. `grpc_server/logs/*.log` の確認
2. PID 31640のstderr出力キャプチャ確認
3. Pythonスクリプトの例外ハンドリング確認

**期待される発見**:
- クラッシュ時のPython例外スタックトレース
- メモリ不足エラー (MemoryError, OutOfMemory)
- gRPCサーバー内部エラー

### 5.2 優先度P0: 画面変化検知の早期リターン実装

**問題**:
- HasChange=Falseなのに6.3秒かけてOCR実行
- 無駄な処理時間とリソース消費

**修正方針**:
```csharp
// ImageChangeDetectionStageStrategy.cs
public async Task<PipelineStageResult> ExecuteAsync(...)
{
    var changeResult = await _changeDetectionService.DetectChangeAsync(...);

    // 🔥 [PHASE5.2J] 早期リターン実装
    if (!changeResult.HasChanged)
    {
        _logger.LogDebug("画面変化なし - OCR処理をスキップ");
        return PipelineStageResult.CreateSkipped("No screen change detected");
    }

    // 以降の処理...
}
```

### 5.3 優先度P1: ヘルスチェック強化

**現状の問題**:
- PID 31640がクラッシュしたがヘルスチェックが検出していない
- 自動再起動が発動していない

**改善方針**:
- ヘルスチェック間隔の短縮（現在の設定確認必要）
- プロセス生存確認の追加
- 自動再起動の有効化

---

## ✅ Phase 6: 期待効果

| 項目 | 現状 | 改善後 |
|------|------|--------|
| **サーバークラッシュ検出** | 未検出 | ヘルスチェックで即座検出 |
| **自動復旧** | なし | 自動再起動実装 |
| **無駄なOCR処理** | 6.3秒 | **0秒（スキップ）** |
| **ユーザー体験** | 翻訳失敗 | **透過的な復旧** |

---

## 🎯 Phase 7: 次のアクション

### Step 1: Pythonサーバーログ確認
- [ ] `grpc_server/logs/` ディレクトリ確認
- [ ] PythonServerInstance stderr出力確認
- [ ] クラッシュ原因の特定

### Step 2: 画面変化検知早期リターン実装
- [ ] ImageChangeDetectionStageStrategy.cs修正
- [ ] HasChanged=False時の早期リターン追加
- [ ] ビルド&動作検証

### Step 3: ヘルスチェック強化
- [ ] PythonServerHealthMonitor.cs設定確認
- [ ] ヘルスチェック間隔の最適化
- [ ] 自動再起動の有効化

---

## 📊 Phase 8: リスク評価

| リスク | 発生確率 | 影響度 | 対策 |
|--------|----------|--------|------|
| Pythonログが存在しない | 中 | 低 | stderrキャプチャ強化 |
| 早期リターンで翻訳が実行されない | 低 | 中 | 段階的テスト実施 |
| ヘルスチェック過負荷 | 低 | 低 | 間隔調整 |

---

**次のステップ**: Phase 5.1実施 - Pythonサーバーログ確認
