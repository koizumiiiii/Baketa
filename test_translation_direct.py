import sys
import os
import json

# 翻訳するテキスト
text = "……複雑でよくわからない"

# 一時ファイルに書き込み
import tempfile
with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', delete=False, suffix='.txt') as f:
    f.write(text)
    temp_file = f.name

# Pythonスクリプトを実行
import subprocess
python_path = r"C:\Users\suke0\.pyenv\pyenv-win\versions\3.10.9\python.exe"
script_path = r"E:\dev\Baketa\scripts\opus_mt_service.py"

proc = subprocess.Popen(
    [python_path, script_path, f"@{temp_file}"],
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True,
    encoding='utf-8'
)

stdout, stderr = proc.communicate()

print(f"STDOUT: {stdout}")
print(f"STDERR: {stderr}")
print(f"Return code: {proc.returncode}")

# 一時ファイルを削除
os.unlink(temp_file)