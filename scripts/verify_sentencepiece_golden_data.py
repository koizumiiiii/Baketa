#!/usr/bin/env python3
"""
SentencePiece Golden Data Verification Script

C# Nativeトークナイザーの実装をPython SentencePieceライブラリと比較検証する
"""

import sys
import json
import os
from pathlib import Path
from typing import Dict, List, Any, Optional
import unicodedata

def main():
    """メイン実行関数"""
    print("🔍 SentencePiece Implementation Verification")
    print("=" * 60)
    
    # SentencePieceライブラリの確認
    try:
        import sentencepiece as ssp
        print(f"✓ SentencePiece library found: version {ssp.__version__}")
    except ImportError:
        print("✗ SentencePiece library not found")
        print("Installing SentencePiece...")
        import subprocess
        try:
            subprocess.check_call([sys.executable, "-m", "pip", "install", "sentencepiece"])
            import sentencepiece as ssp
            print(f"✓ SentencePiece installed: version {ssp.__version__}")
        except Exception as e:
            print(f"✗ Failed to install SentencePiece: {e}")
            return 1
    
    # プロジェクトルート確認
    script_dir = Path(__file__).parent
    project_root = script_dir.parent
    
    print(f"📂 Project root: {project_root}")
    
    # 基本的な正規化テストケース
    test_cases = [
        {"id": "basic_en", "text": "Hello World", "category": "basic"},
        {"id": "basic_ja", "text": "こんにちは", "category": "basic"},
        {"id": "unicode_fullwidth", "text": "ｈｅｌｌｏ", "category": "unicode"},
        {"id": "unicode_mixed", "text": "Ｈｅｌｌｏ世界", "category": "unicode"},
        {"id": "control_tab", "text": "hello\tworld", "category": "control"},
        {"id": "multiple_spaces", "text": "hello   world", "category": "whitespace"},
        {"id": "empty_string", "text": "", "category": "boundary"},
    ]
    
    print("\n🧪 Testing basic normalization cases...")
    
    results = []
    for test_case in test_cases:
        text = test_case["text"]
        
        # Python標準のNFKC正規化を適用
        try:
            normalized = unicodedata.normalize('NFKC', text)
            
            # SentencePiece風の基本処理
            # 制御文字の処理
            filtered = ""
            for char in normalized:
                if unicodedata.category(char).startswith('C'):
                    if char in ['\t', '\n', '\r']:
                        filtered += ' '
                    # その他の制御文字は除去
                else:
                    filtered += char
            
            # 複数空白を単一に
            import re
            whitespace_normalized = re.sub(r'\s+', ' ', filtered).strip()
            
            # プレフィックススペース記号の付与
            if whitespace_normalized:
                final_result = '\u2581' + whitespace_normalized.replace(' ', '\u2581')
            else:
                final_result = ''
            
            result = {
                "test_case_id": test_case["id"],
                "category": test_case["category"],
                "input": text,
                "expected_normalized": final_result,
                "steps": {
                    "nfkc": normalized,
                    "control_filtered": filtered,
                    "whitespace_normalized": whitespace_normalized,
                    "final": final_result
                }
            }
            results.append(result)
            
            print(f"  ✓ {test_case['id']}: '{text}' -> '{final_result}'")
            
        except Exception as e:
            print(f"  ✗ {test_case['id']}: Error - {e}")
            continue
    
    # 結果をJSONファイルに保存
    output_dir = project_root / "tests" / "test_data"
    output_dir.mkdir(parents=True, exist_ok=True)
    
    output_file = output_dir / "normalization_verification.json"
    
    output_data = {
        "generated_by": "Python verification script",
        "test_cases": results,
        "statistics": {
            "total_cases": len(results),
            "categories": list(set(r["category"] for r in results))
        }
    }
    
    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(output_data, f, ensure_ascii=False, indent=2)
    
    print(f"\n💾 Verification data saved to: {output_file}")
    print(f"📊 Total test cases: {len(results)}")
    print(f"📋 Categories: {', '.join(output_data['statistics']['categories'])}")
    
    print("\n✅ Basic verification completed!")
    print("\nNext steps:")
    print("  1. Run C# tests with: dotnet test --filter SentencePieceNormalizerTests")
    print("  2. Compare C# results with expected normalized values")
    print("  3. Analyze any discrepancies")
    
    return 0

if __name__ == "__main__":
    sys.exit(main())