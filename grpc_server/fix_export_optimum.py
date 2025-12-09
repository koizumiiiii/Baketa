#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Issue #197: Surya OCR Recognition Model ONNX変換
Optimumを使用してHuggingFace IDから直接エクスポート
"""
import sys
import time
from pathlib import Path

# UTF-8出力設定
sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

def export_surya_recognition():
    """Surya Recognition ModelをONNXにエクスポート"""
    from optimum.onnxruntime import ORTModelForVision2Seq
    from transformers import AutoProcessor

    # 出力先設定
    OUTPUT_DIR = Path("Models/surya-onnx/recognition")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    # Suryaの認識モデルID (v0.17系)
    # 注: vikp/surya_rec2 がアクセス不可の場合は別のIDを試す
    MODEL_ID = "vikp/surya_rec2"

    print(f"=== Exporting Surya Recognition Model ===")
    print(f"Model ID: {MODEL_ID}")
    print(f"Output: {OUTPUT_DIR}")
    print()

    try:
        start_time = time.time()

        # 1. プロセッサの保存
        print("[1/3] Loading Processor...")
        try:
            processor = AutoProcessor.from_pretrained(MODEL_ID, trust_remote_code=True)
            processor.save_pretrained(OUTPUT_DIR)
            print(f"      ✅ Processor saved")
        except Exception as e:
            print(f"      ⚠️ Processor load failed (non-fatal): {e}")

        # 2. モデルのロードとONNXエクスポート
        print("[2/3] Loading Model & Exporting to ONNX...")
        print("      (This may take several minutes...)")

        model = ORTModelForVision2Seq.from_pretrained(
            MODEL_ID,
            from_transformers=True,
            export=True,
            trust_remote_code=True,
        )

        # 3. ONNXモデルの保存
        print("[3/3] Saving ONNX Model...")
        model.save_pretrained(OUTPUT_DIR)

        elapsed = time.time() - start_time
        print()
        print(f"✅ Export Successful!")
        print(f"   Total Time: {elapsed:.2f}s")
        print()

        # 生成されたファイルの確認
        print("Generated Files:")
        total_size = 0
        for f in sorted(OUTPUT_DIR.glob("*")):
            size_mb = f.stat().st_size / (1024 * 1024)
            total_size += size_mb
            print(f"  - {f.name}: {size_mb:.2f} MB")
        print(f"  Total: {total_size:.2f} MB")

        return True

    except Exception as e:
        print()
        print(f"❌ Export Failed: {e}")
        import traceback
        traceback.print_exc()
        return False


def check_dependencies():
    """依存関係のチェック"""
    print("=== Checking Dependencies ===")

    try:
        import optimum
        try:
            version = optimum.__version__
        except AttributeError:
            from importlib.metadata import version as get_version
            version = get_version("optimum")
        print(f"✅ optimum: {version}")
    except ImportError:
        print("❌ optimum not installed")
        print("   Run: pip install optimum[onnxruntime]")
        return False

    try:
        import onnxruntime
        print(f"✅ onnxruntime: {onnxruntime.__version__}")
    except ImportError:
        print("❌ onnxruntime not installed")
        return False

    try:
        import transformers
        print(f"✅ transformers: {transformers.__version__}")
    except ImportError:
        print("❌ transformers not installed")
        return False

    try:
        import torch
        print(f"✅ torch: {torch.__version__}")
        if torch.cuda.is_available():
            print(f"   CUDA: {torch.cuda.get_device_name(0)}")
        else:
            print("   CUDA: Not available (CPU mode)")
    except ImportError:
        print("❌ torch not installed")
        return False

    print()
    return True


if __name__ == "__main__":
    print("=" * 60)
    print("Surya OCR Recognition Model ONNX Export (Issue #197)")
    print("=" * 60)
    print()

    if not check_dependencies():
        print("\n⚠️ Missing dependencies. Please install required packages.")
        sys.exit(1)

    success = export_surya_recognition()
    sys.exit(0 if success else 1)
