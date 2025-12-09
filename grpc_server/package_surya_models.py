#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Surya OCR モデルパッケージング スクリプト
Issue #189: モデル配布パッケージ作成

GitHub Releaseで配布するためのモデルパッケージを作成します。

配布構成:
- surya-detection-onnx.zip: Detection Model ONNX INT8 (~37MB)
- surya-recognition-pytorch.zip: Recognition Model PyTorch (~1.4GB)
"""
import sys
import os
import shutil
import zipfile
from pathlib import Path

if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

# パス設定
PROJECT_ROOT = Path(__file__).parent.parent
MODELS_DIR = PROJECT_ROOT / "Models"
SURYA_ONNX_DIR = MODELS_DIR / "surya-onnx"
OUTPUT_DIR = PROJECT_ROOT / "release_packages"

# Suryaキャッシュディレクトリ
SURYA_CACHE_DIR = Path(os.environ.get("LOCALAPPDATA", "")) / "datalab" / "datalab" / "Cache" / "models"


def get_surya_cache_dir():
    """Suryaモデルキャッシュディレクトリを取得"""
    # 環境変数から取得
    if SURYA_CACHE_DIR.exists():
        return SURYA_CACHE_DIR

    # Surya settingsから取得
    try:
        from surya.settings import settings
        cache_dir = Path(settings.MODEL_CACHE_DIR)
        if cache_dir.exists():
            return cache_dir
    except ImportError:
        pass

    return None


def package_detection_model():
    """Detection Model ONNX INT8 パッケージ作成"""
    print("=== Detection Model ONNX Package ===")

    detection_dir = SURYA_ONNX_DIR / "detection"
    int8_model = detection_dir / "model_int8.onnx"

    if not int8_model.exists():
        print(f"ERROR: INT8モデルが見つかりません: {int8_model}")
        return None

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    output_zip = OUTPUT_DIR / "surya-detection-onnx.zip"

    print(f"パッケージ作成中: {output_zip}")

    with zipfile.ZipFile(output_zip, 'w', zipfile.ZIP_DEFLATED) as zf:
        zf.write(int8_model, "detection/model_int8.onnx")

    size_mb = output_zip.stat().st_size / (1024 * 1024)
    print(f"完了: {size_mb:.2f} MB")

    return output_zip


def package_recognition_model():
    """Recognition Model PyTorch パッケージ作成"""
    print("\n=== Recognition Model PyTorch Package ===")

    cache_dir = get_surya_cache_dir()
    if cache_dir is None:
        print("ERROR: Suryaキャッシュディレクトリが見つかりません")
        return None

    # Recognition モデルディレクトリを探す
    recognition_dir = None
    for version_dir in (cache_dir / "text_recognition").iterdir():
        if version_dir.is_dir():
            model_file = version_dir / "model.safetensors"
            if model_file.exists():
                recognition_dir = version_dir
                break

    if recognition_dir is None:
        print("ERROR: Recognitionモデルが見つかりません")
        print(f"検索パス: {cache_dir / 'text_recognition'}")
        return None

    print(f"ソースディレクトリ: {recognition_dir}")

    # 必要なファイル
    required_files = [
        "model.safetensors",
        "config.json",
        "preprocessor_config.json",
        "processor_config.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "specials.json",
        "specials_dict.json",
        "vocab_math.json",
    ]

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    output_zip = OUTPUT_DIR / "surya-recognition-pytorch.zip"

    print(f"パッケージ作成中: {output_zip}")

    with zipfile.ZipFile(output_zip, 'w', zipfile.ZIP_DEFLATED) as zf:
        for filename in required_files:
            src_file = recognition_dir / filename
            if src_file.exists():
                zf.write(src_file, f"recognition/{filename}")
                print(f"  追加: {filename}")
            else:
                print(f"  警告: {filename} が見つかりません")

    size_mb = output_zip.stat().st_size / (1024 * 1024)
    print(f"完了: {size_mb:.2f} MB")

    return output_zip


def package_detection_pytorch():
    """Detection Model PyTorch パッケージ作成（オプション）"""
    print("\n=== Detection Model PyTorch Package ===")

    cache_dir = get_surya_cache_dir()
    if cache_dir is None:
        print("ERROR: Suryaキャッシュディレクトリが見つかりません")
        return None

    # Detection モデルディレクトリを探す
    detection_dir = None
    for version_dir in (cache_dir / "text_detection").iterdir():
        if version_dir.is_dir():
            model_file = version_dir / "model.safetensors"
            if model_file.exists():
                detection_dir = version_dir
                break

    if detection_dir is None:
        print("ERROR: Detectionモデルが見つかりません")
        return None

    print(f"ソースディレクトリ: {detection_dir}")

    # 必要なファイル
    required_files = [
        "model.safetensors",
        "config.json",
        "preprocessor_config.json",
    ]

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    output_zip = OUTPUT_DIR / "surya-detection-pytorch.zip"

    print(f"パッケージ作成中: {output_zip}")

    with zipfile.ZipFile(output_zip, 'w', zipfile.ZIP_DEFLATED) as zf:
        for filename in required_files:
            src_file = detection_dir / filename
            if src_file.exists():
                zf.write(src_file, f"detection/{filename}")
                print(f"  追加: {filename}")

    size_mb = output_zip.stat().st_size / (1024 * 1024)
    print(f"完了: {size_mb:.2f} MB")

    return output_zip


def list_packages():
    """作成済みパッケージを一覧表示"""
    print("\n=== 作成済みパッケージ ===")

    if not OUTPUT_DIR.exists():
        print("パッケージディレクトリがありません")
        return

    total_size = 0
    for zip_file in OUTPUT_DIR.glob("*.zip"):
        size_mb = zip_file.stat().st_size / (1024 * 1024)
        total_size += size_mb
        print(f"  {zip_file.name}: {size_mb:.2f} MB")

    print(f"\n合計: {total_size:.2f} MB")


def main():
    import argparse

    parser = argparse.ArgumentParser(description="Surya OCR モデルパッケージング")
    parser.add_argument("--detection-onnx", action="store_true", help="Detection ONNX INT8 パッケージ作成")
    parser.add_argument("--recognition", action="store_true", help="Recognition PyTorch パッケージ作成")
    parser.add_argument("--detection-pytorch", action="store_true", help="Detection PyTorch パッケージ作成")
    parser.add_argument("--all", action="store_true", help="全パッケージ作成")
    parser.add_argument("--list", action="store_true", help="作成済みパッケージを一覧表示")

    args = parser.parse_args()

    if args.list:
        list_packages()
        return 0

    packages = []

    if args.all or args.detection_onnx:
        pkg = package_detection_model()
        if pkg:
            packages.append(pkg)

    if args.all or args.recognition:
        pkg = package_recognition_model()
        if pkg:
            packages.append(pkg)

    if args.detection_pytorch:
        pkg = package_detection_pytorch()
        if pkg:
            packages.append(pkg)

    if not packages and not args.list:
        parser.print_help()
        return 1

    print("\n=== パッケージ作成完了 ===")
    for pkg in packages:
        print(f"  {pkg}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
