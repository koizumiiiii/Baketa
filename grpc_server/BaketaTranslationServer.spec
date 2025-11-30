# -*- mode: python ; coding: utf-8 -*-


a = Analysis(
    ['start_server.py'],
    pathex=[],
    binaries=[],
    datas=[],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['onnxruntime', 'paddle', 'paddlepaddle', 'sklearn', 'scikit-learn', 'cv2', 'opencv-python', 'torchvision', 'matplotlib', 'timm', 'torchaudio', 'scipy', 'pandas', 'lxml', 'pytest', 'pytest-asyncio', 'IPython', 'notebook', 'jupyter'],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='BaketaTranslationServer',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
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
    upx=True,
    upx_exclude=[],
    name='BaketaTranslationServer',
)
