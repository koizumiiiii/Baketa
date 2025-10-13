#!/usr/bin/env python
"""GPU環境診断スクリプト - NLLB-200モデル実行可能性チェック"""
import sys
import torch

def diagnose_gpu_environment():
    print("=" * 80)
    print("GPU Environment Diagnostic Report - Baketa Translation System")
    print("=" * 80)

    # Python環境
    print(f"\n[Python Environment]")
    print(f"  Python Version: {sys.version}")
    print(f"  PyTorch Version: {torch.__version__}")

    # CUDA availability
    print(f"\n[CUDA Availability]")
    cuda_available = torch.cuda.is_available()
    print(f"  CUDA Available: {cuda_available}")

    if not cuda_available:
        print("\n⚠️ CUDA is not available. Only CPU mode is possible.")
        print("  Possible reasons:")
        print("  - NVIDIA GPU driver not installed")
        print("  - PyTorch CPU-only version installed")
        print("  - CUDA Runtime mismatch")
        return

    print(f"  CUDA Version (Runtime): {torch.version.cuda}")

    # cuDNN version
    try:
        import torch.backends.cudnn as cudnn
        print(f"  cuDNN Version: {cudnn.version()}")
        print(f"  cuDNN Enabled: {cudnn.enabled}")
    except Exception as e:
        print(f"  cuDNN Version: Error - {e}")

    # GPU details
    gpu_count = torch.cuda.device_count()
    print(f"\n[GPU Hardware]")
    print(f"  GPU Count: {gpu_count}")

    for i in range(gpu_count):
        props = torch.cuda.get_device_properties(i)
        free_mem, total_mem = torch.cuda.mem_get_info(i)

        print(f"\n  --- GPU {i} ---")
        print(f"    Name: {props.name}")
        print(f"    Compute Capability: {props.major}.{props.minor}")
        print(f"    Total VRAM: {total_mem / 1024**3:.2f} GB")
        print(f"    Free VRAM: {free_mem / 1024**3:.2f} GB")
        print(f"    Used VRAM: {(total_mem - free_mem) / 1024**3:.2f} GB")
        print(f"    Multi-Processor Count: {props.multi_processor_count}")

    # Transformers version
    try:
        import transformers
        print(f"\n[Transformers Library]")
        print(f"  Version: {transformers.__version__}")
    except ImportError:
        print(f"\n[Transformers Library]")
        print(f"  Version: Not installed")

    # NLLB-200 memory requirements
    print("\n" + "=" * 80)
    print("NLLB-200 Model Memory Requirements")
    print("=" * 80)
    print("facebook/nllb-200-distilled-600M (600M parameters):")
    print("  - float32 (CPU):  ~2.4 GB System RAM")
    print("  - float16 (GPU):  ~1.2 GB VRAM")
    print("  - int8 (GPU):     ~0.6 GB VRAM")
    print("  - CTranslate2:    ~0.5 GB VRAM (int8)")

    # Recommendation
    print("\n" + "=" * 80)
    print("Recommendation for Baketa")
    print("=" * 80)

    if gpu_count > 0:
        free_mem, _ = torch.cuda.mem_get_info(0)
        free_gb = free_mem / 1024**3

        print(f"\nCurrent Free VRAM: {free_gb:.2f} GB")

        if free_gb >= 1.5:
            print("\n✅ RECOMMENDATION: float16 mode")
            print("   - Sufficient VRAM available")
            print("   - Best quality/speed balance")
            print("   - device_map={{'': 0}} (explicit GPU 0)")
        elif free_gb >= 0.8:
            print("\n⚠️ RECOMMENDATION: int8 quantization mode")
            print("   - Limited VRAM, quantization recommended")
            print("   - Quality: ~99% of float16 (acceptable for game translation)")
            print("   - Speed: 1.5-2.0x faster than float16")
        else:
            print("\n❌ RECOMMENDATION: CTranslate2 or CPU mode")
            print("   - Insufficient VRAM for standard PyTorch")
            print("   - CTranslate2: 80% memory reduction (500MB)")
            print("   - CPU fallback: Slower but stable")

    # Known issues check
    print("\n" + "=" * 80)
    print("Known Compatibility Issues Check")
    print("=" * 80)

    pytorch_ver = torch.__version__
    cuda_ver = torch.version.cuda if torch.cuda.is_available() else "N/A"

    # PyTorch 2.5.1 + CUDA 12.4 known issues
    if pytorch_ver.startswith("2.5") and cuda_ver and cuda_ver.startswith("12.4"):
        print("\n⚠️ POTENTIAL ISSUE DETECTED:")
        print("  PyTorch 2.5.x + CUDA 12.4 has reported access violation issues")
        print("  with some transformer models.")
        print("\n  RECOMMENDED ACTIONS:")
        print("  1. Downgrade to PyTorch 2.4.1 + CUDA 12.1 (stable)")
        print("  2. Or migrate to CTranslate2 (bypasses PyTorch issues)")

    # Transformers 4.47+ known issues
    try:
        import transformers
        trans_ver = transformers.__version__
        if trans_ver.startswith("4.47") or trans_ver.startswith("4.48"):
            print("\n⚠️ POTENTIAL ISSUE DETECTED:")
            print("  Transformers 4.47+ has memory management changes")
            print("  that may cause access violations with NLLB models.")
            print("\n  RECOMMENDED ACTIONS:")
            print("  1. Downgrade to transformers==4.45.2 (stable)")
            print("  2. Or wait for 4.48.1+ (bug fixes expected)")
    except ImportError:
        pass

    print("\n" + "=" * 80)
    print("Diagnostic Complete")
    print("=" * 80)

if __name__ == "__main__":
    try:
        diagnose_gpu_environment()
    except Exception as e:
        print(f"\n❌ DIAGNOSTIC FAILED: {type(e).__name__}")
        print(f"   Error: {e}")
        import traceback
        traceback.print_exc()
