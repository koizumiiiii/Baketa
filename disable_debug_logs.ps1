# debug_app_logs.txtへの全てのFile.AppendAllText呼び出しを無効化するスクリプト

$files = rg "File\.AppendAllText.*debug_app_logs" -t cs -l

foreach ($file in $files) {
    Write-Host "Processing: $file"
    
    # ファイル内容を読み取り
    $content = Get-Content $file -Raw
    
    # 様々なパターンを無効化
    $newContent = $content -replace 'System\.IO\.File\.AppendAllText', '// System.IO.File.AppendAllText'
    $newContent = $newContent -replace '(\s+)File\.AppendAllText', '$1// File.AppendAllText'
    $newContent = $newContent -replace '^\s*File\.AppendAllText', '// File.AppendAllText'
    
    # ファイルに書き戻し
    Set-Content $file -Value $newContent -NoNewline -Encoding UTF8
}

Write-Host "完了: 全てのdebug_app_logs.txt出力を無効化しました"