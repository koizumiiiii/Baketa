#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Surya OCR ONNX Export Script
Issue #189: モデル軽量化 - PyTorch → ONNX変換＋INT8量子化

使用方法:
    python export_surya_onnx.py --export-all
    python export_surya_onnx.py --export-detection
    python export_surya_onnx.py --quantize
    python export_surya_onnx.py --verify
"""

import os
import sys
import time
import argparse
import logging
from pathlib import Path

# UTF-8エンコーディング強制（Windows対応）
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# 出力ディレクトリ
OUTPUT_DIR = Path(__file__).parent.parent / "Models" / "surya-onnx"


def ensure_output_dir():
    """出力ディレクトリを作成"""
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    (OUTPUT_DIR / "detection").mkdir(exist_ok=True)
    (OUTPUT_DIR / "recognition").mkdir(exist_ok=True)
    logger.info(f"出力ディレクトリ: {OUTPUT_DIR}")


def export_detection_model():
    """
    Detection Model (Line Detector) をONNXにエクスポート

    入力: [batch, 3, height, width] - RGB画像
    出力: [batch, num_classes, height/4, width/4] - セグメンテーションマスク
    """
    import torch

    logger.info("=== Detection Model Export ===")

    try:
        # Surya v0.17.0 API
        from surya.detection import DetectionPredictor

        logger.info("DetectionPredictor をロード中...")
        start_time = time.time()

        predictor = DetectionPredictor()
        model = predictor.model
        model.eval()

        logger.info(f"モデルロード完了: {time.time() - start_time:.2f}秒")

        # ダミー入力（1024x1024がSuryaのデフォルト）
        dummy_input = torch.randn(1, 3, 1024, 1024)

        output_path = OUTPUT_DIR / "detection" / "model.onnx"

        logger.info(f"ONNXエクスポート開始: {output_path}")

        torch.onnx.export(
            model,
            dummy_input,
            str(output_path),
            input_names=['pixel_values'],
            output_names=['segmentation_mask'],
            dynamic_axes={
                'pixel_values': {0: 'batch', 2: 'height', 3: 'width'},
                'segmentation_mask': {0: 'batch', 2: 'height', 3: 'width'}
            },
            opset_version=14,
            do_constant_folding=True,
            export_params=True,
        )

        file_size_mb = output_path.stat().st_size / (1024 * 1024)
        logger.info(f"Detection Model エクスポート完了: {file_size_mb:.1f} MB")

        return str(output_path)

    except Exception as e:
        logger.error(f"Detection Model エクスポート失敗: {e}")
        raise


def export_recognition_model():
    """
    Recognition Model (Foundation Model) をONNXにエクスポート

    Surya v0.17.0では、Recognition = FoundationPredictor.model (SuryaModel)
    """
    import torch

    logger.info("=== Recognition Model Export ===")

    try:
        from surya.foundation import FoundationPredictor

        logger.info("FoundationPredictor をロード中...")
        start_time = time.time()

        foundation = FoundationPredictor()

        logger.info(f"モデルロード完了: {time.time() - start_time:.2f}秒")

        # FoundationPredictorの内部モデルを取得 (SuryaModel)
        # Surya v0.17.0 API: foundation_predictor.model
        model = foundation.model
        model.eval()

        # ダミー入力
        # Recognition入力: [batch, 3, height, width]
        dummy_input = torch.randn(1, 3, 128, 1024)  # 行画像（高さ128、幅可変）

        output_path = OUTPUT_DIR / "recognition" / "model.onnx"

        logger.info(f"ONNXエクスポート開始: {output_path}")

        # Dynamic axes for variable sequence length
        torch.onnx.export(
            model,
            dummy_input,
            str(output_path),
            input_names=['pixel_values'],
            output_names=['logits'],
            dynamic_axes={
                'pixel_values': {0: 'batch', 2: 'height', 3: 'width'},
                'logits': {0: 'batch', 1: 'sequence'}
            },
            opset_version=14,
            do_constant_folding=True,
            export_params=True,
        )

        file_size_mb = output_path.stat().st_size / (1024 * 1024)
        logger.info(f"Recognition Model エクスポート完了: {file_size_mb:.1f} MB")

        return str(output_path)

    except Exception as e:
        logger.error(f"Recognition Model エクスポート失敗: {e}")
        logger.exception("詳細:")
        raise


def quantize_model(input_path: str, output_suffix: str = "_int8"):
    """
    ONNXモデルをINT8動的量子化

    Args:
        input_path: 入力ONNXファイルパス
        output_suffix: 出力ファイル名のサフィックス
    """
    from onnxruntime.quantization import quantize_dynamic, QuantType

    input_path = Path(input_path)
    output_path = input_path.with_stem(input_path.stem + output_suffix)

    logger.info(f"量子化開始: {input_path}")

    start_time = time.time()

    quantize_dynamic(
        model_input=str(input_path),
        model_output=str(output_path),
        weight_type=QuantType.QUInt8,
    )

    elapsed = time.time() - start_time

    original_size = input_path.stat().st_size / (1024 * 1024)
    quantized_size = output_path.stat().st_size / (1024 * 1024)
    reduction = (1 - quantized_size / original_size) * 100

    logger.info(f"量子化完了: {elapsed:.2f}秒")
    logger.info(f"  元サイズ: {original_size:.1f} MB")
    logger.info(f"  量子化後: {quantized_size:.1f} MB")
    logger.info(f"  削減率: {reduction:.1f}%")

    return str(output_path)


def verify_onnx_model(onnx_path: str):
    """
    ONNXモデルの検証
    """
    import onnx
    import onnxruntime as ort
    import numpy as np

    logger.info(f"=== ONNX Model Verification: {onnx_path} ===")

    # 1. ONNX形式の検証
    logger.info("ONNX形式を検証中...")
    model = onnx.load(onnx_path)
    onnx.checker.check_model(model)
    logger.info("  ONNX形式: OK")

    # 2. 入出力情報
    logger.info("入出力情報:")
    for input in model.graph.input:
        shape = [dim.dim_value or dim.dim_param for dim in input.type.tensor_type.shape.dim]
        logger.info(f"  Input: {input.name} - {shape}")

    for output in model.graph.output:
        shape = [dim.dim_value or dim.dim_param for dim in output.type.tensor_type.shape.dim]
        logger.info(f"  Output: {output.name} - {shape}")

    # 3. ONNX Runtime 推論テスト
    logger.info("ONNX Runtime 推論テスト...")

    session = ort.InferenceSession(
        onnx_path,
        providers=['CPUExecutionProvider']
    )

    # ダミー入力で推論
    input_name = session.get_inputs()[0].name
    input_shape = session.get_inputs()[0].shape

    # 動的サイズを固定値に置換
    test_shape = []
    for dim in input_shape:
        if isinstance(dim, int):
            test_shape.append(dim)
        else:
            test_shape.append(1 if dim == 'batch' else 512)

    dummy_input = np.random.randn(*test_shape).astype(np.float32)

    start_time = time.time()
    outputs = session.run(None, {input_name: dummy_input})
    inference_time = time.time() - start_time

    logger.info(f"  推論時間: {inference_time * 1000:.1f} ms")
    logger.info(f"  出力形状: {[o.shape for o in outputs]}")
    logger.info("  推論テスト: OK")

    return True


def compare_pytorch_onnx(test_image_path: str = None):
    """
    PyTorchとONNXの出力を比較（精度検証）
    """
    import numpy as np
    from PIL import Image

    logger.info("=== PyTorch vs ONNX 精度比較 ===")

    if test_image_path is None:
        # テスト画像が指定されていない場合、ダミー画像を使用
        logger.info("ダミー画像で検証（実際のテスト画像推奨）")
        test_image = Image.new('RGB', (1024, 1024), color=(128, 128, 128))
    else:
        test_image = Image.open(test_image_path).convert('RGB')

    # TODO: 実装
    # 1. PyTorchモデルで推論
    # 2. ONNXモデルで推論
    # 3. 出力を比較（MSE, MAE等）

    logger.info("精度比較は未実装（テスト画像での検証を推奨）")


def main():
    parser = argparse.ArgumentParser(description="Surya OCR ONNX Export Tool")
    parser.add_argument("--export-detection", action="store_true", help="Detection Modelをエクスポート")
    parser.add_argument("--export-recognition", action="store_true", help="Recognition Modelをエクスポート")
    parser.add_argument("--export-all", action="store_true", help="全モデルをエクスポート")
    parser.add_argument("--quantize", action="store_true", help="エクスポート後にINT8量子化")
    parser.add_argument("--verify", action="store_true", help="エクスポートしたモデルを検証")
    parser.add_argument("--test-image", type=str, help="精度検証用テスト画像パス")

    args = parser.parse_args()

    ensure_output_dir()

    exported_models = []

    # エクスポート
    if args.export_all or args.export_detection:
        try:
            path = export_detection_model()
            exported_models.append(path)
        except Exception as e:
            logger.error(f"Detection Model エクスポート失敗: {e}")

    if args.export_all or args.export_recognition:
        try:
            path = export_recognition_model()
            exported_models.append(path)
        except Exception as e:
            logger.error(f"Recognition Model エクスポート失敗: {e}")

    # 量子化
    if args.quantize:
        # エクスポートされたモデルがある場合はそれを量子化
        # ない場合は既存のONNXファイルを探して量子化
        models_to_quantize = exported_models if exported_models else []

        if not models_to_quantize:
            # 既存のONNXファイルを検索（量子化済みは除外）
            existing_onnx = [
                str(f) for f in OUTPUT_DIR.rglob("*.onnx")
                if "_int8" not in f.stem
            ]
            models_to_quantize = existing_onnx
            if models_to_quantize:
                logger.info(f"既存ONNXファイルを量子化: {len(models_to_quantize)}件")

        for model_path in models_to_quantize:
            try:
                quantize_model(model_path)
            except Exception as e:
                logger.error(f"量子化失敗 ({model_path}): {e}")

    # 検証
    if args.verify:
        onnx_files = list(OUTPUT_DIR.rglob("*.onnx"))
        for onnx_file in onnx_files:
            try:
                verify_onnx_model(str(onnx_file))
            except Exception as e:
                logger.error(f"検証失敗 ({onnx_file}): {e}")

    # 精度比較
    if args.test_image:
        compare_pytorch_onnx(args.test_image)

    logger.info("完了")


if __name__ == "__main__":
    main()
