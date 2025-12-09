#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Surya v0.17.0 API調査スクリプト
RecognitionPredictorの内部構造を確認
"""
import sys
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

def main():
    print("=== Surya v0.17.0 API調査 ===")
    print()

    # 1. RecognitionPredictor調査
    print("1. RecognitionPredictor の属性一覧:")
    try:
        from surya.foundation import FoundationPredictor
        from surya.recognition import RecognitionPredictor

        foundation = FoundationPredictor()
        rec = RecognitionPredictor(foundation)

        # 属性一覧（_で始まらないもの）
        attrs = [a for a in dir(rec) if not a.startswith('_')]
        for attr in attrs:
            try:
                val = getattr(rec, attr)
                val_type = type(val).__name__
                print(f"  - {attr}: {val_type}")
            except Exception as e:
                print(f"  - {attr}: (error: {e})")
        print()

        # model属性が存在するか確認
        if hasattr(rec, 'model'):
            print("  ✅ model属性あり")
        else:
            print("  ❌ model属性なし")

        # processor/tokenizer など確認
        if hasattr(rec, 'processor'):
            print(f"  ✅ processor: {type(rec.processor)}")
        if hasattr(rec, 'tokenizer'):
            print(f"  ✅ tokenizer: {type(rec.tokenizer)}")

        print()

    except Exception as e:
        print(f"  ERROR: {e}")
        import traceback
        traceback.print_exc()

    # 2. DetectionPredictor調査（比較用）
    print("2. DetectionPredictor の属性一覧:")
    try:
        from surya.detection import DetectionPredictor

        det = DetectionPredictor()

        attrs = [a for a in dir(det) if not a.startswith('_')]
        for attr in attrs:
            try:
                val = getattr(det, attr)
                val_type = type(val).__name__
                print(f"  - {attr}: {val_type}")
            except Exception as e:
                print(f"  - {attr}: (error: {e})")

        if hasattr(det, 'model'):
            print("  ✅ model属性あり")
        else:
            print("  ❌ model属性なし")

    except Exception as e:
        print(f"  ERROR: {e}")

    # 3. FoundationPredictor調査
    print("\n3. FoundationPredictor の属性一覧:")
    try:
        from surya.foundation import FoundationPredictor
        fp = FoundationPredictor()

        attrs = [a for a in dir(fp) if not a.startswith('_')]
        for attr in attrs:
            try:
                val = getattr(fp, attr)
                val_type = type(val).__name__
                print(f"  - {attr}: {val_type}")
            except Exception as e:
                print(f"  - {attr}: (error: {e})")

    except Exception as e:
        print(f"  ERROR: {e}")

if __name__ == "__main__":
    main()
