# Baketa プロジェクト警告修正スクリプト

# ファイルパスとクラス名のマッピング
$sealableClasses = @(
    # ViewModels
    "E:\dev\Baketa\Baketa.UI\ViewModels\CaptureViewModel.cs|CaptureViewModel",
    "E:\dev\Baketa\Baketa.UI\ViewModels\HistoryViewModel.cs|HistoryViewModel",
    "E:\dev\Baketa\Baketa.UI\ViewModels\HomeViewModel.cs|HomeViewModel",
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainViewModel.cs|MainViewModel",
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|MainWindowViewModel",
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|OverlayViewModel",
    "E:\dev\Baketa\Baketa.UI\ViewModels\TranslationViewModel.cs|TranslationViewModel",
    
    # Views
    "E:\dev\Baketa\Baketa.UI\Views\CaptureView.axaml.cs|CaptureView",
    "E:\dev\Baketa\Baketa.UI\Views\HistoryView.axaml.cs|HistoryView",
    "E:\dev\Baketa\Baketa.UI\Views\HomeView.axaml.cs|HomeView",
    "E:\dev\Baketa\Baketa.UI\Views\MainWindow.axaml.cs|MainWindow",
    "E:\dev\Baketa\Baketa.UI\Views\OverlayView.axaml.cs|OverlayView",
    "E:\dev\Baketa\Baketa.UI\Views\TranslationView.axaml.cs|TranslationView",
    
    # Events
    "E:\dev\Baketa\Baketa.UI\ViewModels\CaptureViewModel.cs|StopCaptureRequestedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\HomeViewModel.cs|StartCaptureRequestedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\HomeViewModel.cs|CaptureStatusChangedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\HistoryViewModel.cs|TranslationCompletedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\HistoryViewModel.cs|TranslationHistoryItem",
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|ApplicationExitRequestedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|MinimizeToTrayRequestedEvent", 
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|TranslationSettingsChangedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|TranslationErrorEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|OverlaySettingsChangedEvent",
    "E:\dev\Baketa\Baketa.UI\App.axaml.cs|ApplicationStartupEvent",
    "E:\dev\Baketa\Baketa.UI\App.axaml.cs|ApplicationShutdownEvent",
    
    # Framework
    "E:\dev\Baketa\Baketa.UI\Framework\Navigation\NavigationManager.cs|NavigationManager",
    "E:\dev\Baketa\Baketa.UI\Framework\Navigation\ScreenAdapter.cs|ScreenAdapter",
    
    # Examples
    "E:\dev\Baketa\Baketa.UI\ViewModels\Examples\ReactiveViewModelExample.cs|ReactiveViewModelExample",
    "E:\dev\Baketa\Baketa.UI\ViewModels\Examples\ReactiveViewModelExample.cs|DataSavedEvent",
    "E:\dev\Baketa\Baketa.UI\ViewModels\Examples\ReactiveViewModelExample.cs|DataRequestEvent"
)

# 警告CA1852：sealed キーワードの追加
foreach ($item in $sealableClasses) {
    $parts = $item -split '\|'
    $filePath = $parts[0]
    $className = $parts[1]
    
    if (Test-Path $filePath) {
        $content = Get-Content -Path $filePath -Raw
        
        # クラス定義を見つけて sealed キーワードを追加
        $pattern = "(?<=(internal|public))\s+class\s+$className\b"
        $replacement = " sealed class $className"
        
        if ($content -match $pattern) {
            $newContent = $content -replace $pattern, $replacement
            # ファイルに書き戻す
            Set-Content -Path $filePath -Value $newContent
            Write-Host "Added 'sealed' to class $className in $filePath"
        } else {
            Write-Host "Class $className not found in $filePath or already has 'sealed' keyword" -ForegroundColor Yellow
        }
    } else {
        Write-Host "File not found: $filePath" -ForegroundColor Red
    }
}

# 警告CA1805：デフォルト値の初期化を削除
$defaultInitializers = @(
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|_isCapturing = false",
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|_isOverlayVisible = false",
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|_isBold = false",
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|_offsetX = 0",
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|_offsetY = 0"
)

foreach ($item in $defaultInitializers) {
    $parts = $item -split '\|'
    $filePath = $parts[0]
    $initializer = $parts[1]
    
    if (Test-Path $filePath) {
        $content = Get-Content -Path $filePath -Raw
        
        # 初期化子を検索して削除（var名のみ残す）
        $varName = ($initializer -split '=')[0].Trim()
        $pattern = "$varName\s*=\s*(?:false|0)\s*;"
        $replacement = "$varName;"
        
        if ($content -match $pattern) {
            $newContent = $content -replace $pattern, $replacement
            # ファイルに書き戻す
            Set-Content -Path $filePath -Value $newContent
            Write-Host "Removed default initializer for $varName in $filePath"
        } else {
            Write-Host "Default initializer for $varName not found in $filePath" -ForegroundColor Yellow
        }
    } else {
        Write-Host "File not found: $filePath" -ForegroundColor Red
    }
}

# 警告CS0108：継承メンバーを隠す警告
$hidingMembers = @(
    "E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs|ErrorMessage"
)

foreach ($item in $hidingMembers) {
    $parts = $item -split '\|'
    $filePath = $parts[0]
    $memberName = $parts[1]
    
    if (Test-Path $filePath) {
        $content = Get-Content -Path $filePath -Raw
        
        # メンバー定義を検索して new キーワードを追加
        $pattern = "public\s+([^\s]+)\s+$memberName\s*{\s*get"
        $replacement = "public new `$1 $memberName { get"
        
        if ($content -match $pattern) {
            $newContent = $content -replace $pattern, $replacement
            # ファイルに書き戻す
            Set-Content -Path $filePath -Value $newContent
            Write-Host "Added 'new' keyword to member $memberName in $filePath"
        } else {
            Write-Host "Member $memberName not found in $filePath" -ForegroundColor Yellow
        }
    } else {
        Write-Host "File not found: $filePath" -ForegroundColor Red
    }
}

# null非許容プロパティの初期化
$nullableProperties = @(
    "E:\dev\Baketa\Baketa.UI\ViewModels\OverlayViewModel.cs|ResetSettingsCommand = ReactiveCommandFactory.Create(ExecuteResetToDefaultsAsync);"
)

foreach ($item in $nullableProperties) {
    $parts = $item -split '\|'
    $filePath = $parts[0]
    $initCode = $parts[1]
    
    if (Test-Path $filePath) {
        $content = Get-Content -Path $filePath -Raw
        $className = ($filePath -split '\\')[-1].Replace(".cs", "")
        
        # コンストラクタを見つけて初期化コードを追加
        $pattern = "(?<=public\s+$className.*?{)(?:\s*//[^\n]*\n|\s*\r\n)*\s*"
        $replacement = "`n            // コマンドの初期化`n            $initCode`n            "
        
        if ($content -match $pattern) {
            $newContent = $content -replace $pattern, $replacement
            # ファイルに書き戻す
            Set-Content -Path $filePath -Value $newContent
            Write-Host "Added initialization for property in $filePath"
        } else {
            Write-Host "Constructor not found in $filePath" -ForegroundColor Yellow
        }
    } else {
        Write-Host "File not found: $filePath" -ForegroundColor Red
    }
}

Write-Host "警告修正スクリプトが完了しました。" -ForegroundColor Green
