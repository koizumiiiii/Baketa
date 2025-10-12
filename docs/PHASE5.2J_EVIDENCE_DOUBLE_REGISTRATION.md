# Phase 5.2J 証拠ドキュメント - PythonServerHealthMonitor二重登録問題

## 🎯 調査目的

PythonServerHealthMonitorが起動していない根本原因を特定し、確実な証拠を基に修正方針を策定する。

---

## 📊 証拠1: DIコンテナ二重登録の確認

### ファイル: `Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs`

**Line 371-374**:
```csharp
// シングルトンとしても登録（直接取得のため）
services.AddSingleton<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>();

// HostedServiceとしても登録
services.AddHostedService<PythonServerHealthMonitor>();
```

**問題点**:
- 同一クラス`PythonServerHealthMonitor`を2つの異なる方法で登録
- コメントに「直接取得のため」とあるが、実際に直接取得している箇所は不明

---

## 📊 証拠2: .NET DIコンテナの二重登録動作

### Microsoft.Extensions.DependencyInjection 仕様

**AddSingleton<T>()の動作**:
- `IServiceCollection`に`ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Singleton)`を追加
- DIコンテナが`T`型のシングルトンインスタンスを1つ作成
- `services.GetService<T>()`で取得可能

**AddHostedService<T>()の動作**:
- 内部的に`AddSingleton<IHostedService, T>()`を実行
- `IHostedService`インターフェース経由でアクセス可能な別のインスタンスを作成
- `services.GetServices<IHostedService>()`で取得可能

**二重登録の結果**:
```
ServiceProvider
├── PythonServerHealthMonitor (Singleton) ← AddSingleton登録
└── IHostedService (Singleton)
    └── PythonServerHealthMonitor (別インスタンス) ← AddHostedService登録
```

→ **2つの異なるPythonServerHealthMonitorインスタンスが存在**

---

## 📊 証拠3: 実行時の動作ログ

### Program.cs StartHostedServicesAsync()

**実装** (Line 773-790):
```csharp
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

**ユーザー提供ログ**:
```
[08:10:07.083][T08] 🚀 [HOSTED_SERVICE] IHostedService手動起動開始（非同期）
[08:10:08.546][T06] ✅ [HOSTED_SERVICE] IHostedService手動起動完了
```

**問題**:
- ✅ StartHostedServicesAsync()は**実行されている**
- ❌ Line 776の`🔍 検出されたIHostedService数`ログが**出力されていない**
- ❌ Line 784の`🚀 {serviceName} 起動開始`ログが**出力されていない**

**理由**:
- Console.WriteLine()は標準出力に出力される
- BaketaLogManager.LogSystemDebug()を使用していないため、`debug_app_logs.txt`には記録されない
- 標準出力は表示されているが、ログファイルには残っていない

---

## 📊 証拠4: PythonServerHealthMonitor起動ログの不在

### ファイル: `Baketa.Infrastructure/Translation/Services/PythonServerHealthMonitor.cs`

**コンストラクタ** (Line 55-65):
```csharp
public PythonServerHealthMonitor(...)
{
    Console.WriteLine("🔍 [HEALTH_MONITOR] コンストラクタ開始");
    // ...
    Console.WriteLine("✅ [HEALTH_MONITOR] コンストラクタ完了 - 設定は StartAsync で取得");
}
```

**StartAsync()** (Line 68-101):
```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("✅ PythonServerHealthMonitor開始");
    // ...
    if (settings.EnableServerAutoRestart)
    {
        // ヘルスチェックタイマーを開始
        _healthCheckTimer = new System.Threading.Timer(...);
        _logger.LogInformation("🔍 ヘルスチェック開始 - 間隔: {IntervalMs}ms", ...);
        Console.WriteLine("✅ [HEALTH_MONITOR] ヘルスチェック有効 - 自動監視開始");
    }
    else
    {
        _logger.LogWarning("⚠️ サーバー自動再起動は無効化されています");
        Console.WriteLine("⚠️ [HEALTH_MONITOR] サーバー自動再起動は無効化されています");
    }
}
```

**ログ確認結果**:
```bash
rg "HEALTH_MONITOR|PythonServerHealthMonitor|ヘルスチェック" debug_app_logs.txt
rg "HEALTH_MONITOR|PythonServerHealthMonitor|ヘルスチェック" baketa_debug.log
```
→ **両ファイルで0件**

**結論**:
- コンストラクタが呼ばれていない（Console.WriteLineログなし）
- StartAsync()が呼ばれていない（ILoggerログなし）
- **PythonServerHealthMonitorが全く起動していない**

---

## 📊 証拠5: ServerManagerHostedServiceとの比較 ✅

### ServerManagerHostedService起動ログ

**ファイル**: `Baketa.Infrastructure/Translation/Services/ServerManagerHostedService.cs`

**StartAsync()実装** (Line 32-73):
```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("🚀 [HOSTED_SERVICE] Python翻訳サーバーをバックグラウンドで起動します");

    _ = Task.Run(async () =>
    {
        try
        {
            _logger.LogInformation("🔄 [HOSTED_SERVICE] Python翻訳サーバー起動開始");

            const string defaultLanguagePair = "grpc-all";
            var serverInfo = await _serverManager.StartServerAsync(defaultLanguagePair).ConfigureAwait(false);

            _logger.LogInformation("✅ [HOSTED_SERVICE] Python翻訳サーバー起動完了: Port {Port}", serverInfo.Port);

            // GrpcPortProviderにポート番号を設定
            _portProvider.SetPort(serverInfo.Port);
            _logger.LogInformation("🎯 [HOSTED_SERVICE] GrpcPortProvider設定完了: Port {Port}", serverInfo.Port);

            // ヘルスチェックタイマー初期化
            _serverManager.InitializeHealthCheckTimer();
            _logger.LogInformation("🩺 [HOSTED_SERVICE] ヘルスチェックタイマー初期化完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [HOSTED_SERVICE] Python翻訳サーバー起動失敗");
            _portProvider.SetException(ex);
        }
    }, cancellationToken);

    _logger.LogInformation("✅ [HOSTED_SERVICE] StartAsync完了 - バックグラウンド起動中");
    return Task.CompletedTask;
}
```

**debug_app_logs.txtログ確認結果**:
```bash
rg "HOSTED_SERVICE" debug_app_logs.txt
```

**結果**:
```
[08:10:07.083][T08] 🚀 [HOSTED_SERVICE] IHostedService手動起動開始（非同期）
[08:10:08.546][T06] ✅ [HOSTED_SERVICE] IHostedService手動起動完了
```

**期待されるServerManagerHostedServiceのログ**:
- `🚀 [HOSTED_SERVICE] Python翻訳サーバーをバックグラウンドで起動します`
- `🔄 [HOSTED_SERVICE] Python翻訳サーバー起動開始`
- `✅ [HOSTED_SERVICE] Python翻訳サーバー起動完了: Port 50051`
- `🎯 [HOSTED_SERVICE] GrpcPortProvider設定完了: Port 50051`
- `🩺 [HOSTED_SERVICE] ヘルスチェックタイマー初期化完了`
- `✅ [HOSTED_SERVICE] StartAsync完了 - バックグラウンド起動中`

**実際のログ**: ❌ **ServerManagerHostedServiceのログも一切出ていない**

**重大発見**:
- Program.cs Line 680-735の`StartHostedServicesAsync()`は**実行されている**（ユーザー提供ログで確認済み）
- しかし、Line 776の`Console.WriteLine($"🔍 検出されたIHostedService数: {serviceList.Count}")`ログが**出力されていない**
- **個別のIHostedServiceのStartAsync()が一切呼ばれていない**

**結論**:
- PythonServerHealthMonitorのみならず、**ServerManagerHostedServiceも起動していない**
- **IHostedService起動メカニズム全体が機能していない可能性**

---

## 🧠 仮説の再検討: IHostedService起動メカニズム全体の失敗

### 新たな発見

**証拠5の調査結果**:
- ✅ `StartHostedServicesAsync()`は実行されている（ユーザー提供ログで確認）
- ❌ `Console.WriteLine($"🔍 検出されたIHostedService数: {serviceList.Count}")`が出力されていない
- ❌ ServerManagerHostedService.StartAsync()も一切呼ばれていない
- ❌ PythonServerHealthMonitor.StartAsync()も一切呼ばれていない

### 問題の連鎖（修正版）

1. **Program.cs ConfigureServices()内でStartHostedServicesAsync()呼び出し**:
   ```csharp
   // Line 647-659
   Task.Run(async () =>
   {
       await StartHostedServicesAsync().ConfigureAwait(false);
       Console.WriteLine("🚀🚀🚀 [CRITICAL] IHostedService手動起動完了！");
   });
   ```
   → ✅ このTask.Run()は実行される（ログで確認済み）

2. **StartHostedServicesAsync()が実行されるがGetServices<IHostedService>()で空リストを取得**:
   ```csharp
   // Line 773-774
   var hostedServices = ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
   var serviceList = hostedServices.ToList();
   Console.WriteLine($"🔍 検出されたIHostedService数: {serviceList.Count}"); // ← 出力されない
   ```
   → ❌ `serviceList.Count`ログが出ない = このコードが実行されていない、または例外で中断

3. **可能性1: ServiceProviderがnullまたは未初期化**:
   - Line 770で早期リターンされている可能性
   - しかし、[08:10:08.546]の「IHostedService手動起動完了」ログは出ている

4. **可能性2: GetServices<IHostedService>()で例外発生**:
   - DIコンテナ解決時の例外
   - 二重登録による競合
   - ただし、try-catchで捕捉されるはず

5. **結果: 全IHostedServiceが起動しない**:
   - ServerManagerHostedServiceも起動せず → Pythonサーバー起動失敗
   - PythonServerHealthMonitorも起動せず → ヘルスチェック不能
   - 3分33秒間、サーバークラッシュを検出できない

---

## 🎯 次のステップ

### ✅ 証拠収集完了

1. ✅ **ServerManagerHostedServiceログ確認完了**:
   - ServerManagerHostedService.StartAsync()も一切呼ばれていないことを確認
   - 問題はPythonServerHealthMonitor単体ではなく、IHostedService起動メカニズム全体

2. 🔄 **追加証拠収集が必要**:
   - Program.cs StartHostedServicesAsync()にBaketaLogManagerログ追加
   - `GetServices<IHostedService>()`の実行状況を可視化
   - 例外発生有無を確認

3. ✅ **Geminiレビュー準備完了**:
   - 収集した証拠を基にGeminiにレビュー依頼
   - 修正方針の妥当性を確認

---

## 📝 質問事項（Gemini向け）

### **重大発見: IHostedService起動メカニズム全体の失敗**

**調査結果**:
- ✅ Program.cs ConfigureServices()内のTask.Run()は実行される（ログ確認済み）
- ❌ StartHostedServicesAsync() Line 776の`Console.WriteLine($"🔍 検出されたIHostedService数: {serviceList.Count}")`が**出力されない**
- ❌ ServerManagerHostedService.StartAsync()も**一切呼ばれていない**
- ❌ PythonServerHealthMonitor.StartAsync()も**一切呼ばれていない**

**タイムラインログ**:
```
[08:10:07.083][T08] 🚀 [HOSTED_SERVICE] IHostedService手動起動開始（非同期）
[08:10:08.546][T06] ✅ [HOSTED_SERVICE] IHostedService手動起動完了
```
→ StartHostedServicesAsync()の内部ログ（Line 776以降）が**一切出力されていない**

### **質問**

1. **Program.cs Line 647-659のTask.Run()パターンは正しいか？**
   ```csharp
   Task.Run(async () =>
   {
       try
       {
           await StartHostedServicesAsync().ConfigureAwait(false);
           Console.WriteLine("🚀🚀🚀 [CRITICAL] IHostedService手動起動完了！");
       }
       catch (Exception ex)
       {
           Console.WriteLine($"💥 [CRITICAL] IHostedService手動起動エラー: {ex.Message}");
       }
   });
   ```
   - Task.Run()が実行されているのに、StartHostedServicesAsync()内部ログが出ないのは何故か？
   - fire-and-forget実行で例外が隠蔽されている可能性は？

2. **GetServices<IHostedService>()呼び出しでDIコンテナ例外発生の可能性は？**
   - PythonServerHealthMonitorの二重登録（AddSingleton + AddHostedService）が原因で、DIコンテナが例外をスローしているか？
   - 例外がtry-catchで捕捉されずに静かに失敗しているか？

3. **推奨される修正方針は？**
   - Option A: PythonServerHealthMonitorの二重登録削除（AddSingletonを削除、AddHostedServiceのみに統一）
   - Option B: Task.Run()をawaitして例外を明示的に捕捉
   - Option C: StartHostedServicesAsync()に詳細ログを追加してGetServices<IHostedService>()実行状況を可視化
   - Option D: その他の方法

---

## ✅ 確実な証拠（既に収集済み）

1. ✅ InfrastructureModule.cs Line 371-374で二重登録確認
2. ✅ debug_app_logs.txtにPythonServerHealthMonitorログが0件
3. ✅ PythonサーバーはLine 52.996に`[SERVER_START]`後、サイレントクラッシュ
4. ✅ 3分33秒間、C#側にサーバー異常検出ログなし

## 🔄 追加証拠収集が必要な項目

1. 🔄 ServerManagerHostedServiceのログ確認
2. 🔄 DIコンテナのインスタンス生成ログ追加
3. 🔄 StartHostedServicesAsync()の詳細ログ追加（BaketaLogManager使用）
