"""
gRPC Translation Server Startup Script
Phase 2.2: サーバー起動エントリーポイント

Usage:
    python start_server.py [--port PORT] [--host HOST] [--heavy-model]

Examples:
    python start_server.py
    python start_server.py --port 50051 --host localhost
    python start_server.py --heavy-model  # Use 1.3B model instead of 600M
"""

import asyncio
import argparse
import logging
import signal
import sys

import grpc
from grpc import aio
import torch

# Proto生成ファイル（コンパイル後にインポート可能になります）
from protos import translation_pb2_grpc

from translation_server import TranslationServicer
from engines.nllb_engine import NllbEngine
from engines.ctranslate2_engine import CTranslate2Engine

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('translation_server.log', encoding='utf-8')
    ]
)
logger = logging.getLogger(__name__)


class GracefulShutdown:
    """グレースフルシャットダウンハンドラー"""

    def __init__(self):
        self.shutdown_event = asyncio.Event()

    def __enter__(self):
        loop = asyncio.get_event_loop()

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


async def serve(host: str, port: int, use_heavy_model: bool = False, use_ctranslate2: bool = False):
    """gRPCサーバー起動

    Args:
        host: バインドホスト（例: "localhost", "0.0.0.0"）
        port: ポート番号（例: 50051）
        use_heavy_model: Trueで1.3Bモデル、Falseで600Mモデル使用
        use_ctranslate2: TrueでCTranslate2エンジン、Falseでtransformersエンジン使用
    """
    logger.info("=" * 80)
    logger.info("Baketa gRPC Translation Server Starting...")
    logger.info("=" * 80)

    # エンジン選択
    if use_ctranslate2:
        logger.info("Initializing CTranslate2 translation engine...")
        engine = CTranslate2Engine(
            model_path="../models/nllb-200-ct2",  # grpc_serverディレクトリからの相対パス
            device="cuda" if torch.cuda.is_available() else "cpu",
            compute_type="int8"
        )
    else:
        logger.info("Initializing NLLB translation engine...")
        engine = NllbEngine(use_heavy_model=use_heavy_model)

    logger.info("Loading NLLB model (this may take a few minutes)...")
    await engine.load_model()
    logger.info("NLLB model loaded successfully")

    # gRPCサーバー作成
    logger.info("Creating gRPC server...")
    server = aio.server()

    # TranslationServiceを登録
    servicer = TranslationServicer(engine)
    translation_pb2_grpc.add_TranslationServiceServicer_to_server(servicer, server)

    # リスニングアドレス設定
    listen_addr = f'{host}:{port}'
    server.add_insecure_port(listen_addr)

    logger.info(f"Starting gRPC server on {listen_addr}...")
    await server.start()

    logger.info("=" * 80)
    logger.info(f"gRPC Translation Server is running on {listen_addr}")
    logger.info(f"   Engine: {engine.__class__.__name__}")
    logger.info(f"   Model: {engine.model_name}")
    logger.info(f"   Device: {engine.device}")
    logger.info(f"   Supported languages: {', '.join(engine.get_supported_languages())}")
    logger.info("=" * 80)
    logger.info("Press Ctrl+C to stop the server")

    # グレースフルシャットダウン待機
    with GracefulShutdown() as shutdown_handler:
        try:
            await shutdown_handler.wait_for_shutdown()
        except KeyboardInterrupt:
            logger.info("Received KeyboardInterrupt, shutting down...")

    # サーバー停止
    logger.info("Stopping gRPC server...")
    await server.stop(grace=5.0)  # 5秒のグレースピリオド
    logger.info("gRPC server stopped")


def main():
    """コマンドライン引数パース & サーバー起動"""
    parser = argparse.ArgumentParser(
        description="Baketa gRPC Translation Server"
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
        default="0.0.0.0",
        help="gRPC server host (default: 0.0.0.0 for all interfaces)"
    )
    parser.add_argument(
        "--heavy-model",
        action="store_true",
        help="Use 1.3B model instead of 600M model (requires more memory)"
    )
    parser.add_argument(
        "--use-ctranslate2",
        action="store_true",
        help="Use CTranslate2 engine for 80%% memory reduction (2.4GB -> 500MB)"
    )
    parser.add_argument(
        "--debug",
        action="store_true",
        help="Enable debug logging"
    )

    args = parser.parse_args()

    if args.debug:
        logging.getLogger().setLevel(logging.DEBUG)
        logger.setLevel(logging.DEBUG)

    # 設定情報表示
    logger.info("Server configuration:")
    logger.info(f"  Host: {args.host}")
    logger.info(f"  Port: {args.port}")
    logger.info(f"  Heavy model: {args.heavy_model}")
    logger.info(f"  Use CTranslate2: {args.use_ctranslate2}")
    logger.info(f"  Debug mode: {args.debug}")

    # asyncioイベントループでサーバー起動
    try:
        asyncio.run(
            serve(
                host=args.host,
                port=args.port,
                use_heavy_model=args.heavy_model,
                use_ctranslate2=args.use_ctranslate2
            )
        )
    except KeyboardInterrupt:
        logger.info("Server interrupted by user")
    except Exception as e:
        logger.exception(f"Server error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
