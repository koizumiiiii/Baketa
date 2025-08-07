#!/usr/bin/env python3
"""
TransformersOpusMtEngineを直接テストして性能を測定
"""

import sys
import os
import time
import json

# Baketaプロジェクトルートをパスに追加
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from test_persistent_client import test_persistent_server

def test_direct_translation_performance():
    """
    常駐サーバーでの翻訳性能を詳細に測定
    """
    
    print("[PERFORMANCE TEST] TransformersOpusMtEngine Performance Verification")
    print("=" * 70)
    
    # テスト用テキスト
    test_cases = [
        "第一のスープ",
        "Game Update", 
        "新しいメッセージが届いています",
        "サーバーが正常に動作しています",
        "パフォーマンステスト実行中",
        "翻訳エンジンの検証",
        "常駐プロセスアーキテクチャ",
        "高速翻訳システム",
        "品質維持とパフォーマンス向上",
        "実装完了確認"
    ]
    
    print(f"[INFO] Testing {len(test_cases)} translation cases...")
    print(f"[INFO] Target: <0.5 seconds per translation (excluding first)")
    print()
    
    import socket
    
    try:
        # 常駐サーバーに接続
        client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        client.connect(("127.0.0.1", 29876))
        print("[OK] Connected to persistent server")
        
        total_time = 0
        successful_translations = 0
        
        for i, text in enumerate(test_cases, 1):
            start_time = time.time()
            
            # 翻訳リクエスト送信
            request = {"text": text}
            request_json = json.dumps(request, ensure_ascii=False) + "\n"
            client.send(request_json.encode('utf-8'))
            
            # レスポンス受信
            response_data = client.recv(4096).decode('utf-8')
            response = json.loads(response_data.strip())
            
            processing_time = time.time() - start_time
            
            if response.get("success"):
                server_time = response.get("processing_time", 0)
                translation = response.get("translation", "")
                
                status = "FAST" if processing_time < 0.5 else "SLOW"
                
                print(f"[{status}] Test {i:2d}: '{text[:30]:<30}' -> '{translation[:40]:<40}' "
                      f"({processing_time:.3f}s / {server_time:.3f}s)")
                
                # 初回以外の時間を集計（初回はウォームアップ）
                if i > 1:
                    total_time += processing_time
                    successful_translations += 1
            else:
                print(f"[ERROR] Test {i:2d}: '{text}' failed - {response.get('error', 'Unknown error')}")
        
        client.close()
        
        # パフォーマンス統計
        if successful_translations > 0:
            avg_time = total_time / successful_translations
            print()
            print("=" * 70)
            print("[PERFORMANCE RESULTS]")
            print(f"Successful translations: {successful_translations}")
            print(f"Average time (excluding first): {avg_time:.3f}s")
            print(f"Target achievement: {'✓ PASS' if avg_time < 0.5 else '✗ FAIL'} (target: <0.5s)")
            print(f"Performance improvement: ~{6.0/avg_time:.1f}x faster than original (6s->0.{int(avg_time*1000)}ms)")
            print("=" * 70)
            
            return avg_time < 0.5
        else:
            print("[ERROR] No successful translations")
            return False
            
    except Exception as e:
        print(f"[FAILED] Performance test failed: {e}")
        return False

if __name__ == "__main__":
    # 常駐サーバーを起動
    import subprocess
    import threading
    
    print("[SETUP] Starting persistent server...")
    
    server_process = subprocess.Popen([
        r"C:\Users\suke0\.pyenv\pyenv-win\versions\3.10.9\python.exe",
        "scripts/opus_mt_persistent_server.py"
    ], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    
    # サーバー起動待機
    time.sleep(8)  # モデルロード時間を考慮
    
    try:
        # パフォーマンステスト実行
        success = test_direct_translation_performance()
        
        if success:
            print("\n[SUCCESS] Performance test PASSED! 🎉")
            print("Implementation meets performance requirements.")
        else:
            print("\n[FAILED] Performance test FAILED!")
            
    finally:
        # サーバー停止
        server_process.terminate()
        server_process.wait()
        print("\n[CLEANUP] Server stopped")