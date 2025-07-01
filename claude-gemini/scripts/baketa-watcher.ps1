# baketa-watcher.ps1 - Baketa自動起動システム (FileSystemWatcher版)

param(
    [string]$WatchMode = "file"
)

# 動的パス設定
$ScriptRoot = $PSScriptRoot
$ProjectRoot = (Resolve-Path "$ScriptRoot\..\..").Path
$CloudeGeminiRoot = "$ProjectRoot\claude-gemini"
$TriggerDir = "$CloudeGeminiRoot\triggers"
$BridgeDir = "$CloudeGeminiRoot\bridge"
$RequestsQueueDir = "$BridgeDir\requests"
$ProcessedDir = "$BridgeDir\processed"

# ディレクトリ作成
@($TriggerDir, $BridgeDir, $RequestsQueueDir, $ProcessedDir) | ForEach-Object {
    if (!(Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
}

function Watch-DevelopmentRequestsWithFileSystemWatcher {
    Write-Host "🔍 Baketa開発要求監視開始 (FileSystemWatcher使用)..." -ForegroundColor Green
    Write-Host "   監視ディレクトリ: $RequestsQueueDir" -ForegroundColor Cyan
    Write-Host "   Ctrl+C で終了" -ForegroundColor Yellow

    # プロセスID記録
    $env:PID | Out-File -FilePath "$BridgeDir\watcher.pid" -Encoding UTF8

    # FileSystemWatcher設定
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = $RequestsQueueDir
    $watcher.Filter = "*.json"
    $watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName
    $watcher.EnableRaisingEvents = $true

    # イベントハンドラー定義
    $action = {
        $path = $Event.SourceEventArgs.FullPath
        $fileName = $Event.SourceEventArgs.Name
        $changeType = $Event.SourceEventArgs.ChangeType

        if ($changeType -eq "Created") {
            Write-Host "📝 新しい開発要求検出: $fileName" -ForegroundColor Yellow

            # ファイルの書き込み完了を待つ（ファイルロック回避）
            do {
                Start-Sleep -Milliseconds 100
                try {
                    $file = [System.IO.File]::Open($path, 'Open', 'Read', 'None')
                    $file.Close()
                    $fileReady = $true
                }
                catch {
                    $fileReady = $false
                }
            } while (!$fileReady)

            try {
                # 要求ファイル処理
                Process-RequestFile -FilePath $path

                # 処理済みディレクトリに移動
                $processedPath = Join-Path $using:ProcessedDir $fileName
                Move-Item -Path $path -Destination $processedPath -Force
                Write-Host "✅ 要求処理完了: $fileName" -ForegroundColor Green
            }
            catch {
                Write-Host "❌ 要求処理エラー: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }

    # イベント登録
    Register-ObjectEvent -InputObject $watcher -EventName "Created" -Action $action

    try {
        # 監視開始
        Write-Host "✅ ファイル監視開始。新しい要求ファイルの作成を待機中..." -ForegroundColor Green

        # 既存のファイルがあれば処理
        $existingFiles = Get-ChildItem -Path $RequestsQueueDir -Filter "*.json"
        foreach ($file in $existingFiles) {
            Write-Host "📝 既存の要求ファイル検出: $($file.Name)" -ForegroundColor Yellow
            Process-RequestFile -FilePath $file.FullPath
            Move-Item -Path $file.FullPath -Destination (Join-Path $ProcessedDir $file.Name) -Force
        }

        # 無限ループ（Ctrl+Cで終了）
        while ($true) {
            Start-Sleep -Seconds 1
        }
    }
    finally {
        # クリーンアップ
        $watcher.EnableRaisingEvents = $false
        $watcher.Dispose()
        Get-EventSubscriber | Unregister-Event
        if (Test-Path "$BridgeDir\watcher.pid") {
            Remove-Item "$BridgeDir\watcher.pid" -Force
        }
        Write-Host "🛑 監視を停止しました" -ForegroundColor Red
    }
}

function Process-RequestFile {
    param([string]$FilePath)

    try {
        $request = Get-Content -Path $FilePath -Raw | ConvertFrom-Json

        if ($request.status -eq "pending") {
            Write-Host "🚀 開発要求処理開始: $($request.featureName)" -ForegroundColor Cyan

            # 自動開発実行
            & "$CloudeGeminiRoot\scripts\baketa-dev.ps1" -Action auto-develop `
              -FeatureName $request.featureName `
              -Description $request.description

            # ステータス更新
            $request.status = "completed"
            $request.completedAt = Get-Date

            # 結果をファイルに保存
            $request | ConvertTo-Json -Depth 3 | Out-File -FilePath $FilePath -Encoding UTF8
        }
    }
    catch {
        Write-Host "❌ 要求ファイル処理エラー: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Start-InteractiveMode {
    Write-Host @"
🤖 Baketa対話開発モード

使用方法:
  1. 機能名と説明を入力
  2. 自動開発が開始されます
  3. 'exit' で終了

例: OCR最適化: OpenCVフィルタによるテキスト検出精度向上
"@ -ForegroundColor Green

    while ($true) {
        $input = Read-Host "`n💡 開発要求を入力してください (機能名: 説明)"

        if ($input -eq "exit") {
            Write-Host "👋 対話モード終了" -ForegroundColor Yellow
            break
        }

        if ($input -match "^([^:]+):\s*(.+)") {
            $featureName = $matches[1].Trim()
            $description = $matches[2].Trim()

            Write-Host "🚀 自動開発開始: $featureName" -ForegroundColor Cyan

            & "$CloudeGeminiRoot\scripts\baketa-dev.ps1" -Action auto-develop `
              -FeatureName $featureName `
              -Description $description
        } else {
            Write-Host "❌ 形式が正しくありません。'機能名: 説明' の形式で入力してください。" -ForegroundColor Red
        }
    }
}

switch ($WatchMode.ToLower()) {
    "file" {
        Watch-DevelopmentRequestsWithFileSystemWatcher
    }
    "interactive" {
        Start-InteractiveMode
    }
    default {
        Write-Host @"
Baketa自動起動システム

使用法:
  .\baketa-watcher.ps1 -WatchMode file        # ファイル監視モード (FileSystemWatcher)
  .\baketa-watcher.ps1 -WatchMode interactive # 対話モード

例:
  .\baketa-watcher.ps1 -WatchMode file
  .\baketa-watcher.ps1 -WatchMode interactive
"@ -ForegroundColor Green
    }
}