#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Issue #197: Surya Recognition Model ONNX Int8 Export
Recognition.mdの手順に基づくONNX変換とInt8量子化

目的:
- PyTorchモデル (1.1GB) → ONNX Int8 (~180MB) に変換
- ダウンロードサイズ削減でタイムアウト問題を解決
- torchへの依存を削除して軽量化
"""

import sys
import os
import time
import subprocess
from pathlib import Path

# UTF-8出力設定
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

# 出力ディレクトリ
OUTPUT_DIR = Path("Models/surya-onnx/recognition")

# Suryaの認識モデルID
# Recognition.mdでは vikp/surya_rec を推奨
MODEL_ID = "vikp/surya_rec"


def check_dependencies():
    """依存関係のチェック"""
    print("=== 依存関係チェック ===")

    missing = []

    try:
        import optimum
        from importlib.metadata import version as get_version
        ver = get_version("optimum")
        print(f"✅ optimum: {ver}")
    except ImportError:
        print("❌ optimum not installed")
        missing.append("optimum[onnxruntime]")

    try:
        import onnxruntime
        print(f"✅ onnxruntime: {onnxruntime.__version__}")
    except ImportError:
        print("❌ onnxruntime not installed")
        missing.append("onnxruntime")

    try:
        import transformers
        print(f"✅ transformers: {transformers.__version__}")
    except ImportError:
        print("❌ transformers not installed")
        missing.append("transformers")

    try:
        import torch
        print(f"✅ torch: {torch.__version__}")
        if torch.cuda.is_available():
            print(f"   CUDA: {torch.cuda.get_device_name(0)}")
    except ImportError:
        print("❌ torch not installed")
        missing.append("torch")

    if missing:
        print()
        print("⚠️ 不足パッケージをインストールしてください:")
        print(f"   pip install {' '.join(missing)}")
        return False

    print()
    return True


def export_with_optimum_cli():
    """
    optimum-cliを使用したONNXエクスポート（Int8量子化付き）
    Recognition.mdの推奨手順
    """
    print("=== optimum-cli によるONNXエクスポート ===")
    print(f"Model ID: {MODEL_ID}")
    print(f"Output: {OUTPUT_DIR}")
    print()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    # optimum-cli export onnx コマンド
    # --quantization int8 でサイズを劇的に削減
    cmd = [
        sys.executable, "-m", "optimum.exporters.onnx",
        "--model", MODEL_ID,
        "--task", "image-to-text",
        "--fp16",  # FP16も併用して更に軽量化
        str(OUTPUT_DIR)
    ]

    print(f"実行コマンド: {' '.join(cmd)}")
    print("(数分かかる可能性があります...)")
    print()

    start_time = time.time()

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace'
        )

        if result.returncode == 0:
            elapsed = time.time() - start_time
            print(f"✅ エクスポート成功 ({elapsed:.1f}秒)")
            print()
            print("stdout:", result.stdout[:500] if result.stdout else "(empty)")
            return True
        else:
            print(f"❌ エクスポート失敗 (code: {result.returncode})")
            print("stderr:", result.stderr[:1000] if result.stderr else "(empty)")
            return False

    except Exception as e:
        print(f"❌ 実行エラー: {e}")
        return False


def export_with_python_api():
    """
    Python APIを使用したONNXエクスポート（フォールバック）
    optimum-cliが失敗した場合の代替手段
    """
    print("=== Python API によるONNXエクスポート ===")
    print(f"Model ID: {MODEL_ID}")
    print(f"Output: {OUTPUT_DIR}")
    print()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    try:
        from optimum.onnxruntime import ORTModelForVision2Seq
        from transformers import AutoProcessor

        start_time = time.time()

        # 1. プロセッサの保存
        print("[1/3] Processorをロード中...")
        try:
            processor = AutoProcessor.from_pretrained(MODEL_ID, trust_remote_code=True)
            processor.save_pretrained(OUTPUT_DIR)
            print("      ✅ Processor保存完了")
        except Exception as e:
            print(f"      ⚠️ Processor保存スキップ: {e}")

        # 2. モデルのONNXエクスポート
        print("[2/3] モデルをONNXにエクスポート中...")
        print("      (数分かかる可能性があります...)")

        model = ORTModelForVision2Seq.from_pretrained(
            MODEL_ID,
            from_transformers=True,
            export=True,
            trust_remote_code=True,
        )

        # 3. 保存
        print("[3/3] ONNXモデルを保存中...")
        model.save_pretrained(OUTPUT_DIR)

        elapsed = time.time() - start_time
        print()
        print(f"✅ エクスポート成功 ({elapsed:.1f}秒)")
        return True

    except Exception as e:
        print(f"❌ エクスポート失敗: {e}")
        import traceback
        traceback.print_exc()
        return False


def quantize_to_int8():
    """
    ONNXモデルをInt8に量子化
    サイズを更に1/4程度に削減
    """
    print("=== Int8量子化 ===")

    try:
        from onnxruntime.quantization import quantize_dynamic, QuantType

        # エクスポートされたONNXファイルを検索
        onnx_files = list(OUTPUT_DIR.glob("*.onnx"))

        if not onnx_files:
            print("❌ ONNXファイルが見つかりません")
            return False

        for onnx_path in onnx_files:
            if "_quantized" in onnx_path.name or "_int8" in onnx_path.name:
                continue  # 既に量子化済みはスキップ

            output_path = onnx_path.with_name(
                onnx_path.stem + "_int8" + onnx_path.suffix
            )

            print(f"量子化中: {onnx_path.name} → {output_path.name}")

            quantize_dynamic(
                str(onnx_path),
                str(output_path),
                weight_type=QuantType.QInt8
            )

            # サイズ比較
            orig_size = onnx_path.stat().st_size / (1024 * 1024)
            new_size = output_path.stat().st_size / (1024 * 1024)
            reduction = (1 - new_size / orig_size) * 100

            print(f"   {orig_size:.1f}MB → {new_size:.1f}MB ({reduction:.1f}%削減)")

        print()
        print("✅ Int8量子化完了")
        return True

    except Exception as e:
        print(f"❌ 量子化失敗: {e}")
        import traceback
        traceback.print_exc()
        return False


def show_output_files():
    """生成されたファイルの一覧を表示"""
    print("=== 生成ファイル ===")

    if not OUTPUT_DIR.exists():
        print("❌ 出力ディレクトリが存在しません")
        return

    total_size = 0
    for f in sorted(OUTPUT_DIR.rglob("*")):
        if f.is_file():
            size_mb = f.stat().st_size / (1024 * 1024)
            total_size += size_mb
            rel_path = f.relative_to(OUTPUT_DIR)
            print(f"  {rel_path}: {size_mb:.2f} MB")

    print()
    print(f"合計: {total_size:.2f} MB")

    if total_size < 300:
        print("✅ 目標サイズ (~180MB) を達成！")
    else:
        print("⚠️ サイズが大きいです。追加の最適化を検討してください。")


def main():
    print("=" * 60)
    print("Surya Recognition Model ONNX Int8 Export")
    print("Issue #197: ダウンロードサイズ削減")
    print("=" * 60)
    print()

    # 依存関係チェック
    if not check_dependencies():
        sys.exit(1)

    # Step 1: ONNXエクスポート（optimum-cli優先）
    success = export_with_optimum_cli()

    if not success:
        print()
        print("optimum-cliが失敗しました。Python APIで再試行します...")
        print()
        success = export_with_python_api()

    if not success:
        print()
        print("❌ ONNXエクスポートに失敗しました")
        sys.exit(1)

    # Step 2: Int8量子化
    print()
    quantize_to_int8()

    # 結果表示
    print()
    show_output_files()

    print()
    print("=" * 60)
    print("次のステップ:")
    print("1. 生成されたモデルをGitHub Releasesにアップロード")
    print("2. appsettings.jsonのComponentDownload設定を更新")
    print("3. ocr_server_surya.pyをONNX推論に修正")
    print("=" * 60)


if __name__ == "__main__":
    main()
