# -*- mode: python ; coding: utf-8 -*-
"""
Baketa Surya OCR Server - PyInstaller Spec File
Issue #197: Surya OCR サーバーの配布対応

PyTorch + Surya OCRを含むため、サイズが大きくなります（約2-3GB）。
CUDA版とCPU版のビルドが可能です。
"""

a = Analysis(
    ['ocr_server_surya.py'],
    pathex=['.'],
    binaries=[],
    datas=[
        ('protos', 'protos'),
    ],
    hiddenimports=[
        # gRPC関連
        'protos',
        'protos.ocr_pb2',
        'protos.ocr_pb2_grpc',
        'grpc',
        'grpc._cython',
        'grpc._cython.cygrpc',
        'google.protobuf',
        'google.protobuf.timestamp_pb2',

        # PyTorch関連（Surya OCR依存）
        'torch',
        'torch.nn',
        'torch.nn.functional',
        'torch.cuda',
        'torch.backends',
        'torch.backends.cudnn',
        'torchvision',
        'torchvision.transforms',

        # Surya OCR関連
        'surya',
        'surya.foundation',
        'surya.recognition',
        'surya.detection',
        'surya.model',
        'surya.settings',

        # Transformers関連（Surya OCR依存）
        'transformers',
        'transformers.models',
        'transformers.modeling_utils',
        'tokenizers',

        # 画像処理
        'PIL',
        'PIL.Image',
        'numpy',
        'cv2',  # opencv-python-headless

        # その他依存
        'huggingface_hub',
        'safetensors',
        'einops',
        'pydantic',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        # 不要なパッケージを除外してサイズ削減
        'paddle',
        'paddlepaddle',
        'sklearn',
        'scikit-learn',
        'matplotlib',
        'pytest',
        'pytest-asyncio',
        'IPython',
        'notebook',
        'jupyter',
        # cv2はSurya OCRの依存関係として必要（除外しない）
    ],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='BaketaSuryaOcrServer',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,  # PyTorchはUPX圧縮非対応
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,  # PyTorchはUPX圧縮非対応
    upx_exclude=[],
    name='BaketaSuryaOcrServer',
)
