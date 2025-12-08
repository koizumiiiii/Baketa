#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Baketa OCR Server - Surya OCR Engine
Issue #189: 次世代OCRエンジン移行

Surya OCR を使用したgRPCサーバー実装。
90+言語対応、日本語・英語・中国語のゲームテキスト検出に対応。
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
    # proto未生成の場合のフォールバック
    ocr_pb2 = None
    ocr_pb2_grpc = None

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class SuryaOcrEngine:
    """Surya OCRエンジンラッパー (v0.17.0+ API対応)"""

    VERSION = "0.17.x"  # Surya OCRバージョン

    def __init__(self, device: str = "cuda"):
        self.device = device
        self.foundation_predictor = None
        self.recognition_predictor = None
        self.detection_predictor = None
        self.is_loaded = False

    def load(self) -> bool:
        """モデルをロード (Surya v0.17.0+ API)"""
        try:
            logger.info(f"Surya OCRモデルをロード中... (device: {self.device})")
            total_start = time.time()

            # Surya OCR v0.17.0+ APIのインポート
            import_start = time.time()
            from surya.foundation import FoundationPredictor
            from surya.recognition import RecognitionPredictor
            from surya.detection import DetectionPredictor
            logger.info(f"[Timing] Import完了: {time.time() - import_start:.2f}秒")

            # 環境変数でデバイス設定（Surya v0.17.0+）
            import os
            if self.device == "cuda":
                import torch
                if torch.cuda.is_available():
                    os.environ["TORCH_DEVICE"] = "cuda"
                    gpu_name = torch.cuda.get_device_name(0)
                    logger.info(f"CUDA利用可能: GPUモードで実行 ({gpu_name})")
                else:
                    os.environ["TORCH_DEVICE"] = "cpu"
                    logger.warning("CUDA利用不可: CPUモードにフォールバック")
                    self.device = "cpu"
            else:
                os.environ["TORCH_DEVICE"] = "cpu"

            # 検出モデル
            det_start = time.time()
            self.detection_predictor = DetectionPredictor()
            logger.info(f"[Timing] DetectionPredictor: {time.time() - det_start:.2f}秒")

            # 認識モデル (FoundationPredictor経由)
            found_start = time.time()
            self.foundation_predictor = FoundationPredictor()
            logger.info(f"[Timing] FoundationPredictor: {time.time() - found_start:.2f}秒")

            rec_start = time.time()
            self.recognition_predictor = RecognitionPredictor(self.foundation_predictor)
            logger.info(f"[Timing] RecognitionPredictor: {time.time() - rec_start:.2f}秒")

            elapsed = time.time() - total_start
            logger.info(f"Surya OCRモデルロード完了 (合計: {elapsed:.2f}秒)")
            self.is_loaded = True
            return True

        except ImportError as e:
            logger.error(f"Surya OCRライブラリが見つかりません: {e}")
            logger.error("pip install surya-ocr を実行してください")
            return False
        except Exception as e:
            logger.exception(f"モデルロードエラー: {e}")
            return False

    # 画像サイズ上限（10MB）- Decompression Bomb攻撃対策
    MAX_IMAGE_SIZE = 10 * 1024 * 1024

    # リサイズ設定 - 処理速度と精度のバランス
    MAX_IMAGE_DIMENSION = 2048  # 最長辺の最大ピクセル数

    def _resize_image_if_needed(self, image: "Image.Image") -> tuple["Image.Image", float]:
        """
        画像が大きすぎる場合はリサイズ（アスペクト比維持）

        Returns:
            tuple: (リサイズ後の画像, スケール係数)
            スケール係数は座標を元のサイズに戻すために使用
        """
        width, height = image.size
        max_dim = max(width, height)

        if max_dim <= self.MAX_IMAGE_DIMENSION:
            return image, 1.0

        scale = self.MAX_IMAGE_DIMENSION / max_dim
        new_width = int(width * scale)
        new_height = int(height * scale)

        logger.info(f"画像リサイズ: {width}x{height} → {new_width}x{new_height} (scale: {scale:.3f})")

        # LANCZOS: 高品質リサイズ（テキスト保持に最適）
        from PIL import Image
        resized = image.resize((new_width, new_height), Image.Resampling.LANCZOS)

        return resized, scale

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """画像からテキストを認識 (Surya v0.17.0+ API)"""
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        # 入力画像サイズ検証（セキュリティ対策）
        if len(image_bytes) > self.MAX_IMAGE_SIZE:
            raise ValueError(f"画像サイズが上限を超えています: {len(image_bytes)} bytes (上限: {self.MAX_IMAGE_SIZE} bytes)")

        try:
            from PIL import Image

            # バイトデータから画像を読み込み
            image = Image.open(io.BytesIO(image_bytes))
            original_size = image.size

            # RGB変換（必要な場合）
            if image.mode != "RGB":
                image = image.convert("RGB")

            # 大きな画像はリサイズして処理速度を向上
            image, scale = self._resize_image_if_needed(image)

            # 言語指定（v0.17.0では環境変数またはRecognitionPredictorの設定で制御）
            # Note: Surya v0.17.0では言語は自動検出またはデフォルト設定を使用
            if languages:
                logger.info(f"言語指定: {languages} (Surya v0.17.0では自動検出を使用)")

            logger.info(f"OCR実行中... (サイズ: {image.size})")
            start_time = time.time()

            # Surya OCR v0.17.0+ API: RecognitionPredictor + DetectionPredictor
            predictions = self.recognition_predictor(
                [image],
                det_predictor=self.detection_predictor
            )

            elapsed = time.time() - start_time

            # 結果を整形
            regions = []
            if predictions and len(predictions) > 0:
                ocr_result = predictions[0]  # 最初の画像の結果

                # v0.17.0 API: text_linesプロパティを使用
                text_lines = getattr(ocr_result, 'text_lines', [])
                if not text_lines:
                    # 代替アクセス方法
                    text_lines = getattr(ocr_result, 'lines', [])

                for idx, line in enumerate(text_lines):
                    # BoundingBox取得（v0.17.0 API対応）
                    bbox = getattr(line, 'bbox', None)
                    polygon = getattr(line, 'polygon', None)
                    confidence = getattr(line, 'confidence', 0.0)
                    text = getattr(line, 'text', '')

                    # リサイズした場合は座標を元のスケールに戻す
                    inv_scale = 1.0 / scale if scale != 1.0 else 1.0

                    region = {
                        "text": text,
                        "confidence": float(confidence) if confidence else 0.0,
                        "bbox": {
                            "points": [
                                {"x": float(p[0]) * inv_scale, "y": float(p[1]) * inv_scale}
                                for p in polygon
                            ] if polygon else [],
                            "x": int(bbox[0] * inv_scale) if bbox else 0,
                            "y": int(bbox[1] * inv_scale) if bbox else 0,
                            "width": int((bbox[2] - bbox[0]) * inv_scale) if bbox and len(bbox) >= 4 else 0,
                            "height": int((bbox[3] - bbox[1]) * inv_scale) if bbox and len(bbox) >= 4 else 0,
                        },
                        "line_index": idx
                    }
                    regions.append(region)

            logger.info(f"OCR完了: {len(regions)}領域検出 ({elapsed*1000:.0f}ms)")

            return {
                "success": True,
                "regions": regions,
                "processing_time_ms": int(elapsed * 1000),
                "engine_name": "Surya OCR",
                "engine_version": self.VERSION
            }

        except Exception as e:
            logger.exception(f"OCRエラー: {e}")
            return {
                "success": False,
                "error": str(e),
                "regions": [],
                "processing_time_ms": 0,
                "engine_name": "Surya OCR",
                "engine_version": self.VERSION
            }


class OcrServiceServicer(ocr_pb2_grpc.OcrServiceServicer if ocr_pb2_grpc else object):
    """gRPC OCRサービス実装"""

    def __init__(self, engine: SuryaOcrEngine):
        self.engine = engine

    def Recognize(self, request, context):
        """OCR認識を実行"""
        start_time = time.time()

        try:
            # 言語リスト取得
            languages = list(request.languages) if request.languages else None

            # OCR実行
            result = self.engine.recognize(request.image_data, languages)

            # レスポンス構築
            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = result["success"]
            response.processing_time_ms = result["processing_time_ms"]
            response.engine_name = result["engine_name"]
            response.engine_version = result["engine_version"]
            response.region_count = len(result["regions"])

            # テキスト領域を追加
            for region_data in result["regions"]:
                region = response.regions.add()
                region.text = region_data["text"]
                region.confidence = region_data["confidence"]
                region.line_index = region_data["line_index"]

                # BoundingBox
                bbox = region_data["bbox"]
                region.bounding_box.x = bbox["x"]
                region.bounding_box.y = bbox["y"]
                region.bounding_box.width = bbox["width"]
                region.bounding_box.height = bbox["height"]

                for point in bbox["points"]:
                    p = region.bounding_box.points.add()
                    p.x = point["x"]
                    p.y = point["y"]

            # タイムスタンプ
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
        response.details["engine"] = "Surya OCR"
        response.details["loaded"] = str(self.engine.is_loaded)
        response.timestamp.FromDatetime(datetime.utcnow())
        return response

    def IsReady(self, request, context):
        """準備状態確認"""
        response = ocr_pb2.OcrIsReadyResponse()
        response.is_ready = self.engine.is_loaded
        response.status = "ready" if self.engine.is_loaded else "loading"
        response.details["engine"] = "Surya OCR"
        response.details["version"] = self.engine.VERSION
        response.timestamp.FromDatetime(datetime.utcnow())
        return response


def serve(port: int = 50052, device: str = "cuda"):
    """gRPCサーバーを起動"""

    # エンジン初期化
    engine = SuryaOcrEngine(device=device)
    if not engine.load():
        logger.error("エンジンロード失敗")
        return

    # gRPCサーバー起動
    # max_workers=1: GPU処理の競合を避けるためシングルワーカーに制限
    # max_message_length: 50MB - 高解像度ゲームスクリーンショット対応
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

    logger.info(f"Surya OCR gRPCサーバー起動 (port: {port})")

    try:
        server.wait_for_termination()
    except KeyboardInterrupt:
        logger.info("サーバー停止中...")
        server.stop(5)


def main():
    """エントリーポイント"""
    parser = argparse.ArgumentParser(description="Surya OCR gRPC Server")
    parser.add_argument("--port", type=int, default=50052, help="gRPCポート番号")
    parser.add_argument("--device", type=str, default="cuda",
                        choices=["cuda", "cpu"], help="実行デバイス")
    parser.add_argument("--test", action="store_true", help="テストモード")

    args = parser.parse_args()

    if args.test:
        # テストモード: モデルロードのみ
        engine = SuryaOcrEngine(device=args.device)
        if engine.load():
            logger.info("テスト成功: モデルロード完了")

            # サンプル画像でテスト（あれば）
            test_image_path = "test_images/chrono_trigger.png"
            if os.path.exists(test_image_path):
                with open(test_image_path, "rb") as f:
                    image_bytes = f.read()
                result = engine.recognize(image_bytes, ["ja", "en"])
                logger.info(f"テスト結果: {result}")
        else:
            logger.error("テスト失敗: モデルロードエラー")
            sys.exit(1)
    else:
        serve(port=args.port, device=args.device)


if __name__ == "__main__":
    main()
