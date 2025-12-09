#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Simple INT8 quantization script for Detection Model
"""
import sys
import os
from pathlib import Path

# UTF-8 encoding for Windows
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

def main():
    print("=== INT8 Quantization ===")

    # Input/output paths
    input_path = Path(__file__).parent.parent / "Models" / "surya-onnx" / "detection" / "model.onnx"
    output_path = input_path.with_stem("model_int8")

    print(f"Input: {input_path}")
    print(f"Output: {output_path}")

    if not input_path.exists():
        print(f"ERROR: Input file not found: {input_path}")
        return 1

    try:
        from onnxruntime.quantization import quantize_dynamic, QuantType

        print("Running INT8 quantization...")
        quantize_dynamic(
            model_input=str(input_path),
            model_output=str(output_path),
            weight_type=QuantType.QUInt8,
        )

        # Check results
        original_size = input_path.stat().st_size / (1024 * 1024)
        quantized_size = output_path.stat().st_size / (1024 * 1024)
        reduction = (1 - quantized_size / original_size) * 100

        print(f"Original: {original_size:.1f} MB")
        print(f"Quantized: {quantized_size:.1f} MB")
        print(f"Reduction: {reduction:.1f}%")
        print("SUCCESS!")
        return 0

    except ImportError as e:
        print(f"ERROR: Missing dependency: {e}")
        print("Run: pip install onnxruntime")
        return 1
    except Exception as e:
        print(f"ERROR: {e}")
        import traceback
        traceback.print_exc()
        return 1

if __name__ == "__main__":
    sys.exit(main())
