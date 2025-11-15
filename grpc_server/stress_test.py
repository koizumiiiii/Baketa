"""
24時間ストレステストスクリプト
Phase 2: Python翻訳サーバーの長期稼働安定性検証

目的:
- GPU VRAMリークの検出
- メモリ使用量の長期推移測定
- クラッシュ再現性の確認

実行方法:
    python stress_test.py --duration 24 --interval 0.5

オプション:
    --duration: テスト時間（時間単位、デフォルト24）
    --interval: リクエスト間隔（秒単位、デフォルト0.5）
    --server-address: gRPCサーバーアドレス（デフォルト localhost:50051）
"""

import asyncio
import argparse
import logging
import random
import sys
from datetime import datetime, timedelta
from typing import List, Tuple

import grpc
from protos import translation_pb2, translation_pb2_grpc

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('stress_test.log', encoding='utf-8')
    ]
)
logger = logging.getLogger(__name__)


class StressTestRunner:
    """24時間ストレステスト実行クラス"""

    def __init__(self, server_address: str, duration_hours: int, interval_seconds: float):
        self.server_address = server_address
        self.duration_hours = duration_hours
        self.interval_seconds = interval_seconds

        self.translation_count = 0
        self.error_count = 0
        self.start_time = None
        self.end_time = None

        # テスト用翻訳テキスト（バリエーション）
        self.test_texts = [
            "こんにちは、世界！",
            "今日はいい天気ですね。",
            "翻訳システムのテストを実行しています。",
            "長期稼働の安定性を検証します。",
            "GPU VRAMリークを検出するためのストレステストです。",
            "メモリ使用量の推移を24時間監視します。",
            "クラッシュが発生しないことを確認します。",
            "このテキストは日本語から英語に翻訳されます。",
        ]

    async def run_stress_test(self):
        """ストレステスト本体"""
        logger.info("=" * 80)
        logger.info("24時間ストレステスト開始")
        logger.info("=" * 80)
        logger.info(f"サーバーアドレス: {self.server_address}")
        logger.info(f"テスト時間: {self.duration_hours} 時間")
        logger.info(f"リクエスト間隔: {self.interval_seconds} 秒")
        logger.info("=" * 80)

        self.start_time = datetime.now()
        self.end_time = self.start_time + timedelta(hours=self.duration_hours)

        logger.info(f"開始時刻: {self.start_time}")
        logger.info(f"終了予定時刻: {self.end_time}")

        try:
            # gRPCチャネル作成
            async with grpc.aio.insecure_channel(self.server_address) as channel:
                stub = translation_pb2_grpc.TranslationServiceStub(channel)

                # メインループ
                while datetime.now() < self.end_time:
                    try:
                        # ランダムにテストテキストを選択
                        test_text = random.choice(self.test_texts)

                        # 翻訳リクエスト
                        # 🔥 [PHASE2.1_FIX] Protobuf定義に合わせてフィールド名修正
                        source_language = translation_pb2.Language(code="ja")
                        target_language = translation_pb2.Language(code="en")

                        request = translation_pb2.TranslateRequest(
                            source_text=test_text,          # text → source_text
                            source_language=source_language, # source_lang → source_language (Language型)
                            target_language=target_language  # target_lang → target_language (Language型)
                        )

                        # タイムアウト設定（10秒）
                        response = await asyncio.wait_for(
                            stub.Translate(request),
                            timeout=10.0
                        )

                        if response.translated_text:
                            self.translation_count += 1
                        else:
                            logger.warning(f"[EMPTY_RESPONSE] リクエスト {self.translation_count + 1}")
                            self.error_count += 1

                        # 統計出力（100回ごと）
                        if self.translation_count % 100 == 0:
                            self._log_statistics()

                    except asyncio.TimeoutError:
                        self.error_count += 1
                        logger.error(f"[TIMEOUT_ERROR] リクエスト {self.translation_count + 1}")
                    except grpc.aio.AioRpcError as e:
                        self.error_count += 1
                        logger.error(
                            f"[gRPC_ERROR] Status: {e.code()}, "
                            f"Details: {e.details()}, "
                            f"リクエスト: {self.translation_count + 1}"
                        )
                    except Exception as e:
                        self.error_count += 1
                        logger.exception(f"[UNKNOWN_ERROR] {e}")

                    # リクエスト間隔待機
                    await asyncio.sleep(self.interval_seconds)

        except KeyboardInterrupt:
            logger.info("ユーザーによる中断")
        except Exception as e:
            logger.exception(f"致命的エラー: {e}")
        finally:
            self._log_final_statistics()

    def _log_statistics(self):
        """統計情報ログ出力"""
        elapsed = (datetime.now() - self.start_time).total_seconds()
        elapsed_hours = elapsed / 3600
        remaining = (self.end_time - datetime.now()).total_seconds() / 3600

        success_rate = 0.0
        if self.translation_count + self.error_count > 0:
            success_rate = (self.translation_count / (self.translation_count + self.error_count)) * 100

        req_per_min = (self.translation_count / elapsed) * 60 if elapsed > 0 else 0.0

        logger.info("=" * 80)
        logger.info("[STATISTICS]")
        logger.info(f"  成功翻訳数: {self.translation_count}")
        logger.info(f"  エラー数: {self.error_count}")
        logger.info(f"  成功率: {success_rate:.2f}%")
        logger.info(f"  経過時間: {elapsed_hours:.2f} 時間")
        logger.info(f"  残り時間: {remaining:.2f} 時間")
        logger.info(f"  処理速度: {req_per_min:.2f} req/min")
        logger.info(f"  現在時刻: {datetime.now()}")
        logger.info("=" * 80)

    def _log_final_statistics(self):
        """最終統計情報ログ出力"""
        total_elapsed = (datetime.now() - self.start_time).total_seconds()
        total_hours = total_elapsed / 3600

        success_rate = 0.0
        if self.translation_count + self.error_count > 0:
            success_rate = (self.translation_count / (self.translation_count + self.error_count)) * 100

        avg_req_per_min = (self.translation_count / total_elapsed) * 60 if total_elapsed > 0 else 0.0

        logger.info("=" * 80)
        logger.info("ストレステスト終了")
        logger.info("=" * 80)
        logger.info(f"開始時刻: {self.start_time}")
        logger.info(f"終了時刻: {datetime.now()}")
        logger.info(f"総実行時間: {total_hours:.2f} 時間")
        logger.info(f"総翻訳数: {self.translation_count}")
        logger.info(f"総エラー数: {self.error_count}")
        logger.info(f"成功率: {success_rate:.2f}%")
        logger.info(f"平均処理速度: {avg_req_per_min:.2f} req/min")
        logger.info("=" * 80)


async def main():
    """メイン関数"""
    parser = argparse.ArgumentParser(
        description="Python翻訳サーバー24時間ストレステスト"
    )
    parser.add_argument(
        "--duration",
        type=int,
        default=24,
        help="テスト時間（時間単位、デフォルト24）"
    )
    parser.add_argument(
        "--interval",
        type=float,
        default=0.5,
        help="リクエスト間隔（秒単位、デフォルト0.5）"
    )
    parser.add_argument(
        "--server-address",
        type=str,
        default="localhost:50051",
        help="gRPCサーバーアドレス（デフォルト localhost:50051）"
    )

    args = parser.parse_args()

    # ストレステスト実行
    runner = StressTestRunner(
        server_address=args.server_address,
        duration_hours=args.duration,
        interval_seconds=args.interval
    )

    await runner.run_stress_test()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        logger.info("プログラム終了")
    except Exception as e:
        logger.exception(f"予期しないエラー: {e}")
        sys.exit(1)
