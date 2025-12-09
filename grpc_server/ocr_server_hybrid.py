#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Baketa OCR Server - Hybrid Mode (ONNX Detection + PyTorch Recognition)
Issue #197: ハイブリッド構成によるOCR最適化

構成:
- Detection: ONNX Runtime (model_int8.onnx, 39MB) - GitHub Releasesで配布可能
- Recognition: PyTorch/Surya (HuggingFaceからダウンロード)

メリット:
- Detection: モデルサイズ74%削減 (153MB → 39MB)
- Detection: GPU不要でも高速推論
- Recognition: 複雑なVLMモデルの精度を維持
"""

import sys
import os
import io
import time
import logging
import argparse
from concurrent import futures
from datetime import datetime
from typing import Optional, List
from pathlib import Path

import grpc
from google.protobuf.timestamp_pb2 import Timestamp

# UTF-8エンコーディング強制（Windows対応）
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

# gRPC生成コードのインポート
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from protos import ocr_pb2, ocr_pb2_grpc
except ImportError:
    ocr_pb2 = None
    ocr_pb2_grpc = None

# ONNX Detection Engine
from engines.onnx_detection import OnnxDetectionEngine, DetectionResult

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class HybridOcrEngine:
    """
    ハイブリッドOCRエンジン

    Detection: ONNX Runtime (軽量・高速)
    Recognition: PyTorch/Surya (高精度VLM)
    """

    VERSION = "0.17.x-hybrid"

    def __init__(self, device: str = "cuda", onnx_model_path: Optional[Path] = None):
        self.device = device
        self.onnx_model_path = onnx_model_path

        # Detection (ONNX)
        self.detection_engine: Optional[OnnxDetectionEngine] = None

        # Recognition (PyTorch)
        self.foundation_predictor = None
        self.recognition_predictor = None

        self.is_loaded = False

    def load(self) -> bool:
        """モデルをロード"""
        try:
            logger.info(f"=== Hybrid OCR Engine Loading ===")
            logger.info(f"Detection: ONNX Runtime")
            logger.info(f"Recognition: PyTorch (device: {self.device})")
            total_start = time.time()

            # 1. Detection Model (ONNX) - 高速ロード
            logger.info("[1/3] Loading ONNX Detection Model...")
            det_start = time.time()

            self.detection_engine = OnnxDetectionEngine(
                model_path=self.onnx_model_path,
                use_gpu=False  # Detection は CPU で十分高速
            )

            if not self.detection_engine.load():
                logger.error("ONNX Detection Model ロード失敗")
                return False

            logger.info(f"[Timing] ONNX Detection: {time.time() - det_start:.2f}秒")

            # 2. Recognition Model (PyTorch) - 初回はHuggingFaceダウンロード
            logger.info("[2/3] Loading PyTorch Recognition Model...")

            # 環境変数でデバイス設定
            if self.device == "cuda":
                import torch
                if torch.cuda.is_available():
                    os.environ["TORCH_DEVICE"] = "cuda"
                    gpu_name = torch.cuda.get_device_name(0)
                    logger.info(f"CUDA利用可能: {gpu_name}")
                else:
                    os.environ["TORCH_DEVICE"] = "cpu"
                    logger.warning("CUDA利用不可: CPUモードにフォールバック")
                    self.device = "cpu"
            else:
                os.environ["TORCH_DEVICE"] = "cpu"

            from surya.foundation import FoundationPredictor
            from surya.recognition import RecognitionPredictor

            found_start = time.time()
            self.foundation_predictor = FoundationPredictor()
            logger.info(f"[Timing] FoundationPredictor: {time.time() - found_start:.2f}秒")

            rec_start = time.time()
            self.recognition_predictor = RecognitionPredictor(self.foundation_predictor)
            logger.info(f"[Timing] RecognitionPredictor: {time.time() - rec_start:.2f}秒")

            elapsed = time.time() - total_start
            logger.info(f"[3/3] Hybrid OCR Engine ロード完了 (合計: {elapsed:.2f}秒)")

            self.is_loaded = True
            return True

        except ImportError as e:
            logger.error(f"必要なライブラリが見つかりません: {e}")
            return False
        except Exception as e:
            logger.exception(f"モデルロードエラー: {e}")
            return False

    # 画像サイズ上限（10MB）- Decompression Bomb攻撃対策
    MAX_IMAGE_SIZE = 10 * 1024 * 1024

    # リサイズ設定
    MAX_IMAGE_DIMENSION = 2048

    def _resize_image_if_needed(self, image: "Image.Image") -> tuple["Image.Image", float]:
        """画像が大きすぎる場合はリサイズ（アスペクト比維持）"""
        from PIL import Image

        width, height = image.size
        max_dim = max(width, height)

        if max_dim <= self.MAX_IMAGE_DIMENSION:
            return image, 1.0

        scale = self.MAX_IMAGE_DIMENSION / max_dim
        new_width = int(width * scale)
        new_height = int(height * scale)

        logger.info(f"画像リサイズ: {width}x{height} → {new_width}x{new_height}")
        resized = image.resize((new_width, new_height), Image.Resampling.LANCZOS)

        return resized, scale

    def _crop_line_images(
        self,
        image: "Image.Image",
        detections: List[DetectionResult]
    ) -> List["Image.Image"]:
        """検出結果に基づいて行画像をクロップ"""
        from PIL import Image

        line_images = []
        for det in detections:
            x1, y1, x2, y2 = det.bbox
            # パディングを追加（認識精度向上）
            padding = 5
            x1 = max(0, x1 - padding)
            y1 = max(0, y1 - padding)
            x2 = min(image.width, x2 + padding)
            y2 = min(image.height, y2 + padding)

            line_img = image.crop((x1, y1, x2, y2))
            line_images.append(line_img)

        return line_images

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """
        ハイブリッドOCR実行

        1. ONNX Detection でテキスト行を検出
        2. 検出領域をクロップ
        3. PyTorch Recognition でテキスト認識
        """
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        # 入力検証
        if len(image_bytes) > self.MAX_IMAGE_SIZE:
            raise ValueError(f"画像サイズが上限を超えています: {len(image_bytes)} bytes")

        try:
            from PIL import Image

            # 画像読み込み
            image = Image.open(io.BytesIO(image_bytes))
            original_size = image.size

            if image.mode != "RGB":
                image = image.convert("RGB")

            # リサイズ
            image, scale = self._resize_image_if_needed(image)

            logger.info(f"Hybrid OCR実行中... (サイズ: {image.size})")
            start_time = time.time()

            # Phase 1: ONNX Detection
            det_start = time.time()
            detections = self.detection_engine.detect(image)
            det_time = time.time() - det_start
            logger.info(f"[Detection] {len(detections)}領域検出 ({det_time*1000:.0f}ms)")

            if not detections:
                logger.info("テキスト領域が検出されませんでした")
                return {
                    "success": True,
                    "regions": [],
                    "processing_time_ms": int((time.time() - start_time) * 1000),
                    "engine_name": "Surya OCR (Hybrid)",
                    "engine_version": self.VERSION
                }

            # Phase 2: PyTorch Recognition
            # 方法1: 全体画像をRecognitionPredictorに渡す（内部でDetectionを使わない）
            # 方法2: クロップした行画像を個別に認識

            # 方法1を採用（Suryaの内部処理を活用）
            # ただし、Detectionは既にONNXで実行済みなので、
            # RecognitionPredictorを直接使用
            rec_start = time.time()

            # RecognitionPredictorはdet_predictorなしで呼ぶと認識のみ実行
            # ただしSurya v0.17.0ではdet_predictorが必要な場合がある
            # そのため、検出結果を元にクロップして認識を実行

            line_images = self._crop_line_images(image, detections)

            # クロップ画像を認識
            # Note: RecognitionPredictorに複数画像を渡すとバッチ処理される
            try:
                predictions = self.recognition_predictor(
                    line_images,
                    det_predictor=None  # 検出済みなのでDetectionは不要
                )
            except TypeError:
                # det_predictor引数がない場合のフォールバック
                predictions = self.recognition_predictor(line_images)

            rec_time = time.time() - rec_start
            logger.info(f"[Recognition] {len(line_images)}行認識 ({rec_time*1000:.0f}ms)")

            # 結果を整形
            regions = []
            inv_scale = 1.0 / scale if scale != 1.0 else 1.0

            for idx, (det, pred) in enumerate(zip(detections, predictions)):
                # Detection結果からBBox
                x1, y1, x2, y2 = det.bbox

                # Recognition結果からテキスト
                text_lines = getattr(pred, 'text_lines', [])
                if not text_lines:
                    text_lines = getattr(pred, 'lines', [])

                # 行のテキストを結合
                text = ""
                confidence = det.confidence
                if text_lines:
                    line = text_lines[0]  # 最初の行
                    text = getattr(line, 'text', '')
                    line_conf = getattr(line, 'confidence', None)
                    if line_conf is not None:
                        confidence = float(line_conf)

                region = {
                    "text": text,
                    "confidence": confidence,
                    "bbox": {
                        "points": [
                            {"x": float(p[0]) * inv_scale, "y": float(p[1]) * inv_scale}
                            for p in det.polygon
                        ],
                        "x": int(x1 * inv_scale),
                        "y": int(y1 * inv_scale),
                        "width": int((x2 - x1) * inv_scale),
                        "height": int((y2 - y1) * inv_scale),
                    },
                    "line_index": idx
                }
                regions.append(region)

            elapsed = time.time() - start_time
            logger.info(f"Hybrid OCR完了: {len(regions)}領域 ({elapsed*1000:.0f}ms)")

            return {
                "success": True,
                "regions": regions,
                "processing_time_ms": int(elapsed * 1000),
                "engine_name": "Surya OCR (Hybrid)",
                "engine_version": self.VERSION,
                "detection_time_ms": int(det_time * 1000),
                "recognition_time_ms": int(rec_time * 1000)
            }

        except Exception as e:
            logger.exception(f"OCRエラー: {e}")
            return {
                "success": False,
                "error": str(e),
                "regions": [],
                "processing_time_ms": 0,
                "engine_name": "Surya OCR (Hybrid)",
                "engine_version": self.VERSION
            }


class OcrServiceServicer(ocr_pb2_grpc.OcrServiceServicer if ocr_pb2_grpc else object):
    """gRPC OCRサービス実装"""

    def __init__(self, engine: HybridOcrEngine):
        self.engine = engine

    def Recognize(self, request, context):
        """OCR認識を実行"""
        try:
            languages = list(request.languages) if request.languages else None
            result = self.engine.recognize(request.image_data, languages)

            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = result["success"]
            response.processing_time_ms = result["processing_time_ms"]
            response.engine_name = result["engine_name"]
            response.engine_version = result["engine_version"]
            response.region_count = len(result["regions"])

            for region_data in result["regions"]:
                region = response.regions.add()
                region.text = region_data["text"]
                region.confidence = region_data["confidence"]
                region.line_index = region_data["line_index"]

                bbox = region_data["bbox"]
                region.bounding_box.x = bbox["x"]
                region.bounding_box.y = bbox["y"]
                region.bounding_box.width = bbox["width"]
                region.bounding_box.height = bbox["height"]

                for point in bbox["points"]:
                    p = region.bounding_box.points.add()
                    p.x = point["x"]
                    p.y = point["y"]

            response.timestamp.FromDatetime(datetime.utcnow())
            return response

        except Exception as e:
            logger.error(f"Recognize error: {e}")
            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = False
            response.error.error_type = ocr_pb2.OCR_ERROR_TYPE_PROCESSING_ERROR
            response.error.message = str(e)
            response.timestamp.FromDatetime(datetime.utcnow())
            return response

    def HealthCheck(self, request, context):
        """ヘルスチェック"""
        response = ocr_pb2.OcrHealthCheckResponse()
        response.is_healthy = self.engine.is_loaded
        response.status = "healthy" if self.engine.is_loaded else "unhealthy"
        response.details["engine"] = "Surya OCR (Hybrid)"
        response.details["mode"] = "ONNX Detection + PyTorch Recognition"
        response.details["loaded"] = str(self.engine.is_loaded)
        response.timestamp.FromDatetime(datetime.utcnow())
        return response

    def IsReady(self, request, context):
        """準備状態確認"""
        response = ocr_pb2.OcrIsReadyResponse()
        response.is_ready = self.engine.is_loaded
        response.status = "ready" if self.engine.is_loaded else "loading"
        response.details["engine"] = "Surya OCR (Hybrid)"
        response.details["version"] = self.engine.VERSION
        response.timestamp.FromDatetime(datetime.utcnow())
        return response


def serve(port: int = 50052, device: str = "cuda", onnx_model_path: Optional[str] = None):
    """gRPCサーバーを起動"""

    # エンジン初期化
    model_path = Path(onnx_model_path) if onnx_model_path else None
    engine = HybridOcrEngine(device=device, onnx_model_path=model_path)

    if not engine.load():
        logger.error("エンジンロード失敗")
        return

    # gRPCサーバー起動
    MAX_MESSAGE_LENGTH = 50 * 1024 * 1024  # 50MB
    server = grpc.server(
        futures.ThreadPoolExecutor(max_workers=1),
        options=[
            ('grpc.max_receive_message_length', MAX_MESSAGE_LENGTH),
            ('grpc.max_send_message_length', MAX_MESSAGE_LENGTH),
        ]
    )

    if ocr_pb2_grpc:
        ocr_pb2_grpc.add_OcrServiceServicer_to_server(
            OcrServiceServicer(engine), server
        )

    server.add_insecure_port(f'[::]:{port}')
    server.start()

    logger.info(f"Hybrid OCR gRPCサーバー起動 (port: {port})")
    logger.info(f"  Mode: ONNX Detection + PyTorch Recognition")

    try:
        server.wait_for_termination()
    except KeyboardInterrupt:
        logger.info("サーバー停止中...")
        server.stop(5)


def main():
    """エントリーポイント"""
    parser = argparse.ArgumentParser(description="Surya OCR Hybrid gRPC Server")
    parser.add_argument("--port", type=int, default=50052, help="gRPCポート番号")
    parser.add_argument("--device", type=str, default="cuda",
                        choices=["cuda", "cpu"], help="Recognition実行デバイス")
    parser.add_argument("--onnx-model", type=str, help="Detection ONNXモデルパス")
    parser.add_argument("--test", action="store_true", help="テストモード")

    args = parser.parse_args()

    if args.test:
        # テストモード
        model_path = Path(args.onnx_model) if args.onnx_model else None
        engine = HybridOcrEngine(device=args.device, onnx_model_path=model_path)

        if engine.load():
            logger.info("テスト成功: モデルロード完了")

            # サンプル画像でテスト
            test_image_path = Path(__file__).parent / "test_images" / "chrono_trigger.png"
            if test_image_path.exists():
                with open(test_image_path, "rb") as f:
                    image_bytes = f.read()
                result = engine.recognize(image_bytes, ["ja", "en"])
                logger.info(f"テスト結果: {len(result['regions'])}領域検出")
                for r in result['regions'][:3]:
                    logger.info(f"  - {r['text'][:30]}...")
            else:
                logger.info("テスト画像なし - モデルロードのみ確認")
        else:
            logger.error("テスト失敗: モデルロードエラー")
            sys.exit(1)
    else:
        serve(port=args.port, device=args.device, onnx_model_path=args.onnx_model)


if __name__ == "__main__":
    main()
