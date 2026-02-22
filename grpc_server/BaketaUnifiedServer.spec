# -*- mode: python ; coding: utf-8 -*-
"""
Baketa Unified AI Server - PyInstaller Spec File
Issue #292: OCR AIサーバー

[Issue #458] 翻訳はC# OnnxTranslationEngineに移行済み。
このサーバーはOCR (Surya) のみを提供します。

ビルド方法:
    CPU版: .\venv_build\Scripts\pyinstaller BaketaUnifiedServer.spec
    CUDA版: .\venv_build_cuda\Scripts\pyinstaller BaketaUnifiedServer.spec

サイズ目安:
    CPU版: ~300MB
    CUDA版: ~2.5GB (GitHub 2GB制限のため分割が必要)
"""

a = Analysis(
    ['unified_server.py'],
    pathex=['.'],
    binaries=[],
    datas=[
        ('protos', 'protos'),
        ('resource_monitor.py', '.'),
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

        # リソース監視
        'resource_monitor',

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
        'cv2',

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
        'pandas',
        'scipy',
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
    name='BaketaUnifiedServer',
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
    name='BaketaUnifiedServer',
)
