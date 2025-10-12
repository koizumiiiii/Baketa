# Phase 2完了報告 & Phase 3テストガイド

**作成日**: 2025-10-10
**ステータス**: Phase 1～2完了、Phase 3準備完了

---

## ✅ Phase 2完了サマリー

### 📊 実装内容

**実装日**: 2025-10-10
**実装ファイル**: `grpc_server/stress_test.py`（243行）

### 🎯 Phase 2の成果物

**24時間ストレステストスクリプト**:
- gRPC経由の連続翻訳リクエスト
- カスタマイズ可能な実行時間・間隔
- 100リクエストごとの統計ログ
- タイムアウト検出（10秒）
- 成功率・処理速度の自動計算

**主要機能**:
```python
class StressTestRunner:
    - run_stress_test(): メインループ
    - _log_statistics(): 100リクエストごとの統計
    - _log_final_statistics(): 最終結果サマリー
```

**テストテキスト**:
- 8種類の日本語テキスト
- ランダム選択でバリエーション確保

---

## 📋 Geminiコードレビュー結果

**総合評価**: ⭐⭐⭐⭐ (4/5) - 優秀な実装、軽微な改善提案あり

### ✅ 高評価項目

| 評価項目 | スコア | コメント |
|---------|--------|----------|
| アーキテクチャ設計 | ⭐⭐⭐⭐⭐ | 関心の分離が適切、asyncio統合が正確 |
| メモリ管理 | ⭐⭐⭐⭐ | 多層防御戦略、動的GC調整の提案あり |
| エラーハンドリング | ⭐⭐⭐⭐⭐ | 多層防御が完璧 |
| パフォーマンス | ⭐⭐⭐⭐ | 影響最小限 |
| ベストプラクティス | ⭐⭐⭐⭐⭐ | 完全準拠 |

### 🚨 P0修正完了

**修正内容**: ResourceMonitor.stop_monitoring()デッドロック対策強化

```python
async def stop_monitoring(self):
    """監視停止 - デッドロック防止のため即座にキャンセル"""
    if self.monitoring_task and not self.monitoring_task.done():
        self.monitoring_task.cancel()
        try:
            await self.monitoring_task
        except asyncio.CancelledError:
            logger.info("[RESOURCE_MONITOR] Monitoring task cancelled gracefully")
```

**効果**: 最大5分の待機時間を即座キャンセルに短縮

---

## 🚀 Phase 3: ストレステスト実行ガイド

### 📝 事前準備

#### 1. 依存パッケージ確認

```powershell
cd E:\dev\Baketa\grpc_server
py -m pip list | Select-String -Pattern "pynvml|psutil|grpcio|ctranslate2"
```

**期待される出力**:
```
ctranslate2             4.6.0
grpcio                  1.75.1
grpcio-tools            1.75.1
psutil                  7.0.0
pynvml                  13.0.1
```

#### 2. 既存プロセスの停止

```powershell
# 既存のPythonサーバーを停止
Get-Process | Where-Object {$_.ProcessName -eq "python"} | Stop-Process -Force
```

#### 3. 重要: Baketaアプリは起動不要

**テスト構成**:
```
ウィンドウ1: Python翻訳サーバー (start_server.py)
              ↑
              │ gRPC通信
              │
ウィンドウ2: ストレステスト (stress_test.py)

Baketa.UIアプリ: 起動不要（テストには関与しない）
```

---

### 🚀 Phase 3-A: 1時間テスト（動作確認）

#### Step 1: Python翻訳サーバー起動

```powershell
# 新しいPowerShellウィンドウを開く（ウィンドウ1）
cd E:\dev\Baketa\grpc_server
py start_server.py --port 50051 --use-ctranslate2
```

**期待されるログ出力**:
```
[PHASE1.3] faulthandler enabled - OS-level crash detection active
[PHASE1.3] Global exception handler installed
Server configuration:
  Host: 0.0.0.0
  Port: 50051
  Heavy model: False
  Use CTranslate2: True
  Debug mode: False
Initializing CTranslate2 translation engine...
Loading NLLB model (this may take a few minutes)...
NLLB model loaded successfully
Creating gRPC server...
Starting gRPC server on 0.0.0.0:50051...
================================================================================
gRPC Translation Server is running on 0.0.0.0:50051
   Engine: CTranslate2Engine
   Model: CTranslate2 (int8)
   Device: cuda
[SERVER_START]
[PHASE1.1] Resource monitoring started (CPU RAM + GPU VRAM + Handles)
================================================================================
Press Ctrl+C to stop the server
```

**✅ 確認ポイント**:
- [ ] `[PHASE1.1] Resource monitoring started` が表示される
- [ ] `[PHASE1.3] faulthandler enabled` が表示される
- [ ] `[SERVER_START]` が表示される
- [ ] エラーが出ていない

---

#### Step 2: ストレステスト起動（1時間）

```powershell
# 別の新しいPowerShellウィンドウを開く（ウィンドウ2）
cd E:\dev\Baketa\grpc_server
py stress_test.py --duration 1 --interval 0.5 --server-address localhost:50051
```

**期待されるログ出力**:
```
================================================================================
24時間ストレステスト開始
================================================================================
サーバーアドレス: localhost:50051
テスト時間: 1 時間
リクエスト間隔: 0.5 秒
================================================================================
開始時刻: 2025-10-10 23:30:00.123456
終了予定時刻: 2025-10-11 00:30:00.123456

... (しばらく待つ)

================================================================================
[STATISTICS]
  成功翻訳数: 100
  エラー数: 0
  成功率: 100.00%
  経過時間: 0.08 時間
  残り時間: 0.92 時間
  処理速度: 120.00 req/min
  現在時刻: 2025-10-10 23:35:00.123456
================================================================================
```

**✅ 確認ポイント**:
- [ ] 100リクエストごとに統計が出力される
- [ ] 成功率が99%以上
- [ ] エラー数が少ない（<10）
- [ ] 処理速度が60 req/min以上

---

#### Step 3: リソース監視ログの確認

```powershell
# 別のPowerShellウィンドウを開く（ウィンドウ3）- オプション
cd E:\dev\Baketa\grpc_server
Get-Content translation_server.log -Tail 50 -Wait | Select-String -Pattern "RESOURCE_MONITOR|VRAM_ALERT|HANDLE_LEAK"
```

**期待されるログ出力（5分ごと）**:
```
2025-10-10 23:35:00,123 - [RESOURCE_MONITOR] CPU_RAM: 512.34 MB (VMS: 1024.56 MB), VRAM: 523.12/8192.00 MB (6.4%), Handles: 234, Threads: 12
```

**🚨 アラート発生時（即座にテスト中止）**:
```
[VRAM_ALERT] VRAM usage exceeds 90%: 7372.80 MB / 8192.00 MB (90.1%) - Potential memory leak!
[HANDLE_LEAK_ALERT] Handle count exceeds 10k: 10234 - Potential handle leak!
```

---

#### Step 4: 1時間テスト完了後の評価

**ウィンドウ2（stress_test.py）の最終ログ**:
```
================================================================================
ストレステスト終了
================================================================================
開始時刻: 2025-10-10 23:30:00.123456
終了時刻: 2025-10-11 00:30:00.123456
総実行時間: 1.00 時間
総翻訳数: 7200
総エラー数: 0
成功率: 100.00%
平均処理速度: 120.00 req/min
================================================================================
```

**✅ 合格基準**:
- [ ] 成功率 > 99.5%（推奨: 100%）
- [ ] 総エラー数 < 36（0.5%未満）
- [ ] 平均処理速度 > 60 req/min
- [ ] VRAM使用率 < 90%
- [ ] Windowsハンドル < 1000

**1時間テストがすべて合格 → 24時間テストに進む**

---

### 🚀 Phase 3-B: 24時間テスト（本番）

#### Step 1: サーバー起動（ログファイル出力）

```powershell
# ウィンドウ1
cd E:\dev\Baketa\grpc_server
py start_server.py --port 50051 --use-ctranslate2 > server_output.log 2>&1
```

**注意**:
- ログがファイルに保存されるため、画面には何も表示されない
- サーバーが起動しているかは `server_output.log` で確認

```powershell
# 起動確認
Get-Content server_output.log -Tail 20
# [SERVER_START] が表示されればOK
```

---

#### Step 2: ストレステスト起動（24時間）

```powershell
# ウィンドウ2
cd E:\dev\Baketa\grpc_server
py stress_test.py --duration 24 --interval 0.5 --server-address localhost:50051
```

**実行後の注意事項**:
- ⚠️ **PCをスリープさせない**
- ⚠️ **Pythonウィンドウを閉じない**
- ⚠️ **ネットワーク接続を維持**
- ✅ リモートデスクトップで接続している場合は切断してもOK

**スリープ防止設定**:
```powershell
# Windows設定で電源プランを「高パフォーマンス」に変更
# または PowerShellで実行:
powercfg /change standby-timeout-ac 0
powercfg /change monitor-timeout-ac 30
```

---

#### Step 3: リアルタイム監視スクリプト（オプション）

**監視スクリプト作成**:

```powershell
# E:\dev\Baketa\grpc_server\monitor_test.ps1
while ($true) {
    Clear-Host
    Write-Host "=== Phase 3 Stress Test Monitor ===" -ForegroundColor Cyan
    Write-Host "Last Updated: $(Get-Date)" -ForegroundColor Gray
    Write-Host ""

    # 最新の統計ログを表示
    Write-Host "--- Stress Test Statistics (Last 15 lines) ---" -ForegroundColor Yellow
    Get-Content stress_test.log -Tail 15 -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "--- Resource Monitor (Last 3 entries) ---" -ForegroundColor Yellow
    Get-Content translation_server.log -ErrorAction SilentlyContinue |
        Select-String "RESOURCE_MONITOR" |
        Select-Object -Last 3

    Write-Host ""
    Write-Host "--- Alerts (if any) ---" -ForegroundColor Red
    $alerts = Get-Content translation_server.log -ErrorAction SilentlyContinue |
        Select-String "ALERT|CRITICAL" |
        Select-Object -Last 5
    if ($alerts) {
        $alerts
    } else {
        Write-Host "No alerts detected" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Press Ctrl+C to stop monitoring" -ForegroundColor Gray
    Start-Sleep -Seconds 60  # 1分ごとに更新
}
```

**実行**:
```powershell
# ウィンドウ3
cd E:\dev\Baketa\grpc_server
.\monitor_test.ps1
```

---

### 📊 24時間後の評価

#### Step 1: ストレステスト結果の確認

```powershell
# stress_test.logの最終結果を確認
Get-Content E:\dev\Baketa\grpc_server\stress_test.log -Tail 50
```

**期待される最終ログ**:
```
================================================================================
ストレステスト終了
================================================================================
開始時刻: 2025-10-10 23:00:00.000000
終了時刻: 2025-10-11 23:00:00.000000
総実行時間: 24.00 時間
総翻訳数: 172,800
総エラー数: 0
成功率: 100.00%
平均処理速度: 120.00 req/min
================================================================================
```

**✅ 評価基準**:
- [ ] 総翻訳数 > 172,000（24時間 × 120 req/min × 60分）
- [ ] 総エラー数 < 864（0.5%未満）
- [ ] 成功率 > 99.5%
- [ ] 平均処理速度 > 100 req/min

---

#### Step 2: リソース使用量の推移分析

```powershell
# VRAM使用量の推移を抽出
Get-Content translation_server.log |
    Select-String "RESOURCE_MONITOR" |
    Select-String "VRAM" |
    Out-File vram_log.txt

# VRAMログを時系列で確認
notepad vram_log.txt
```

**期待される傾向**:
```
[00:00] VRAM: 523.12/8192.00 MB (6.4%)
[00:05] VRAM: 534.56/8192.00 MB (6.5%)
[00:10] VRAM: 541.23/8192.00 MB (6.6%)
...
[23:55] VRAM: 612.34/8192.00 MB (7.5%)  # +20%以内
```

**🚨 異常パターン**:
```
[00:00] VRAM: 523.12 MB (6.4%)
[06:00] VRAM: 2048.00 MB (25.0%)  # 急激な増加
[12:00] VRAM: 4096.00 MB (50.0%)  # 線形増加（リーク疑い）
[18:00] VRAM: 6144.00 MB (75.0%)
```
→ メモリリークの可能性が高い

**分析コマンド**:
```powershell
# VRAM使用量の最小・最大・平均を計算
$vramLogs = Get-Content translation_server.log |
    Select-String "VRAM: (\d+\.\d+)/(\d+\.\d+) MB" |
    ForEach-Object {
        if ($_ -match "VRAM: (\d+\.\d+)/(\d+\.\d+) MB") {
            [PSCustomObject]@{
                Used = [double]$matches[1]
                Total = [double]$matches[2]
                Percent = ([double]$matches[1] / [double]$matches[2]) * 100
            }
        }
    }

$vramLogs | Measure-Object -Property Used -Average -Minimum -Maximum | Format-List

# 期待結果:
# Average: 600 MB程度
# Minimum: 500 MB程度
# Maximum: 700 MB程度（初期値+20%以内）
```

---

#### Step 3: クラッシュ・例外の確認

```powershell
# 未処理例外の検出
Get-Content translation_server.log | Select-String "UNCAUGHT EXCEPTION|CRITICAL ERROR"

# faulthandlerのクラッシュ検出
Get-Content server_output.log | Select-String "Fatal Python error|Segmentation fault|SIGSEGV"
```

**期待結果**: 何も表示されない（クラッシュなし）

**🚨 クラッシュ検出時**:
```
Fatal Python error: Segmentation fault
Current thread 0x00001234 (most recent call first):
  File "ctranslate2_engine.py", line 123 in translate
  ...
```
→ クラッシュが再現された場合、詳細ログを保存してGeminiに相談

---

### 🎯 最終合格基準

| 項目 | 目標値 | 実測値 | 評価 |
|------|--------|--------|------|
| **成功率** | > 99.5% | _____ % | Pass/Fail |
| **総エラー数** | < 864 | _____ | Pass/Fail |
| **VRAM増加率** | < 20% | _____ % | Pass/Fail |
| **VRAM最大値** | < 初期値×1.2 | _____ MB | Pass/Fail |
| **Windowsハンドル** | < 初期値+100 | _____ | Pass/Fail |
| **CPU RAM増加率** | < 40% | _____ % | Pass/Fail |
| **クラッシュ** | 0回 | _____ 回 | Pass/Fail |
| **未処理例外** | 0回 | _____ 回 | Pass/Fail |

**すべてPass**: ✅ Phase 1～2実装が成功 🎉

**1つでもFail**: ⚠️ Gemini P1推奨事項の実装を検討

---

### 📝 テスト中断が必要な場合

```powershell
# ストレステスト停止
# ウィンドウ2でCtrl+Cを押す

# サーバー停止
# ウィンドウ1でCtrl+Cを押す

# または強制終了
Get-Process python | Where-Object {$_.CommandLine -like "*stress_test*"} | Stop-Process
Get-Process python | Where-Object {$_.CommandLine -like "*start_server*"} | Stop-Process
```

---

## 📋 トラブルシューティング

### 問題1: サーバーが起動しない

**症状**:
```
ModuleNotFoundError: No module named 'ctranslate2'
```

**解決策**:
```powershell
cd E:\dev\Baketa\grpc_server
py -m pip install -r requirements.txt
```

---

### 問題2: gRPC接続エラー

**症状**:
```
[gRPC_ERROR] Status: StatusCode.UNAVAILABLE, Details: failed to connect to all addresses
```

**解決策**:
1. サーバーが起動しているか確認
   ```powershell
   Get-Process python | Where-Object {$_.CommandLine -like "*start_server*"}
   ```
2. ポート50051が使用中か確認
   ```powershell
   netstat -ano | Select-String ":50051"
   ```
3. サーバーログを確認
   ```powershell
   Get-Content translation_server.log -Tail 20
   ```

---

### 問題3: VRAM不足

**症状**:
```
RuntimeError: CUDA out of memory
```

**解決策**:
1. 他のGPU使用アプリケーションを終了
2. CTranslate2の代わりにCPU版を使用
   ```powershell
   py start_server.py --port 50051  # --use-ctranslate2を外す
   ```

---

## 🎯 次のステップ

**Phase 3-A（1時間テスト）成功後**:
- [ ] Phase 3-B（24時間テスト）実行
- [ ] 結果をドキュメントに記録
- [ ] CLAUDE.local.mdに評価結果を追加

**Phase 3-B（24時間テスト）成功後**:
- [ ] Phase 1～2実装完了を宣言
- [ ] Gemini P1推奨事項の実装を検討（オプション）
- [ ] Baketaアプリでの実運用テスト

**Phase 3失敗時**:
- [ ] 失敗ログをすべて保存
- [ ] Geminiに詳細レビュー依頼
- [ ] P1推奨事項の実装を優先

---

## 📄 関連ドキュメント

- `PYTHON_SERVER_CRASH_ANALYSIS.md`: 初期分析レポート
- `PHASE1_IMPLEMENTATION_COMPLETE.md`: Phase 1詳細実装
- `../grpc_server/stress_test.py`: ストレステストスクリプト
- `../grpc_server/resource_monitor.py`: GPU/VRAM監視実装
- `../grpc_server/start_server.py`: サーバー起動スクリプト
