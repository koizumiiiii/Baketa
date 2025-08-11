#!/usr/bin/env python3
"""
Gemini 3段階ロードマップ完了後のボトルネック再調査スクリプト
翻訳処理全工程の詳細なパフォーマンス分析を実施
"""

import psutil
import time
import json
import sys
from datetime import datetime
import subprocess
import threading
import os

def monitor_system_resources():
    """システムリソース監視"""
    cpu_percent = psutil.cpu_percent(interval=0.1)
    memory_info = psutil.virtual_memory()
    disk_io = psutil.disk_io_counters()
    
    return {
        "timestamp": datetime.now().isoformat(),
        "cpu_percent": cpu_percent,
        "memory_used_mb": memory_info.used // 1024 // 1024,
        "memory_available_mb": memory_info.available // 1024 // 1024,
        "memory_percent": memory_info.percent,
        "disk_read_mb": disk_io.read_bytes // 1024 // 1024 if disk_io else 0,
        "disk_write_mb": disk_io.write_bytes // 1024 // 1024 if disk_io else 0
    }

def analyze_baketa_processes():
    """Baketaプロセス分析"""
    baketa_processes = []
    
    for proc in psutil.process_iter(['pid', 'name', 'cpu_percent', 'memory_info', 'create_time']):
        try:
            if 'baketa' in proc.info['name'].lower() or 'dotnet' in proc.info['name'].lower():
                baketa_processes.append({
                    "pid": proc.info['pid'],
                    "name": proc.info['name'],
                    "cpu_percent": proc.info['cpu_percent'],
                    "memory_mb": proc.info['memory_info'].rss // 1024 // 1024,
                    "runtime_seconds": time.time() - proc.info['create_time']
                })
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    
    return baketa_processes

def check_ocr_performance():
    """OCR性能状況確認"""
    performance_indicators = {
        "expected_improvements": {
            "step1_pooling": "14秒→5-8秒",
            "step2_staged": "バックグラウンド初期化",
            "step3_caching": "キャッシュヒット時数ミリ秒"
        },
        "target_overall": "17秒→1-2秒"
    }
    
    return performance_indicators

def main():
    """メイン分析実行"""
    print("Gemini 3-stage roadmap post-bottleneck analysis start")
    print(f"Analysis start time: {datetime.now()}")
    
    analysis_results = {
        "analysis_start": datetime.now().isoformat(),
        "system_resources": monitor_system_resources(),
        "baketa_processes": analyze_baketa_processes(),
        "ocr_performance_context": check_ocr_performance(),
        "investigation_focus": [
            "Overall translation process latency",
            "OCR cache hit rate",
            "Python server OPUS-MT response time", 
            "UI rendering and overlay display time",
            "Memory usage efficiency",
            "Background task load"
        ]
    }
    
    # 結果出力
    output_file = "post_gemini_bottleneck_analysis.json"
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(analysis_results, f, indent=2, ensure_ascii=False)
    
    print(f"Analysis results saved: {output_file}")
    
    # 主要発見事項の表示
    print("\nKey system status:")
    print(f"- CPU usage: {analysis_results['system_resources']['cpu_percent']:.1f}%")
    print(f"- Memory usage: {analysis_results['system_resources']['memory_used_mb']}MB")
    print(f"- Baketa processes: {len(analysis_results['baketa_processes'])}")
    
    print("\nRecommended next investigation steps:")
    print("1. Measure actual translation process execution time")
    print("2. Verify OCR cache hit rate measurements") 
    print("3. Measure OPUS-MT translation engine response time")
    print("4. Measure UI rendering performance")
    
    return analysis_results

if __name__ == "__main__":
    main()