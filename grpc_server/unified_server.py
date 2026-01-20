#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Baketa Unified AI Server
Issue #292: OCR+翻訳統合AIサーバー

Phase 1: OCR (Surya) と翻訳 (NLLB-200) を単一プロセスで実行
- VRAM削減: 2プロセス(~1GB) → 1プロセス(~500MB)
- CUDAコンテキスト共有によるメモリ効率化
- 並列モデルロードによる起動時間短縮

使用方法:
    python unified_server.py [--port PORT] [--host HOST]

例:
    python unified_server.py
    python unified_server.py --port 50051 --host 127.0.0.1
"""

# Phase 6: 全warnings完全抑制（import文より前に実行）
import warnings
warnings.filterwarnings('ignore')

import os
os.environ["PYTHONWARNINGS"] = "ignore"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
os.environ["TOKENIZERS_PARALLELISM"] = "false"

import sys
import io
import time
import asyncio
import argparse
import logging
import signal
import faulthandler
import traceback
from pathlib import Path
from datetime import datetime
from typing import Optional, List
from concurrent.futures import ThreadPoolExecutor

# miniconda CUDA DLL競合防止
def _sanitize_path_for_cuda():
    """minicondaのCUDA DLLパスをPATHから除外"""
    path = os.environ.get("PATH", "")
    path_parts = path.split(os.pathsep)
    sanitized_parts = []
    excluded_parts = []
    for part in path_parts:
        part_lower = part.lower()
        if "miniconda" in part_lower or "anaconda" in part_lower:
            excluded_parts.append(part)
        else:
            sanitized_parts.append(part)
    if excluded_parts:
        print(f"[INFO] CUDA DLL競合防止: PATH から除外しました", file=sys.stderr)
        os.environ["PATH"] = os.pathsep.join(sanitized_parts)
        return True
    return False

_sanitize_path_for_cuda()

# Python Embeddable版対応
if os.getcwd() not in sys.path:
    sys.path.insert(0, os.getcwd())

import grpc
from grpc import aio
from google.protobuf.timestamp_pb2 import Timestamp

# Proto生成ファイル
from protos import translation_pb2, translation_pb2_grpc
from protos import ocr_pb2, ocr_pb2_grpc

# エンジン
from engines.ctranslate2_engine import CTranslate2Engine
from translation_server import TranslationServicer
from resource_monitor import ResourceMonitor

# UTF-8エンコーディング強制（Windows対応）
try:
    if sys.stdout is not None and hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    if sys.stderr is not None and hasattr(sys.stderr, 'reconfigure'):
        sys.stderr.reconfigure(encoding='utf-8')
except (AttributeError, OSError):
    pass

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('unified_server.log', encoding='utf-8')
    ]
)
logger = logging.getLogger(__name__)


# ============================================================================
# Surya OCR Engine (統合版)
# ============================================================================

class SuryaOcrEngine:
    """Surya OCRエンジンラッパー (v0.17.0+ API対応)"""

    VERSION = "0.17.x"
    MAX_IMAGE_SIZE = 10 * 1024 * 1024  # 10MB
    MAX_IMAGE_DIMENSION = 2048

    def __init__(self, device: str = "cuda"):
        self.device = device
        self.foundation_predictor = None
        self.recognition_predictor = None
        self.detection_predictor = None
        self.is_loaded = False
        self.logger = logging.getLogger(f"{__name__}.SuryaOcrEngine")

    async def load_model(self) -> bool:
        """非同期でモデルをロード"""
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(None, self._load_model_sync)

    def _load_model_sync(self) -> bool:
        """同期的にモデルをロード (Surya v0.17.0+ API)"""
        try:
            self.logger.info(f"Surya OCRモデルをロード中... (device: {self.device})")
            total_start = time.time()

            # CUDA利用可否チェック
            use_cuda = False
            if self.device == "cuda":
                try:
                    import torch
                    if torch.cuda.is_available():
                        use_cuda = True
                        gpu_name = torch.cuda.get_device_name(0)
                        self.logger.info(f"CUDA利用可能: GPUモードで実行 ({gpu_name})")
                    else:
                        self.logger.info("CUDA利用不可: CPUモードで実行")
                except OSError as e:
                    self.logger.warning(f"CUDA DLLロードエラー: {e}")
                    self.logger.info("CPUモードにフォールバック")
                    os.environ["CUDA_VISIBLE_DEVICES"] = ""

            # デバイス設定の確定
            if use_cuda:
                os.environ["TORCH_DEVICE"] = "cuda"
            else:
                os.environ["TORCH_DEVICE"] = "cpu"
                self.device = "cpu"

            # Surya OCR v0.17.0+ APIのインポート
            from surya.foundation import FoundationPredictor
            from surya.recognition import RecognitionPredictor
            from surya.detection import DetectionPredictor

            # 検出モデル
            det_start = time.time()
            self.detection_predictor = DetectionPredictor()
            self.logger.info(f"[Timing] DetectionPredictor: {time.time() - det_start:.2f}秒")

            # 認識モデル (FoundationPredictor経由)
            found_start = time.time()
            self.foundation_predictor = FoundationPredictor()
            self.logger.info(f"[Timing] FoundationPredictor: {time.time() - found_start:.2f}秒")

            rec_start = time.time()
            self.recognition_predictor = RecognitionPredictor(self.foundation_predictor)
            self.logger.info(f"[Timing] RecognitionPredictor: {time.time() - rec_start:.2f}秒")

            elapsed = time.time() - total_start
            self.logger.info(f"Surya OCRモデルロード完了 (合計: {elapsed:.2f}秒)")
            self.is_loaded = True
            return True

        except ImportError as e:
            self.logger.error(f"Surya OCRライブラリが見つかりません: {e}")
            return False
        except Exception as e:
            self.logger.exception(f"モデルロードエラー: {e}")
            return False

    def _resize_image_if_needed(self, image: "Image.Image") -> tuple:
        """画像が大きすぎる場合はリサイズ"""
        from PIL import Image
        width, height = image.size
        max_dim = max(width, height)

        if max_dim <= self.MAX_IMAGE_DIMENSION:
            return image, 1.0

        scale = self.MAX_IMAGE_DIMENSION / max_dim
        new_width = int(width * scale)
        new_height = int(height * scale)

        self.logger.info(f"画像リサイズ: {width}x{height} → {new_width}x{new_height}")
        resized = image.resize((new_width, new_height), Image.Resampling.LANCZOS)

        return resized, scale

    def recognize(self, image_bytes: bytes, languages: Optional[List[str]] = None) -> dict:
        """画像からテキストを認識"""
        if not self.is_loaded:
            raise RuntimeError("モデルが未ロードです")

        if len(image_bytes) > self.MAX_IMAGE_SIZE:
            raise ValueError(f"画像サイズが上限を超えています: {len(image_bytes)} bytes")

        try:
            from PIL import Image

            image = Image.open(io.BytesIO(image_bytes))
            if image.mode != "RGB":
                image = image.convert("RGB")

            image, scale = self._resize_image_if_needed(image)

            self.logger.info(f"OCR実行中... (サイズ: {image.size})")
            start_time = time.time()

            predictions = self.recognition_predictor(
                [image],
                det_predictor=self.detection_predictor
            )

            elapsed = time.time() - start_time

            regions = []
            if predictions and len(predictions) > 0:
                ocr_result = predictions[0]
                text_lines = getattr(ocr_result, 'text_lines', [])
                if not text_lines:
                    text_lines = getattr(ocr_result, 'lines', [])

                inv_scale = 1.0 / scale if scale != 1.0 else 1.0

                for idx, line in enumerate(text_lines):
                    bbox = getattr(line, 'bbox', None)
                    polygon = getattr(line, 'polygon', None)
                    confidence = getattr(line, 'confidence', 0.0)
                    text = getattr(line, 'text', '')

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

            self.logger.info(f"OCR完了: {len(regions)}領域検出 ({elapsed*1000:.0f}ms)")

            return {
                "success": True,
                "regions": regions,
                "processing_time_ms": int(elapsed * 1000),
                "engine_name": "Surya OCR",
                "engine_version": self.VERSION
            }

        except Exception as e:
            self.logger.exception(f"OCRエラー: {e}")
            return {
                "success": False,
                "error": str(e),
                "regions": [],
                "processing_time_ms": 0,
                "engine_name": "Surya OCR",
                "engine_version": self.VERSION
            }


# ============================================================================
# OCR Servicer (非同期版)
# ============================================================================

class AsyncOcrServiceServicer(ocr_pb2_grpc.OcrServiceServicer):
    """gRPC OCRサービス実装 (非同期版)"""

    def __init__(self, engine: SuryaOcrEngine):
        self.engine = engine
        self.executor = ThreadPoolExecutor(max_workers=1)
        self.logger = logging.getLogger(f"{__name__}.AsyncOcrServiceServicer")

    async def Recognize(self, request, context):
        """OCR認識を実行"""
        self.logger.info(f"Recognize RPC called - request_id: {request.request_id}")

        try:
            languages = list(request.languages) if request.languages else None

            # 同期処理をスレッドプールで実行
            loop = asyncio.get_running_loop()
            result = await loop.run_in_executor(
                self.executor,
                self.engine.recognize,
                request.image_data,
                languages
            )

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
            self.logger.error(f"Recognize error: {e}")
            response = ocr_pb2.OcrResponse()
            response.request_id = request.request_id
            response.is_success = False
            response.error.error_type = ocr_pb2.OCR_ERROR_TYPE_PROCESSING_ERROR
            response.error.message = str(e)
            response.timestamp.FromDatetime(datetime.utcnow())
            return response

    async def HealthCheck(self, request, context):
        """ヘルスチェック"""
        response = ocr_pb2.OcrHealthCheckResponse()
        response.is_healthy = self.engine.is_loaded
        response.status = "healthy" if self.engine.is_loaded else "unhealthy"
        response.details["engine"] = "Surya OCR"
        response.details["loaded"] = str(self.engine.is_loaded)
        response.timestamp.FromDatetime(datetime.utcnow())
        return response

    async def IsReady(self, request, context):
        """準備状態確認"""
        response = ocr_pb2.OcrIsReadyResponse()
        response.is_ready = self.engine.is_loaded
        response.status = "ready" if self.engine.is_loaded else "loading"
        response.details["engine"] = "Surya OCR"
        response.details["version"] = self.engine.VERSION
        response.timestamp.FromDatetime(datetime.utcnow())
        return response


# ============================================================================
# Graceful Shutdown Handler
# ============================================================================

class GracefulShutdown:
    """グレースフルシャットダウンハンドラー"""

    def __init__(self):
        self.shutdown_event = asyncio.Event()

    def __enter__(self):
        def signal_handler(signum, frame):
            logger.info(f"Received signal {signum}, shutting down gracefully...")
            asyncio.create_task(self._async_shutdown())

        signal.signal(signal.SIGINT, signal_handler)
        signal.signal(signal.SIGTERM, signal_handler)
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        pass

    async def _async_shutdown(self):
        self.shutdown_event.set()

    async def wait_for_shutdown(self):
        await self.shutdown_event.wait()


# ============================================================================
# VRAM Check
# ============================================================================

def get_available_vram_mb() -> float:
    """利用可能なVRAM量を取得 (MB)"""
    try:
        import pynvml
        pynvml.nvmlInit()
        handle = pynvml.nvmlDeviceGetHandleByIndex(0)
        mem_info = pynvml.nvmlDeviceGetMemoryInfo(handle)
        pynvml.nvmlShutdown()
        return mem_info.total / 1024 / 1024  # MB
    except Exception:
        return 0.0


def should_use_parallel_loading() -> bool:
    """並列ロードを使用するかどうかを判定

    VRAM 8GB以上: 並列ロード (OCRと翻訳を同時にロード)
    VRAM 8GB未満: 逐次ロード (VRAMピーク抑制)
    """
    vram_mb = get_available_vram_mb()
    if vram_mb >= 8192:  # 8GB
        logger.info(f"VRAM: {vram_mb:.0f}MB - 並列モデルロードを使用")
        return True
    elif vram_mb > 0:
        logger.info(f"VRAM: {vram_mb:.0f}MB - 逐次モデルロードを使用 (VRAM節約)")
        return False
    else:
        logger.info("VRAM検出失敗 - CPUモードで逐次ロード")
        return False


# ============================================================================
# Main Server
# ============================================================================

async def serve(host: str, port: int, model_path_arg: str | None = None):
    """統合gRPCサーバー起動"""
    logger.info("=" * 80)
    logger.info("Baketa Unified AI Server Starting...")
    logger.info("Issue #292: OCR + Translation in single process")
    logger.info("=" * 80)

    # GPU検出
    is_cuda_available = False
    nvml_initialized = False
    try:
        import pynvml
        pynvml.nvmlInit()
        nvml_initialized = True
        device_count = pynvml.nvmlDeviceGetCount()
        is_cuda_available = device_count > 0
        if is_cuda_available:
            handle = pynvml.nvmlDeviceGetHandleByIndex(0)
            gpu_name = pynvml.nvmlDeviceGetName(handle)
            if isinstance(gpu_name, bytes):
                gpu_name = gpu_name.decode('utf-8')
            logger.info(f"GPU detection: {device_count} CUDA device(s) found - {gpu_name}")
    except Exception as e:
        logger.warning(f"GPU detection failed: {e}")
    finally:
        if nvml_initialized:
            pynvml.nvmlShutdown()

    device = "cuda" if is_cuda_available else "cpu"

    # モデルパス決定 (翻訳用)
    if model_path_arg:
        translation_model_path = Path(model_path_arg)
    else:
        appdata = os.environ.get('APPDATA', os.path.expanduser('~'))
        translation_model_path = Path(appdata) / "Baketa" / "Models" / "nllb-200-1.3B-ct2"

    logger.info(f"Translation model path: {translation_model_path}")
    logger.info(f"Device: {device}")

    # エンジン初期化
    translation_engine = CTranslate2Engine(
        model_path=str(translation_model_path),
        device=device,
        compute_type="int8"
    )

    ocr_engine = SuryaOcrEngine(device=device)

    # モデルロード (並列 or 逐次)
    use_parallel = should_use_parallel_loading()

    logger.info("=" * 80)
    logger.info("Loading AI Models...")
    logger.info("=" * 80)

    load_start = time.time()

    if use_parallel:
        # 並列ロード (VRAM 8GB以上)
        logger.info("Parallel model loading started...")

        async def load_translation():
            logger.info("[Translation] Loading NLLB-200-distilled-1.3B...")
            await translation_engine.load_model()
            logger.info("[Translation] Model loaded successfully")

        async def load_ocr():
            logger.info("[OCR] Loading Surya OCR...")
            await ocr_engine.load_model()
            logger.info("[OCR] Model loaded successfully")

        await asyncio.gather(load_translation(), load_ocr())
    else:
        # 逐次ロード (VRAM節約)
        logger.info("Sequential model loading started (VRAM saving mode)...")

        logger.info("[Translation] Loading NLLB-200-distilled-1.3B...")
        await translation_engine.load_model()
        logger.info("[Translation] Model loaded successfully")

        logger.info("[OCR] Loading Surya OCR...")
        await ocr_engine.load_model()
        logger.info("[OCR] Model loaded successfully")

    load_elapsed = time.time() - load_start
    logger.info(f"All models loaded in {load_elapsed:.2f} seconds")

    # gRPCサーバー作成
    logger.info("Creating unified gRPC server...")

    MAX_MESSAGE_LENGTH = 50 * 1024 * 1024  # 50MB (高解像度画像対応)

    server = aio.server(options=[
        # メッセージサイズ制限
        ('grpc.max_receive_message_length', MAX_MESSAGE_LENGTH),
        ('grpc.max_send_message_length', MAX_MESSAGE_LENGTH),
        # KeepAlive設定
        ('grpc.keepalive_time_ms', 30000),
        ('grpc.keepalive_timeout_ms', 10000),
        ('grpc.keepalive_permit_without_calls', True),
        ('grpc.http2.min_time_between_pings_ms', 10000),
        ('grpc.http2.max_pings_without_data', 0),
        ('grpc.http2.min_ping_interval_without_data_ms', 10000),
    ])

    # サービス登録
    translation_servicer = TranslationServicer(translation_engine)
    translation_pb2_grpc.add_TranslationServiceServicer_to_server(translation_servicer, server)

    ocr_servicer = AsyncOcrServiceServicer(ocr_engine)
    ocr_pb2_grpc.add_OcrServiceServicer_to_server(ocr_servicer, server)

    # リスニングアドレス設定
    listen_addr = f'{host}:{port}'
    server.add_insecure_port(listen_addr)

    logger.info(f"Starting unified gRPC server on {listen_addr}...")
    await server.start()

    logger.info("=" * 80)
    logger.info(f"Baketa Unified AI Server is running on {listen_addr}")
    logger.info(f"   Translation Engine: {translation_engine.__class__.__name__}")
    logger.info(f"   Translation Model: {translation_engine.model_name}")
    logger.info(f"   OCR Engine: Surya OCR v{ocr_engine.VERSION}")
    logger.info(f"   Device: {device}")

    # C#側への起動完了シグナル
    try:
        if sys.stderr is not None:
            sys.stderr.write("[SERVER_START]\n")
            sys.stderr.flush()
            logger.info("[SERVER_START] signal sent to stderr for C# detection")
    except (OSError, AttributeError):
        logger.info("[SERVER_START] signal skipped (stderr unavailable)")

    logger.info("=" * 80)
    logger.info("Services available:")
    logger.info("   - TranslationService (Translate, TranslateBatch, HealthCheck, IsReady)")
    logger.info("   - OcrService (Recognize, HealthCheck, IsReady)")
    logger.info("=" * 80)
    logger.info("Press Ctrl+C to stop the server")

    # リソース監視開始
    resource_monitor = ResourceMonitor(enable_gpu_monitoring=is_cuda_available)
    await resource_monitor.start_monitoring(interval_seconds=300)
    logger.info("[Resource Monitor] Started (5-minute interval)")

    # グレースフルシャットダウン待機
    with GracefulShutdown() as shutdown_handler:
        try:
            await shutdown_handler.wait_for_shutdown()
        except KeyboardInterrupt:
            logger.info("Received KeyboardInterrupt, shutting down...")

    # サーバー停止
    logger.info("Stopping unified gRPC server...")
    await server.stop(grace=5.0)
    logger.info("gRPC server stopped")

    # リソース監視クリーンアップ
    await resource_monitor.stop_monitoring()
    resource_monitor.cleanup()
    logger.info("Resource monitoring cleanup completed")


def global_exception_handler(exc_type, exc_value, exc_traceback):
    """グローバル例外ハンドラー"""
    if issubclass(exc_type, KeyboardInterrupt):
        sys.__excepthook__(exc_type, exc_value, exc_traceback)
        return

    logger.critical("=" * 80)
    logger.critical("UNCAUGHT EXCEPTION - CRITICAL ERROR")
    logger.critical("=" * 80)
    logger.critical(f"Exception Type: {exc_type.__name__}")
    logger.critical(f"Exception Value: {exc_value}")
    logger.critical("Traceback:")
    logger.critical("".join(traceback.format_exception(exc_type, exc_value, exc_traceback)))
    logger.critical("=" * 80)


def main():
    """コマンドライン引数パース & サーバー起動"""
    faulthandler.enable(file=sys.stderr, all_threads=True)
    sys.excepthook = global_exception_handler

    parser = argparse.ArgumentParser(
        description="Baketa Unified AI Server (OCR + Translation)"
    )
    parser.add_argument(
        "--port",
        type=int,
        default=50051,
        help="gRPC server port (default: 50051)"
    )
    parser.add_argument(
        "--host",
        type=str,
        default="127.0.0.1",
        help="gRPC server host (default: 127.0.0.1)"
    )
    parser.add_argument(
        "--debug",
        action="store_true",
        help="Enable debug logging"
    )
    parser.add_argument(
        "--model-path",
        type=str,
        default=None,
        help="Path to translation model directory"
    )

    args = parser.parse_args()

    if args.debug:
        logging.getLogger().setLevel(logging.DEBUG)

    logger.info("Server configuration:")
    logger.info(f"  Host: {args.host}")
    logger.info(f"  Port: {args.port}")
    logger.info(f"  Model path: {args.model_path or '(default)'}")
    logger.info(f"  Debug mode: {args.debug}")

    try:
        asyncio.run(serve(
            host=args.host,
            port=args.port,
            model_path_arg=args.model_path
        ))
    except KeyboardInterrupt:
        logger.info("Server interrupted by user")
    except Exception as e:
        logger.exception(f"Server error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
