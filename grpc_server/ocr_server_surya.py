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
    """Surya OCRエンジンラッパー"""

    VERSION = "0.6.x"  # Surya OCRバージョン

    def __init__(self, device: str = "cuda"):
        self.device = device
        self.det_model = None
        self.det_processor = None
        self.rec_model = None
        self.rec_processor = None
        self.is_loaded = False

    def load(self) -> bool:
        """モデルをロード"""
        try:
            logger.info(f"Surya OCRモデルをロード中... (device: {self.device})")
            start_time = time.time()

            # Surya OCRのインポート
            from surya.model.detection.model import load_model as load_det_model
            from surya.model.detection.model import load_processor as load_det_processor
            from surya.model.recognition.model import load_model as load_rec_model
            from surya.model.recognition.processor import load_processor as load_rec_processor

            # 検出モデル
            self.det_model = load_det_model()
            self.det_processor = load_det_processor()

            # 認識モデル
            self.rec_model = load_rec_model()
            self.rec_processor = load_rec_processor()

            # デバイス移動
            if self.device == "cuda":
                import torch
                if torch.cuda.is_available():
                    self.det_model = self.det_model.to("cuda")
                    self.rec_model = self.rec_model.to("cuda")
                    logger.info("CUDA利用可能: GPUモードで実行")
                else:
                    logger.warning("CUDA利用不可: CPUモードにフォールバック")
                    self.device = "cpu"

            elapsed = time.time() - start_time
            logger.info(f"Surya OCRモデルロード完了 ({elapsed:.2f}秒)")
            self.is_loaded = True
            return True

        except ImportError as e:
            logger.error(f"Surya OCRライブラリが見つかりません: {e}")
            logger.error("pip install surya-ocr を実行してください")
            return False
        except Exception as e:
            logger.error(f"モデルロードエラー: {e}")
            return False

    # 画像サイズ上限（10MB）- Decompression Bomb攻撃対策
    MAX_IMAGE_SIZE = 10 * 1024 * 1024

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """画像からテキストを認識"""
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        # 入力画像サイズ検証（セキュリティ対策）
        if len(image_bytes) > self.MAX_IMAGE_SIZE:
            raise ValueError(f"画像サイズが上限を超えています: {len(image_bytes)} bytes (上限: {self.MAX_IMAGE_SIZE} bytes)")

        try:
            from PIL import Image
            from surya.ocr import run_ocr

            # バイトデータから画像を読み込み
            image = Image.open(io.BytesIO(image_bytes))

            # RGB変換（必要な場合）
            if image.mode != "RGB":
                image = image.convert("RGB")

            # 言語指定（デフォルトは日本語+英語）
            if not languages:
                languages = ["ja", "en"]

            logger.info(f"OCR実行中... (言語: {languages}, サイズ: {image.size})")
            start_time = time.time()

            # Surya OCR実行
            results = run_ocr(
                [image],
                [languages],
                self.det_model,
                self.det_processor,
                self.rec_model,
                self.rec_processor
            )

            elapsed = time.time() - start_time

            # 結果を整形
            regions = []
            if results and len(results) > 0:
                ocr_result = results[0]  # 最初の画像の結果

                for idx, line in enumerate(ocr_result.text_lines):
                    region = {
                        "text": line.text,
                        "confidence": line.confidence,
                        "bbox": {
                            "points": [
                                {"x": p[0], "y": p[1]}
                                for p in line.polygon
                            ] if hasattr(line, 'polygon') else [],
                            "x": int(line.bbox[0]) if hasattr(line, 'bbox') else 0,
                            "y": int(line.bbox[1]) if hasattr(line, 'bbox') else 0,
                            "width": int(line.bbox[2] - line.bbox[0]) if hasattr(line, 'bbox') else 0,
                            "height": int(line.bbox[3] - line.bbox[1]) if hasattr(line, 'bbox') else 0,
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
            logger.error(f"OCRエラー: {e}")
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
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=1))

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
