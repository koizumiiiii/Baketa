#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Baketa OCR Server - PaddleOCR-VL Engine
Issue #189: 次世代OCRエンジン移行

PaddleOCR-VL (Vision-Language Model) を使用したgRPCサーバー実装。
0.9Bパラメータ、109言語対応。
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
    ocr_pb2 = None
    ocr_pb2_grpc = None

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class PaddleVLOcrEngine:
    """PaddleOCR-VL エンジンラッパー"""

    VERSION = "2.8.x"  # PaddleOCR バージョン

    def __init__(self, device: str = "cuda", use_angle_cls: bool = True):
        self.device = device
        self.use_angle_cls = use_angle_cls
        self.ocr = None
        self.is_loaded = False

    def load(self) -> bool:
        """モデルをロード"""
        try:
            logger.info(f"PaddleOCR-VL モデルをロード中... (device: {self.device})")
            start_time = time.time()

            from paddleocr import PaddleOCR

            # GPU使用設定
            use_gpu = self.device == "cuda"

            # PaddleOCR-VL初期化
            # PP-OCR-VLはPaddleOCRの最新バージョンに含まれる
            self.ocr = PaddleOCR(
                use_angle_cls=self.use_angle_cls,
                lang="japan",  # 日本語モデル（多言語対応）
                use_gpu=use_gpu,
                show_log=False,
                # VLモデル設定
                ocr_version="PP-OCRv4",  # 最新バージョン
                structure_version="PP-StructureV2",
                # 高精度設定
                det_db_thresh=0.3,
                det_db_box_thresh=0.5,
                rec_batch_num=6,
            )

            elapsed = time.time() - start_time
            logger.info(f"PaddleOCR-VL モデルロード完了 ({elapsed:.2f}秒)")
            self.is_loaded = True
            return True

        except ImportError as e:
            logger.error(f"PaddleOCRライブラリが見つかりません: {e}")
            logger.error("pip install paddlepaddle paddleocr を実行してください")
            return False
        except Exception as e:
            logger.error(f"モデルロードエラー: {e}")
            import traceback
            traceback.print_exc()
            return False

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """画像からテキストを認識"""
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        try:
            from PIL import Image
            import numpy as np

            # バイトデータから画像を読み込み
            image = Image.open(io.BytesIO(image_bytes))

            # RGB変換
            if image.mode != "RGB":
                image = image.convert("RGB")

            # numpy配列に変換
            img_array = np.array(image)

            logger.info(f"OCR実行中... (サイズ: {image.size})")
            start_time = time.time()

            # PaddleOCR実行
            results = self.ocr.ocr(img_array, cls=self.use_angle_cls)

            elapsed = time.time() - start_time

            # 結果を整形
            regions = []
            if results and results[0]:
                for idx, line in enumerate(results[0]):
                    bbox_points, (text, confidence) = line

                    # バウンディングボックスの座標を計算
                    x_coords = [p[0] for p in bbox_points]
                    y_coords = [p[1] for p in bbox_points]
                    x_min, x_max = min(x_coords), max(x_coords)
                    y_min, y_max = min(y_coords), max(y_coords)

                    region = {
                        "text": text,
                        "confidence": confidence,
                        "bbox": {
                            "points": [
                                {"x": float(p[0]), "y": float(p[1])}
                                for p in bbox_points
                            ],
                            "x": int(x_min),
                            "y": int(y_min),
                            "width": int(x_max - x_min),
                            "height": int(y_max - y_min),
                        },
                        "line_index": idx
                    }
                    regions.append(region)

            logger.info(f"OCR完了: {len(regions)}領域検出 ({elapsed*1000:.0f}ms)")

            return {
                "success": True,
                "regions": regions,
                "processing_time_ms": int(elapsed * 1000),
                "engine_name": "PaddleOCR-VL",
                "engine_version": self.VERSION
            }

        except Exception as e:
            logger.error(f"OCRエラー: {e}")
            import traceback
            traceback.print_exc()
            return {
                "success": False,
                "error": str(e),
                "regions": [],
                "processing_time_ms": 0,
                "engine_name": "PaddleOCR-VL",
                "engine_version": self.VERSION
            }


class OcrServiceServicer(ocr_pb2_grpc.OcrServiceServicer if ocr_pb2_grpc else object):
    """gRPC OCRサービス実装"""

    def __init__(self, engine: PaddleVLOcrEngine):
        self.engine = engine

    def Recognize(self, request, context):
        """OCR認識を実行"""
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
        response.details["engine"] = "PaddleOCR-VL"
        response.details["loaded"] = str(self.engine.is_loaded)
        response.timestamp.FromDatetime(datetime.utcnow())
        return response

    def IsReady(self, request, context):
        """準備状態確認"""
        response = ocr_pb2.OcrIsReadyResponse()
        response.is_ready = self.engine.is_loaded
        response.status = "ready" if self.engine.is_loaded else "loading"
        response.details["engine"] = "PaddleOCR-VL"
        response.details["version"] = self.engine.VERSION
        response.timestamp.FromDatetime(datetime.utcnow())
        return response


def serve(port: int = 50053, device: str = "cuda"):
    """gRPCサーバーを起動"""

    # エンジン初期化
    engine = PaddleVLOcrEngine(device=device)
    if not engine.load():
        logger.error("エンジンロード失敗")
        return

    # gRPCサーバー起動
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))

    if ocr_pb2_grpc:
        ocr_pb2_grpc.add_OcrServiceServicer_to_server(
            OcrServiceServicer(engine), server
        )

    server.add_insecure_port(f'[::]:{port}')
    server.start()

    logger.info(f"PaddleOCR-VL gRPCサーバー起動 (port: {port})")

    try:
        server.wait_for_termination()
    except KeyboardInterrupt:
        logger.info("サーバー停止中...")
        server.stop(5)


def main():
    """エントリーポイント"""
    parser = argparse.ArgumentParser(description="PaddleOCR-VL gRPC Server")
    parser.add_argument("--port", type=int, default=50053, help="gRPCポート番号")
    parser.add_argument("--device", type=str, default="cuda",
                        choices=["cuda", "cpu"], help="実行デバイス")
    parser.add_argument("--test", action="store_true", help="テストモード")

    args = parser.parse_args()

    if args.test:
        # テストモード: モデルロードのみ
        engine = PaddleVLOcrEngine(device=args.device)
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
