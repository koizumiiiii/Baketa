#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import re
import os
from datetime import datetime

# 対象ファイルのリスト（最も頻繁に使用されるもの優先）
target_files = [
    r"E:\dev\Baketa\Baketa.Application\Services\Translation\TranslationOrchestrationService.cs",
    r"E:\dev\Baketa\Baketa.Infrastructure\OCR\PaddleOCR\Engine\PaddleOcrEngine.cs",
    r"E:\dev\Baketa\Baketa.Infrastructure\OCR\TextProcessing\JapaneseTextMerger.cs",
    r"E:\dev\Baketa\Baketa.Infrastructure\OCR\PostProcessing\UniversalMisrecognitionCorrector.cs",
    r"E:\dev\Baketa\Baketa.Infrastructure\OCR\PostProcessing\ConfidenceBasedReprocessor.cs",
    r"E:\dev\Baketa\Baketa.Infrastructure\OCR\BatchProcessing\BatchOcrProcessor.cs",
    r"E:\dev\Baketa\Baketa.Infrastructure\Imaging\Services\GameOptimizedPreprocessingService.cs",
    r"E:\dev\Baketa\Baketa.Application\Services\Translation\CoordinateBasedTranslationService.cs",
    r"E:\dev\Baketa\Baketa.Application\Services\Translation\StreamingTranslationService.cs",
    r"E:\dev\Baketa\Baketa.UI\Services\AvaloniaNavigationService.cs",
    r"E:\dev\Baketa\Baketa.UI\Services\TranslationFlowEventProcessor.cs",
    r"E:\dev\Baketa\Baketa.UI\ViewModels\HomeViewModel.cs",
    r"E:\dev\Baketa\Baketa.UI\ViewModels\MainOverlayViewModel.cs",
    r"E:\dev\Baketa\Baketa.UI\ViewModels\MainWindowViewModel.cs",
    r"E:\dev\Baketa\Baketa.UI\ViewModels\SimpleSettingsViewModel.cs",
    r"E:\dev\Baketa\Baketa.Core\Events\Implementation\EventAggregator.cs"
]

def process_file(file_path):
    """ファイル内のFile.AppendAllText呼び出しをコメントアウト"""
    if not os.path.exists(file_path):
        print(f"WARNING: ファイルが見つかりません: {file_path}")
        return False
    
    print(f"処理中: {file_path}")
    
    # バックアップ作成
    backup_path = file_path + f".backup_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
    
    try:
        # ファイル内容を読み込み
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # バックアップ保存
        with open(backup_path, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"バックアップ作成: {backup_path}")
        
        # debug_app_logs.txtを含むFile.AppendAllText行をコメントアウト
        pattern = r'^(\s*)(System\.IO\.)?File\.AppendAllText\([^)]*debug_app_logs\.txt[^)]*\)'
        
        def replace_func(match):
            indent = match.group(1)
            prefix = match.group(2) or ""
            return f"{indent}// {prefix}File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化"
        
        # 複数行置換
        new_content = re.sub(pattern, replace_func, content, flags=re.MULTILINE)
        
        # 変更があった場合のみ保存
        if new_content != content:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(new_content)
            
            # 変更箇所数を計算
            changes = len(re.findall(pattern, content, flags=re.MULTILINE))
            print(f"完了: {changes}箇所をコメントアウトしました")
            return True
        else:
            print(f"変更なし（既に処理済み）")
            # 不要なバックアップファイルを削除
            os.remove(backup_path)
            return False
            
    except Exception as e:
        print(f"エラー: {e}")
        return False

def main():
    """メイン処理"""
    print("File.AppendAllText 無効化処理開始")
    print(f"対象ファイル数: {len(target_files)}")
    
    success_count = 0
    total_changes = 0
    
    for file_path in target_files:
        if process_file(file_path):
            success_count += 1
        print("-" * 60)
    
    print(f"処理結果:")
    print(f"   処理対象: {len(target_files)}ファイル")
    print(f"   成功: {success_count}ファイル")
    print(f"   診断レポートシステムにより、debug_app_logs.txtへの出力が無効化されました")
    print("処理完了")

if __name__ == "__main__":
    main()