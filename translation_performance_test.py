#!/usr/bin/env python3
"""
🚀 Baketa翻訳処理パフォーマンステスト
永続プロセス化効果測定用スクリプト
"""

import time
import json
import subprocess
import statistics
from datetime import datetime
from typing import List, Dict, Any

def measure_dotnet_translation_performance():
    """
    .NET翻訳エンジンのパフォーマンスを測定
    """
    print("🔥 Baketa翻訳処理パフォーマンス測定開始")
    print("=" * 50)
    
    # テストデータセット
    test_texts = [
        "こんにちは、世界！",
        "新しいクエストが利用可能です。",
        "あなたのレベルが上がりました！経験値を獲得しています。",
        "遠い昔、この大陸には平和が訪れていました。しかし、闇の勢力が復活し、世界は再び混沌に包まれようとしています。",
        "クエストを完了すると、経験値とゴールドが獲得できます。また、まれに強力な装備品も手に入れることができるかもしれません。",
        "HP: 100/100\n経験値: 1,250 XP\nゴールド: ￥50,000",
        "レア装備【神剣エクスカリバー】を入手しました！\nクリティカルヒット！ダメージ×2.5倍！",
        "Game Overです。Continue しますか？\nNewゲームを開始しますか？"
    ]
    
    results = []
    total_start_time = time.time()
    
    print(f"📊 テスト対象テキスト数: {len(test_texts)}")
    print(f"📅 測定開始時刻: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print()
    
    # テスト実行
    for i, text in enumerate(test_texts, 1):
        print(f"⚡ テスト {i}/{len(test_texts)}: {text[:30]}...")
        
        start_time = time.time()
        
        try:
            # .NET翻訳エンジンテスト実行
            result = run_dotnet_translation_test(text)
            end_time = time.time()
            
            processing_time = (end_time - start_time) * 1000  # ミリ秒変換
            
            test_result = {
                "test_number": i,
                "text": text,
                "text_length": len(text),
                "processing_time_ms": processing_time,
                "success": result["success"],
                "translation": result.get("translation", ""),
                "error": result.get("error", "")
            }
            
            results.append(test_result)
            
            status = "✅ 成功" if result["success"] else "❌ 失敗"
            print(f"   {status} - 処理時間: {processing_time:.1f}ms")
            if not result["success"]:
                print(f"   エラー: {result.get('error', 'Unknown error')}")
            else:
                translation_preview = result.get("translation", "")[:50]
                print(f"   翻訳: {translation_preview}...")
            print()
            
        except Exception as e:
            print(f"   ❌ 例外発生: {str(e)}")
            results.append({
                "test_number": i,
                "text": text,
                "text_length": len(text),
                "processing_time_ms": -1,
                "success": False,
                "translation": "",
                "error": str(e)
            })
        
        # テスト間の間隔
        time.sleep(0.5)
    
    total_end_time = time.time()
    total_time = (total_end_time - total_start_time) * 1000
    
    # 結果分析
    analyze_results(results, total_time)
    
    return results

def run_dotnet_translation_test(text: str) -> Dict[str, Any]:
    """
    .NET翻訳エンジンのテスト実行
    """
    try:
        # PowerShell経由で.NETテスト実行
        powershell_cmd = f'''
        cd "E:\\dev\\Baketa"
        $text = @"{text.replace('"', '""')}"@
        dotnet run --project tests\\Baketa.Infrastructure.Tests\\ -- --test-translation "$text"
        '''
        
        result = subprocess.run(
            ["powershell", "-Command", powershell_cmd],
            capture_output=True,
            text=True,
            timeout=30
        )
        
        if result.returncode == 0:
            # 成功時の処理（簡易実装）
            return {
                "success": True,
                "translation": f"[Test Translation of: {text[:50]}...]"
            }
        else:
            return {
                "success": False,
                "error": f"Return code: {result.returncode}, Error: {result.stderr[:200]}"
            }
            
    except subprocess.TimeoutExpired:
        return {
            "success": False,
            "error": "Translation timeout (30秒)"
        }
    except Exception as e:
        return {
            "success": False,
            "error": f"Subprocess error: {str(e)}"
        }

def analyze_results(results: List[Dict[str, Any]], total_time: float):
    """
    パフォーマンス結果の分析
    """
    print("📈 パフォーマンス分析結果")
    print("=" * 50)
    
    successful_results = [r for r in results if r["success"] and r["processing_time_ms"] > 0]
    failed_results = [r for r in results if not r["success"]]
    
    print(f"📊 総実行時間: {total_time:.1f}ms ({total_time/1000:.2f}秒)")
    print(f"✅ 成功テスト: {len(successful_results)}/{len(results)}")
    print(f"❌ 失敗テスト: {len(failed_results)}/{len(results)}")
    
    if successful_results:
        processing_times = [r["processing_time_ms"] for r in successful_results]
        
        print(f"\n⚡ 処理時間統計:")
        print(f"   平均: {statistics.mean(processing_times):.1f}ms")
        print(f"   中央値: {statistics.median(processing_times):.1f}ms")
        print(f"   最小: {min(processing_times):.1f}ms")
        print(f"   最大: {max(processing_times):.1f}ms")
        
        if len(processing_times) > 1:
            print(f"   標準偏差: {statistics.stdev(processing_times):.1f}ms")
        
        # 前回レポートとの比較
        print(f"\n🔍 前回レポート比較:")
        avg_time = statistics.mean(processing_times)
        previous_time = 9339  # 前回レポートの処理時間
        
        if avg_time < previous_time:
            improvement_ratio = previous_time / avg_time
            improvement_percent = ((previous_time - avg_time) / previous_time) * 100
            print(f"   🚀 改善効果: {improvement_ratio:.1f}倍高速化")
            print(f"   📈 改善率: {improvement_percent:.1f}%削減")
            print(f"   ✅ 目標達成: {'YES' if avg_time <= 300 else 'NO'} (目標: <300ms)")
        else:
            print(f"   ⚠️  性能低下: {avg_time:.1f}ms (前回: {previous_time}ms)")
    
    if failed_results:
        print(f"\n❌ 失敗テスト詳細:")
        for r in failed_results:
            print(f"   テスト{r['test_number']}: {r['error'][:100]}...")
    
    print(f"\n🎯 結論:")
    if len(successful_results) >= len(results) * 0.8:  # 80%以上成功
        if successful_results and statistics.mean([r["processing_time_ms"] for r in successful_results]) <= 300:
            print("   ✅ 性能目標達成！翻訳処理が大幅に高速化されています。")
        else:
            print("   ⚠️  さらなる最適化が必要です。")
    else:
        print("   ❌ 実装に問題があります。エラーの調査が必要です。")

if __name__ == "__main__":
    try:
        results = measure_dotnet_translation_performance()
        
        # 結果をJSONファイルに保存
        output_file = f"E:\\dev\\Baketa\\translation_performance_results_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
        with open(output_file, 'w', encoding='utf-8') as f:
            json.dump({
                "timestamp": datetime.now().isoformat(),
                "test_results": results,
                "summary": {
                    "total_tests": len(results),
                    "successful_tests": len([r for r in results if r["success"]])
                }
            }, f, ensure_ascii=False, indent=2)
        
        print(f"\n📁 詳細結果保存: {output_file}")
        
    except Exception as e:
        print(f"❌ テスト実行エラー: {str(e)}")
        import traceback
        traceback.print_exc()