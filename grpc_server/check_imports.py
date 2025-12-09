#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Check imports for ONNX detection"""
import sys

if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

def check_imports():
    print("Checking imports...")

    try:
        import onnxruntime
        print(f"OK: onnxruntime {onnxruntime.__version__}")
    except ImportError as e:
        print(f"FAIL: onnxruntime - {e}")
        return False

    try:
        import cv2
        print(f"OK: opencv {cv2.__version__}")
    except ImportError as e:
        print(f"FAIL: cv2 - {e}")
        return False

    try:
        from PIL import Image
        import PIL
        print(f"OK: PIL {PIL.__version__}")
    except ImportError as e:
        print(f"FAIL: PIL - {e}")
        return False

    try:
        import numpy as np
        print(f"OK: numpy {np.__version__}")
    except ImportError as e:
        print(f"FAIL: numpy - {e}")
        return False

    print("\nAll imports OK!")
    return True

if __name__ == "__main__":
    success = check_imports()
    sys.exit(0 if success else 1)
