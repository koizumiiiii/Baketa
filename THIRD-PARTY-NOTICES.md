# Third-party notices / 第三者コンポーネント

2026-11-14 調査。Baketa本体の[MIT](LICENSE)とは別に各ライブラリ・モデルの条件が適用されます。本一覧はローカルの採用環境・ソースにある表記の記録であり、各配布物のLICENSE・NOTICEを置き換えません。

## 確認範囲

[PyInstaller spec](grpc_server/BaketaUnifiedServer.spec)はSurya、torch、torchvision、gRPC、Transformers、tokenizers、Pillow、NumPy、OpenCV等を取り込みます。[CPUロック](grpc_server/requirements-lock-cpu.txt)・[CUDAロック](grpc_server/requirements-lock-cuda.txt)には開発・変換・テスト用依存も含まれ、全項目のexe同梱を意味しません。ロックは公開済み配布物のビルド時点との完全一致を保証していません。

Pythonの根拠は `grpc_server/<環境>/Lib/site-packages/` 配下、NuGetは `%USERPROFILE%/.nuget/packages/<小文字ID>/<版>/` 配下です。キャッシュはリポジトリに含まれません。公開アセットの全ファイル照合・外部アクセスは未実施です。

## モデル・トークナイザー

| コンポーネント | ライセンス・変更 | 根拠・確認範囲 |
|---|---|---|
| Meta NLLB-200 distilled-600M | CC-BY-NC-4.0。ONNX変換・INT8量子化した改変版 | 依頼で提示されたモデル条件。[元モデル](https://huggingface.co/facebook/nllb-200-distilled-600M)。実装: `Baketa.Infrastructure/Translation/Onnx/OnnxTranslationEngine.cs`。ローカルのモデルカード・LICENSE原本は未確認 |
| NLLB tokenizer.json（f8d333a098d19b4fd9a8b18f94170487ad3f821d） | 要確認（配布tokenizerに適用される原本の個別確認が必要） | [READMEの固定取得元](README.md)。モデルの非商用条件とtokenizer実装ライブラリの条件を混同しない |
| Surya OCRコード 0.17.1 CPU / 0.17.0 CUDA | GPL-3.0-or-later | 両版の `surya_ocr-<版>.dist-info/METADATA` はGPL表記。CPU版 `licenses/LICENSE` はGPLv3本文。CUDA版の同名ファイルはモデル用Open RAIL-M本文であり、コードのLICENSE同梱状況は要確認。上流URLはMETADATAの [VikParuchuri/surya](https://github.com/VikParuchuri/surya) |
| Surya検出・認識モデル重み | modified AI Pubs Open Rail-M（コードとは別） | 両版のMETADATA本文。research / personal use / funding・revenueが$2M未満のstartup向け無償との説明あり。CUDA版 `surya_ocr-0.17.0.dist-info/licenses/LICENSE` に改変条項全文（収益・資金調達・競合用途の制限等）を確認。実際に取得される各モデル重みとこの原本の対応は要確認。この説明だけで利用範囲を確定しない |

## Python環境のパッケージ

License-Expression / License / Classifierを確認。単にBSD等と記載されたものは条項数を推測しません。環境略称 CPU=`venv_build`、CUDA=`venv_build_cuda`。根拠欄は各環境のsite-packages内の相対パスです。

| パッケージ | バージョン | 環境 | ライセンス表記 | 根拠 |
|---|---|---|---|---|
| altgraph | 0.17.5 | CPU, CUDA | MIT | `altgraph-0.17.5.dist-info/METADATA` |
| annotated-types | 0.7.0 | CPU, CUDA | MIT License | `annotated_types-0.7.0.dist-info/METADATA` |
| anyio | 4.12.0 | CUDA | MIT | `anyio-4.12.0.dist-info/METADATA` |
| anyio | 4.12.1 | CPU | MIT | `anyio-4.12.1.dist-info/METADATA` |
| backports.asyncio.runner | 1.2.0 | CPU, CUDA | PSF-2.0 | `backports_asyncio_runner-1.2.0.dist-info/METADATA` |
| certifi | 2026.1.4 | CPU, CUDA | MPL-2.0 | `certifi-2026.1.4.dist-info/METADATA` |
| cfgv | 3.5.0 | CPU, CUDA | MIT | `cfgv-3.5.0.dist-info/METADATA` |
| charset-normalizer | 3.4.4 | CPU, CUDA | MIT | `charset_normalizer-3.4.4.dist-info/METADATA` |
| click | 8.3.1 | CPU, CUDA | BSD-3-Clause | `click-8.3.1.dist-info/METADATA` |
| colorama | 0.4.6 | CPU, CUDA | BSD License | `colorama-0.4.6.dist-info/METADATA` |
| coloredlogs | 15.0.1 | CPU | MIT | `coloredlogs-15.0.1.dist-info/METADATA` |
| ctranslate2 | 4.6.2 | CUDA | MIT License | `ctranslate2-4.6.2.dist-info/METADATA` |
| ctranslate2 | 4.6.3 | CPU | MIT | `ctranslate2-4.6.3.dist-info/METADATA` |
| distlib | 0.4.0 | CPU, CUDA | PSF-2.0 | `distlib-0.4.0.dist-info/METADATA` |
| einops | 0.8.1 | CUDA | MIT | `einops-0.8.1.dist-info/METADATA` |
| einops | 0.8.2 | CPU | MIT | `einops-0.8.2.dist-info/METADATA` |
| exceptiongroup | 1.3.1 | CPU, CUDA | MIT License | `exceptiongroup-1.3.1.dist-info/METADATA` |
| filelock | 3.20.0 | CUDA | Unlicense | `filelock-3.20.0.dist-info/METADATA` |
| filelock | 3.20.3 | CPU | Unlicense | `filelock-3.20.3.dist-info/METADATA` |
| filetype | 1.2.0 | CPU, CUDA | MIT | `filetype-1.2.0.dist-info/METADATA` |
| flatbuffers | 25.12.19 | CPU | Apache 2.0 | `flatbuffers-25.12.19.dist-info/METADATA` |
| fsspec | 2025.12.0 | CUDA | BSD-3-Clause | `fsspec-2025.12.0.dist-info/METADATA` |
| fsspec | 2026.1.0 | CPU | BSD-3-Clause | `fsspec-2026.1.0.dist-info/METADATA` |
| grpcio | 1.76.0 | CPU, CUDA | Apache License 2.0 | `grpcio-1.76.0.dist-info/METADATA` |
| grpcio-health-checking | 1.76.0 | CPU, CUDA | Apache License 2.0 | `grpcio_health_checking-1.76.0.dist-info/METADATA` |
| grpcio-tools | 1.76.0 | CPU, CUDA | Apache License 2.0 | `grpcio_tools-1.76.0.dist-info/METADATA` |
| h11 | 0.16.0 | CPU, CUDA | MIT | `h11-0.16.0.dist-info/METADATA` |
| hf-xet | 1.2.0 | CPU, CUDA | Apache-2.0 | `hf_xet-1.2.0.dist-info/METADATA` |
| httpcore | 1.0.9 | CPU, CUDA | BSD-3-Clause | `httpcore-1.0.9.dist-info/METADATA` |
| httpx | 0.28.1 | CPU, CUDA | BSD-3-Clause | `httpx-0.28.1.dist-info/METADATA` |
| huggingface-hub | 0.36.0 | CUDA | Apache | `huggingface_hub-0.36.0.dist-info/METADATA` |
| huggingface_hub | 0.36.1 | CPU | Apache | `huggingface_hub-0.36.1.dist-info/METADATA` |
| humanfriendly | 10.0 | CPU | MIT | `humanfriendly-10.0.dist-info/METADATA` |
| identify | 2.6.15 | CUDA | MIT | `identify-2.6.15.dist-info/METADATA` |
| identify | 2.6.16 | CPU | MIT | `identify-2.6.16.dist-info/METADATA` |
| idna | 3.11 | CPU, CUDA | BSD-3-Clause | `idna-3.11.dist-info/METADATA` |
| iniconfig | 2.3.0 | CPU, CUDA | MIT | `iniconfig-2.3.0.dist-info/METADATA` |
| Jinja2 | 3.1.6 | CPU, CUDA | BSD License | `jinja2-3.1.6.dist-info/METADATA` |
| MarkupSafe | 2.1.5 | CUDA | BSD-3-Clause | `MarkupSafe-2.1.5.dist-info/METADATA` |
| MarkupSafe | 3.0.3 | CPU | BSD-3-Clause | `markupsafe-3.0.3.dist-info/METADATA` |
| ml_dtypes | 0.5.4 | CPU | Apache-2.0 | `ml_dtypes-0.5.4.dist-info/METADATA` |
| mpmath | 1.3.0 | CPU, CUDA | BSD | `mpmath-1.3.0.dist-info/METADATA` |
| networkx | 3.4.2 | CPU, CUDA | BSD License | `networkx-3.4.2.dist-info/METADATA` |
| nodeenv | 1.10.0 | CPU, CUDA | BSD | `nodeenv-1.10.0.dist-info/METADATA` |
| numpy | 2.2.6 | CPU, CUDA | BSD（本体。METADATAに第三者の別条件あり） | `numpy-2.2.6.dist-info/METADATA` |
| nvidia-ml-py | 13.590.44 | CUDA | BSD | `nvidia_ml_py-13.590.44.dist-info/METADATA` |
| nvidia-ml-py | 13.590.48 | CPU | BSD | `nvidia_ml_py-13.590.48.dist-info/METADATA` |
| onnx | 1.20.1 | CPU | Apache-2.0 | `onnx-1.20.1.dist-info/METADATA` |
| onnx-ir | 0.1.15 | CPU | Apache-2.0 | `onnx_ir-0.1.15.dist-info/METADATA` |
| onnxruntime | 1.23.2 | CPU | MIT License | `onnxruntime-1.23.2.dist-info/METADATA` |
| onnxscript | 0.6.0 | CPU | MIT（METADATA本文） | `onnxscript-0.6.0.dist-info/METADATA` |
| opencv-python-headless | 4.11.0.86 | CPU, CUDA | Apache 2.0 | `opencv_python_headless-4.11.0.86.dist-info/METADATA` |
| optimum | 2.1.0 | CPU | Apache | `optimum-2.1.0.dist-info/METADATA` |
| optimum-onnx | 0.1.0 | CPU | Apache-2.0 | `optimum_onnx-0.1.0.dist-info/METADATA` |
| packaging | 26.0 | CPU, CUDA | Apache-2.0 OR BSD-2-Clause | `packaging-26.0.dist-info/METADATA` |
| pefile | 2024.8.26 | CPU, CUDA | MIT | `pefile-2024.8.26.dist-info/METADATA` |
| pillow | 10.4.0 | CPU, CUDA | HPND | `pillow-10.4.0.dist-info/METADATA` |
| platformdirs | 4.5.1 | CPU, CUDA | MIT | `platformdirs-4.5.1.dist-info/METADATA` |
| pluggy | 1.6.0 | CPU, CUDA | MIT | `pluggy-1.6.0.dist-info/METADATA` |
| pre_commit | 4.5.1 | CPU, CUDA | MIT | `pre_commit-4.5.1.dist-info/METADATA` |
| protobuf | 6.33.4 | CPU, CUDA | 3-Clause BSD License | `protobuf-6.33.4.dist-info/METADATA` |
| psutil | 7.2.1 | CPU, CUDA | BSD-3-Clause | `psutil-7.2.1.dist-info/METADATA` |
| pydantic | 2.12.5 | CPU, CUDA | MIT | `pydantic-2.12.5.dist-info/METADATA` |
| pydantic_core | 2.41.5 | CPU, CUDA | MIT | `pydantic_core-2.41.5.dist-info/METADATA` |
| pydantic-settings | 2.12.0 | CPU, CUDA | MIT | `pydantic_settings-2.12.0.dist-info/METADATA` |
| Pygments | 2.19.2 | CPU, CUDA | BSD-2-Clause | `pygments-2.19.2.dist-info/METADATA` |
| pyinstaller | 6.18.0 | CPU, CUDA | GPL-2.0-or-later + 特別例外（licenses/COPYING.txt） | `pyinstaller-6.18.0.dist-info/METADATA` |
| pyinstaller-hooks-contrib | 2025.11 | CUDA | GPL-2.0-or-later / Apache-2.0（ファイル別。licenses/LICENSE） | `pyinstaller_hooks_contrib-2025.11.dist-info/METADATA` |
| pyinstaller-hooks-contrib | 2026.0 | CPU | GPL-2.0-or-later / Apache-2.0（ファイル別。licenses/LICENSE） | `pyinstaller_hooks_contrib-2026.0.dist-info/METADATA` |
| pynvml | 13.0.1 | CPU, CUDA | BSD-3-Clause | `pynvml-13.0.1.dist-info/METADATA` |
| pypdfium2 | 4.30.0 | CPU, CUDA | (Apache-2.0 OR BSD-3-Clause) AND LicenseRef-PdfiumThirdParty | `pypdfium2-4.30.0.dist-info/METADATA` |
| pyreadline3 | 3.5.4 | CPU | BSD | `pyreadline3-3.5.4.dist-info/METADATA` |
| pytest | 9.0.2 | CPU, CUDA | MIT | `pytest-9.0.2.dist-info/METADATA` |
| pytest-asyncio | 1.3.0 | CPU, CUDA | Apache-2.0 | `pytest_asyncio-1.3.0.dist-info/METADATA` |
| python-dotenv | 1.2.1 | CPU, CUDA | BSD-3-Clause | `python_dotenv-1.2.1.dist-info/METADATA` |
| pywin32-ctypes | 0.2.3 | CPU, CUDA | BSD-3-Clause | `pywin32_ctypes-0.2.3.dist-info/METADATA` |
| PyYAML | 6.0.3 | CPU, CUDA | MIT | `pyyaml-6.0.3.dist-info/METADATA` |
| regex | 2025.11.3 | CUDA | Apache-2.0 AND CNRI-Python | `regex-2025.11.3.dist-info/METADATA` |
| regex | 2026.1.15 | CPU | Apache-2.0 AND CNRI-Python | `regex-2026.1.15.dist-info/METADATA` |
| requests | 2.32.5 | CPU, CUDA | Apache-2.0 | `requests-2.32.5.dist-info/METADATA` |
| safetensors | 0.7.0 | CPU, CUDA | Apache Software License | `safetensors-0.7.0.dist-info/METADATA` |
| shellingham | 1.5.4 | CPU, CUDA | ISC License | `shellingham-1.5.4.dist-info/METADATA` |
| surya-ocr | 0.17.0 | CUDA | GPL-3.0-or-later | `surya_ocr-0.17.0.dist-info/METADATA` |
| surya-ocr | 0.17.1 | CPU | GPL-3.0-or-later | `surya_ocr-0.17.1.dist-info/METADATA` |
| sympy | 1.14.0 | CPU, CUDA | BSD | `sympy-1.14.0.dist-info/METADATA` |
| tokenizers | 0.22.1 | CUDA | Apache Software License | `tokenizers-0.22.1.dist-info/METADATA` |
| tokenizers | 0.22.2 | CPU | Apache Software License | `tokenizers-0.22.2.dist-info/METADATA` |
| tomli | 2.3.0 | CUDA | MIT | `tomli-2.3.0.dist-info/METADATA` |
| tomli | 2.4.0 | CPU | MIT | `tomli-2.4.0.dist-info/METADATA` |
| torch | 2.10.0+cpu | CPU | BSD-3-Clause | `torch-2.10.0+cpu.dist-info/METADATA` |
| torch | 2.9.1+cu126 | CUDA | BSD-3-Clause | `torch-2.9.1+cu126.dist-info/METADATA` |
| torchaudio | 2.9.1+cu126 | CUDA | BSD-2-Clause（licenses/LICENSE） | `torchaudio-2.9.1+cu126.dist-info/METADATA` |
| torchvision | 0.24.1+cu126 | CUDA | BSD | `torchvision-0.24.1+cu126.dist-info/METADATA` |
| tqdm | 4.67.1 | CPU, CUDA | MPL-2.0 AND MIT | `tqdm-4.67.1.dist-info/METADATA` |
| transformers | 4.57.3 | CUDA | Apache 2.0 License | `transformers-4.57.3.dist-info/METADATA` |
| transformers | 4.57.6 | CPU | Apache 2.0 License | `transformers-4.57.6.dist-info/METADATA` |
| typer-slim | 0.21.0 | CUDA | MIT | `typer_slim-0.21.0.dist-info/METADATA` |
| typer-slim | 0.21.1 | CPU | MIT | `typer_slim-0.21.1.dist-info/METADATA` |
| typing_extensions | 4.15.0 | CPU, CUDA | PSF-2.0 | `typing_extensions-4.15.0.dist-info/METADATA` |
| typing-inspection | 0.4.2 | CPU, CUDA | MIT | `typing_inspection-0.4.2.dist-info/METADATA` |
| urllib3 | 2.6.2 | CUDA | MIT | `urllib3-2.6.2.dist-info/METADATA` |
| urllib3 | 2.6.3 | CPU | MIT | `urllib3-2.6.3.dist-info/METADATA` |
| virtualenv | 20.35.4 | CUDA | MIT | `virtualenv-20.35.4.dist-info/METADATA` |
| virtualenv | 20.36.1 | CPU | MIT | `virtualenv-20.36.1.dist-info/METADATA` |

### ネイティブ依存・ビルドツールの補足

- **PyTorch**: CPU 2.10.0+cpu、CUDA 2.9.1+cu126はBSD-3-Clause。dist-infoのLICENSEにはPyTorch以外の帰属もあります。付属バイナリすべてをBSD扱いしません。
- **CUDA / cuDNN等**: CUDA環境の `torch/lib` で `cudart64_12.dll`、`cublas64_12.dll`、`cudnn64_9.dll`、`cufft64_11.dll`、`curand64_10.dll`、`cusolver64_11.dll`、`cusparse64_12.dll`、`nvrtc64_120_0.dll` 等を確認。個別の版、適用ライセンス原本、公開アセットへの同梱範囲は**要確認**。PyTorchのBSDを流用しません。
- **OpenCV / FFmpeg**: `opencv_python_headless-4.11.0.86.dist-info/LICENSE-3RD-PARTY.txt` はOpenCVのApache-2.0に加え、全opencv-pythonパッケージへのFFmpeg再配布とLGPL-2.1本文を記載。同文書内のmacOS専用ライブラリをWindows同梱の証拠にはしません。
- **PyInstaller**: GPL-2.0-or-laterと特別例外（`licenses/COPYING.txt`）。COPYING.txtは生成exeへ含まれるbootloaderと関連ファイルへの例外、run-time hooksのApache-2.0も記載しています。ビルドツール本体と生成exeの条件を区別します。hooks-contribはGPL-2.0-or-later / Apache-2.0のファイル別条件です。
- **certifi / tqdm**: MPL-2.0、MPL-2.0 AND MIT表記をそれぞれ確認。
- **pypdfium2**: Apache-2.0 OR BSD-3-Clauseに加え `LicenseRef-PdfiumThirdParty.txt` にPDFiumの第三者条件があります。
- **Pythonランタイム、VC++ランタイム、OpenCvSharp内包物、Interフォント自体、その他推移依存**: 各配布版の原本・同梱範囲は要確認。NuGet単位の表記で内包物の条件を代用しません。

## NuGet直接参照

`Baketa.*/*.csproj` から抽出（テストプロジェクトを除く）。条件付き・開発用・旧機能の参照も含み、参照の存在は機能の有効化を意味しません。NetSparkle参照は残りますが最終版の自動更新確認は無効です。

| パッケージ | バージョン | ライセンス | 根拠（キャッシュ内相対パス） |
|---|---|---|---|
| Avalonia | 11.3.3 | MIT | `avalonia/11.3.3/avalonia.nuspec` |
| Avalonia.Desktop | 11.3.3 | MIT | `avalonia.desktop/11.3.3/avalonia.desktop.nuspec` |
| Avalonia.Diagnostics | 11.3.3 | MIT | `avalonia.diagnostics/11.3.3/avalonia.diagnostics.nuspec` |
| Avalonia.Fonts.Inter | 11.3.3 | MIT | `avalonia.fonts.inter/11.3.3/avalonia.fonts.inter.nuspec` |
| Avalonia.ReactiveUI | 11.3.3 | MIT | `avalonia.reactiveui/11.3.3/avalonia.reactiveui.nuspec` |
| Avalonia.Themes.Fluent | 11.3.3 | MIT | `avalonia.themes.fluent/11.3.3/avalonia.themes.fluent.nuspec` |
| Avalonia.Xaml.Interactivity | 11.3.0.6 | MIT | `avalonia.xaml.interactivity/11.3.0.6/avalonia.xaml.interactivity.nuspec` |
| Google.Protobuf | 3.32.1 | BSD-3-Clause | `google.protobuf/3.32.1/google.protobuf.nuspec` |
| Google.Protobuf.Tools | 3.32.1 | 要確認（nuspecにlicenseUrlのみ） | `google.protobuf.tools/3.32.1/google.protobuf.tools.nuspec` |
| Grpc.HealthCheck | 2.70.0 | Apache-2.0 | `grpc.healthcheck/2.70.0/grpc.healthcheck.nuspec` |
| Grpc.Net.Client | 2.70.0 | Apache-2.0 | `grpc.net.client/2.70.0/grpc.net.client.nuspec` |
| Grpc.Tools | 2.70.0 | Apache-2.0 | `grpc.tools/2.70.0/grpc.tools.nuspec` |
| Microsoft.Extensions.Configuration | 8.0.0 | MIT | `microsoft.extensions.configuration/8.0.0/microsoft.extensions.configuration.nuspec` |
| Microsoft.Extensions.Configuration.Binder | 8.0.0 | MIT | `microsoft.extensions.configuration.binder/8.0.0/microsoft.extensions.configuration.binder.nuspec` |
| Microsoft.Extensions.Configuration.Json | 8.0.0 | MIT | `microsoft.extensions.configuration.json/8.0.0/microsoft.extensions.configuration.json.nuspec` |
| Microsoft.Extensions.DependencyInjection | 8.0.0 | MIT | `microsoft.extensions.dependencyinjection/8.0.0/microsoft.extensions.dependencyinjection.nuspec` |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.0 | MIT | `microsoft.extensions.dependencyinjection.abstractions/8.0.0/microsoft.extensions.dependencyinjection.abstractions.nuspec` |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.8 | MIT | `microsoft.extensions.dependencyinjection.abstractions/9.0.8/microsoft.extensions.dependencyinjection.abstractions.nuspec` |
| Microsoft.Extensions.Hosting | 8.0.0 | MIT | `microsoft.extensions.hosting/8.0.0/microsoft.extensions.hosting.nuspec` |
| Microsoft.Extensions.Hosting.Abstractions | 8.0.0 | MIT | `microsoft.extensions.hosting.abstractions/8.0.0/microsoft.extensions.hosting.abstractions.nuspec` |
| Microsoft.Extensions.Http | 8.0.0 | MIT | `microsoft.extensions.http/8.0.0/microsoft.extensions.http.nuspec` |
| Microsoft.Extensions.Http.Polly | 8.0.0 | MIT | `microsoft.extensions.http.polly/8.0.0/microsoft.extensions.http.polly.nuspec` |
| Microsoft.Extensions.Logging | 8.0.0 | MIT | `microsoft.extensions.logging/8.0.0/microsoft.extensions.logging.nuspec` |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | MIT | `microsoft.extensions.logging.abstractions/8.0.0/microsoft.extensions.logging.abstractions.nuspec` |
| Microsoft.Extensions.Logging.Abstractions | 9.0.8 | MIT | `microsoft.extensions.logging.abstractions/9.0.8/microsoft.extensions.logging.abstractions.nuspec` |
| Microsoft.Extensions.Logging.Console | 8.0.0 | MIT | `microsoft.extensions.logging.console/8.0.0/microsoft.extensions.logging.console.nuspec` |
| Microsoft.Extensions.ObjectPool | 8.0.0 | MIT | `microsoft.extensions.objectpool/8.0.0/microsoft.extensions.objectpool.nuspec` |
| Microsoft.Extensions.Options | 8.0.0 | MIT | `microsoft.extensions.options/8.0.0/microsoft.extensions.options.nuspec` |
| Microsoft.ML.OnnxRuntime | 1.22.0 | MIT | `microsoft.ml.onnxruntime/1.22.0/microsoft.ml.onnxruntime.nuspec` + `LICENSE` |
| Microsoft.ML.Tokenizers | 2.0.0 | MIT | `microsoft.ml.tokenizers/2.0.0/microsoft.ml.tokenizers.nuspec` |
| Microsoft.Windows.CsWinRT | 2.2.0 | MIT | `microsoft.windows.cswinrt/2.2.0/microsoft.windows.cswinrt.nuspec` + `LICENSE` |
| MinVer | 6.0.0 | Apache-2.0 | `minver/6.0.0/minver.nuspec` |
| NetSparkleUpdater.SparkleUpdater | 3.0.4 | MIT | `netsparkleupdater.sparkleupdater/3.0.4/netsparkleupdater.sparkleupdater.nuspec` |
| NetSparkleUpdater.UI.Avalonia | 3.0.4 | MIT | `netsparkleupdater.ui.avalonia/3.0.4/netsparkleupdater.ui.avalonia.nuspec` |
| OpenCvSharp4 | 4.11.0.20250507 | Apache-2.0 | `opencvsharp4/4.11.0.20250507/opencvsharp4.nuspec` |
| OpenCvSharp4.runtime.win | 4.11.0.20250507 | Apache-2.0 | `opencvsharp4.runtime.win/4.11.0.20250507/opencvsharp4.runtime.win.nuspec` |
| ReactiveUI | 20.1.63 | MIT | `reactiveui/20.1.63/reactiveui.nuspec` |
| ReactiveUI.Fody | 19.5.41 | MIT | `reactiveui.fody/19.5.41/reactiveui.fody.nuspec` |
| ReactiveUI.Validation | 4.1.1 | MIT | `reactiveui.validation/4.1.1/reactiveui.validation.nuspec` |
| Sdcb.PaddleInference | 3.0.1 | Apache-2.0 | `sdcb.paddleinference/3.0.1/sdcb.paddleinference.nuspec` |
| Sdcb.PaddleInference.runtime.win64.cu126_cudnn95_sm89 | 3.1.0.54 | 要確認（該当版キャッシュなし） | — |
| Sdcb.PaddleInference.runtime.win64.mkl | 3.1.0.54 | Apache-2.0 | `sdcb.paddleinference.runtime.win64.mkl/3.1.0.54/sdcb.paddleinference.runtime.win64.mkl.nuspec` |
| Sdcb.PaddleOCR | 3.0.1 | Apache-2.0 | `sdcb.paddleocr/3.0.1/sdcb.paddleocr.nuspec` |
| Sdcb.PaddleOCR.Models.Local | 3.0.1 | Apache-2.0 | `sdcb.paddleocr.models.local/3.0.1/sdcb.paddleocr.models.local.nuspec` |
| SharpDX | 4.2.0 | 要確認（nuspecにlicenseUrlのみ） | `sharpdx/4.2.0/sharpdx.nuspec` |
| SharpDX.DXGI | 4.2.0 | 要確認（nuspecにlicenseUrlのみ） | `sharpdx.dxgi/4.2.0/sharpdx.dxgi.nuspec` |
| SharpDX.Direct3D11 | 4.2.0 | 要確認（nuspecにlicenseUrlのみ） | `sharpdx.direct3d11/4.2.0/sharpdx.direct3d11.nuspec` |
| Splat.Microsoft.Extensions.DependencyInjection | 15.0.1 | MIT | `splat.microsoft.extensions.dependencyinjection/15.0.1/splat.microsoft.extensions.dependencyinjection.nuspec` |
| Supabase.Gotrue | 6.0.3 | MIT | `supabase.gotrue/6.0.3/supabase.gotrue.nuspec` |
| System.Diagnostics.PerformanceCounter | 8.0.1 | MIT | `system.diagnostics.performancecounter/8.0.1/system.diagnostics.performancecounter.nuspec` |
| System.Drawing.Common | 8.0.0 | MIT | `system.drawing.common/8.0.0/system.drawing.common.nuspec` |
| System.IO.Hashing | 10.0.1 | MIT | `system.io.hashing/10.0.1/system.io.hashing.nuspec` |
| System.Management | 8.0.0 | MIT | `system.management/8.0.0/system.management.nuspec` |
| System.Numerics.Tensors | 9.0.8 | MIT | `system.numerics.tensors/9.0.8/system.numerics.tensors.nuspec` |
| System.Reactive | 6.0.0 | MIT | `system.reactive/6.0.0/system.reactive.nuspec` |
| System.Threading.Tasks.Dataflow | 8.0.0 | MIT | `system.threading.tasks.dataflow/8.0.0/system.threading.tasks.dataflow.nuspec` |
| postgrest-csharp | 3.5.1 | MIT | `postgrest-csharp/3.5.1/postgrest-csharp.nuspec` |
| supabase-csharp | 0.16.2 | MIT | `supabase-csharp/0.16.2/supabase-csharp.nuspec` |

## 要確認一覧

- NLLBモデルの原本、tokenizerの個別条件、Suryaモデル重みとCUDA版にある改変ライセンス原本の対応、CUDA版コードのGPL原本同梱状況。
- CUDA / cuDNN等の個別ライセンス・版、Python・VC++ランタイム、フォント・ネイティブ内包物と推移依存。
- 公開済みCPU / CUDAアセットとロック・ローカル環境との一致、各LICENSE / NOTICEの同梱状況。
- NuGet Google.Protobuf.Tools 3.32.1: 要確認（nuspecにlicenseUrlのみ）
- NuGet Sdcb.PaddleInference.runtime.win64.cu126_cudnn95_sm89 3.1.0.54: 要確認（該当版キャッシュなし）
- NuGet SharpDX 4.2.0: 要確認（nuspecにlicenseUrlのみ）
- NuGet SharpDX.DXGI 4.2.0: 要確認（nuspecにlicenseUrlのみ）
- NuGet SharpDX.Direct3D11 4.2.0: 要確認（nuspecにlicenseUrlのみ）
