#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
ONNX Detection Engine for Surya OCR Hybrid Mode
Issue #197: ハイブリッド構成 - ONNX Detection + PyTorch Recognition

Detection Model (Line Detector) をONNX Runtimeで実行し、
セグメンテーションマスクからテキスト行のBoundingBoxを抽出します。
"""
import sys
import time
import logging
from pathlib import Path
from dataclasses import dataclass
from typing import List, Tuple, Optional

import numpy as np
from PIL import Image

if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

logger = logging.getLogger(__name__)


@dataclass
class DetectionResult:
    """検出結果データクラス"""
    bbox: Tuple[int, int, int, int]  # (x1, y1, x2, y2)
    polygon: List[Tuple[float, float]]  # [(x1,y1), (x2,y2), (x3,y3), (x4,y4)]
    confidence: float


class OnnxDetectionEngine:
    """
    ONNX Runtime を使用したテキスト行検出エンジン

    Surya Detection Model (segformer) をONNX形式で推論し、
    セグメンテーションマスクからテキスト行のBoundingBoxを抽出します。

    メリット:
    - PyTorchモデル（153MB）→ ONNX INT8（39MB）で74%削減
    - GPU不要でもCPUで高速推論可能
    - GitHub Releasesで配布可能
    """

    # デフォルトモデルパス
    DEFAULT_MODEL_PATH = Path(__file__).parent.parent.parent / "Models" / "surya-onnx" / "detection" / "model_int8.onnx"

    # Surya Detection のデフォルト入力サイズ
    INPUT_SIZE = 1024

    # セグメンテーションマスクの閾値
    DETECTION_THRESHOLD = 0.5

    # 最小BoundingBoxサイズ（ノイズ除去用）
    MIN_BBOX_SIZE = 10

    def __init__(self, model_path: Optional[Path] = None, use_gpu: bool = False):
        """
        Args:
            model_path: ONNXモデルファイルパス（Noneの場合はデフォルトパス）
            use_gpu: GPUを使用するかどうか
        """
        self.model_path = model_path or self.DEFAULT_MODEL_PATH
        self.use_gpu = use_gpu
        self.session = None
        self.is_loaded = False

    def load(self) -> bool:
        """ONNXモデルをロード"""
        try:
            import onnxruntime as ort

            if not self.model_path.exists():
                logger.error(f"Detection ONNXモデルが見つかりません: {self.model_path}")
                return False

            logger.info(f"ONNX Detection Modelをロード中: {self.model_path}")
            start_time = time.time()

            # プロバイダー選択
            providers = ['CUDAExecutionProvider', 'CPUExecutionProvider'] if self.use_gpu else ['CPUExecutionProvider']

            self.session = ort.InferenceSession(
                str(self.model_path),
                providers=providers
            )

            # 入出力情報をログ
            input_info = self.session.get_inputs()[0]
            output_info = self.session.get_outputs()[0]
            logger.info(f"  Input: {input_info.name} - {input_info.shape}")
            logger.info(f"  Output: {output_info.name} - {output_info.shape}")

            elapsed = time.time() - start_time
            logger.info(f"ONNX Detection Model ロード完了: {elapsed:.2f}秒")

            self.is_loaded = True
            return True

        except Exception as e:
            logger.error(f"ONNX Detection Model ロード失敗: {e}")
            return False

    def preprocess(self, image: Image.Image) -> Tuple[np.ndarray, float]:
        """
        画像を前処理

        Args:
            image: PIL Image (RGB)

        Returns:
            (preprocessed_array, scale_factor)
        """
        original_size = image.size  # (width, height)

        # リサイズ（アスペクト比維持、最長辺をINPUT_SIZEに）
        width, height = original_size
        max_dim = max(width, height)
        scale = self.INPUT_SIZE / max_dim

        new_width = int(width * scale)
        new_height = int(height * scale)

        # パディングして正方形に
        resized = image.resize((new_width, new_height), Image.Resampling.LANCZOS)

        # 正方形キャンバスにパディング
        padded = Image.new('RGB', (self.INPUT_SIZE, self.INPUT_SIZE), (0, 0, 0))
        padded.paste(resized, (0, 0))

        # NumPy配列に変換 [H, W, C] -> [C, H, W]
        img_array = np.array(padded, dtype=np.float32)
        img_array = img_array.transpose(2, 0, 1)

        # 正規化 (ImageNet標準)
        mean = np.array([0.485, 0.456, 0.406], dtype=np.float32).reshape(3, 1, 1)
        std = np.array([0.229, 0.224, 0.225], dtype=np.float32).reshape(3, 1, 1)
        img_array = (img_array / 255.0 - mean) / std

        # バッチ次元追加 [1, C, H, W]
        img_array = np.expand_dims(img_array, axis=0)

        return img_array, scale

    def postprocess(
        self,
        segmentation_mask: np.ndarray,
        scale: float,
        original_size: Tuple[int, int]
    ) -> List[DetectionResult]:
        """
        セグメンテーションマスクからBoundingBoxを抽出

        Args:
            segmentation_mask: [batch, classes, H/4, W/4] の出力
            scale: 前処理で適用したスケール係数
            original_size: 元画像サイズ (width, height)

        Returns:
            DetectionResultのリスト
        """
        import cv2

        # [1, classes, H, W] -> [H, W] (クラス0がテキスト領域と仮定)
        mask = segmentation_mask[0, 0]  # 最初のクラス

        # Sigmoid適用（モデル出力がlogitsの場合）
        mask = 1 / (1 + np.exp(-mask))

        # 閾値処理
        binary_mask = (mask > self.DETECTION_THRESHOLD).astype(np.uint8) * 255

        # マスクサイズ（出力は入力の1/4）
        mask_scale = self.INPUT_SIZE / 4 / self.INPUT_SIZE  # = 0.25

        # 元画像座標への変換係数
        inv_scale = 1.0 / scale * 4  # mask→original

        # 輪郭検出
        contours, _ = cv2.findContours(binary_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        results = []
        original_width, original_height = original_size

        for contour in contours:
            # 最小回転矩形
            rect = cv2.minAreaRect(contour)
            box = cv2.boxPoints(rect)
            box = np.intp(box)

            # BoundingBox（軸平行）
            x1 = max(0, int(np.min(box[:, 0]) * inv_scale))
            y1 = max(0, int(np.min(box[:, 1]) * inv_scale))
            x2 = min(original_width, int(np.max(box[:, 0]) * inv_scale))
            y2 = min(original_height, int(np.max(box[:, 1]) * inv_scale))

            # 小さすぎるBBoxは除外
            if (x2 - x1) < self.MIN_BBOX_SIZE or (y2 - y1) < self.MIN_BBOX_SIZE:
                continue

            # ポリゴン座標
            polygon = [(float(p[0] * inv_scale), float(p[1] * inv_scale)) for p in box]

            # 信頼度（マスク領域の平均値）
            mask_region = mask[
                max(0, int(rect[0][1] - rect[1][1]/2)):int(rect[0][1] + rect[1][1]/2),
                max(0, int(rect[0][0] - rect[1][0]/2)):int(rect[0][0] + rect[1][0]/2)
            ]
            confidence = float(np.mean(mask_region)) if mask_region.size > 0 else 0.5

            results.append(DetectionResult(
                bbox=(x1, y1, x2, y2),
                polygon=polygon,
                confidence=confidence
            ))

        # Y座標でソート（上から下へ）
        results.sort(key=lambda r: r.bbox[1])

        logger.debug(f"検出結果: {len(results)}領域")
        return results

    def detect(self, image: Image.Image) -> List[DetectionResult]:
        """
        画像からテキスト行を検出

        Args:
            image: PIL Image (RGB)

        Returns:
            DetectionResultのリスト
        """
        if not self.is_loaded:
            raise RuntimeError("ONNXモデルが未ロードです")

        start_time = time.time()

        # 前処理
        input_array, scale = self.preprocess(image)

        # 推論
        input_name = self.session.get_inputs()[0].name
        outputs = self.session.run(None, {input_name: input_array})
        segmentation_mask = outputs[0]

        # 後処理
        results = self.postprocess(segmentation_mask, scale, image.size)

        elapsed = time.time() - start_time
        logger.info(f"ONNX Detection完了: {len(results)}領域検出 ({elapsed*1000:.0f}ms)")

        return results


def test_onnx_detection():
    """テスト実行"""
    print("=== ONNX Detection Engine Test ===")

    engine = OnnxDetectionEngine()
    if not engine.load():
        print("モデルロード失敗")
        return False

    # テスト画像
    test_image_path = Path(__file__).parent.parent / "test_images" / "chrono_trigger.png"

    if test_image_path.exists():
        print(f"テスト画像: {test_image_path}")
        image = Image.open(test_image_path).convert('RGB')
    else:
        print("テスト画像なし - ダミー画像使用")
        image = Image.new('RGB', (800, 600), color=(200, 200, 200))

    results = engine.detect(image)

    print(f"\n検出結果: {len(results)}領域")
    for i, r in enumerate(results[:5]):  # 最初の5件
        print(f"  [{i}] bbox={r.bbox}, conf={r.confidence:.2f}")

    print("\n✅ テスト完了")
    return True


if __name__ == "__main__":
    test_onnx_detection()
