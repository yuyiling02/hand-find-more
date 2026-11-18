param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
& $Python -c "import sys; assert sys.version_info >= (3, 10), 'Python 3.10+ required; pass -Python with its executable path'"
if ($LASTEXITCODE -ne 0) { throw '需要 Python 3.10 或更高版本' }
& $Python -m venv .venv
if ($LASTEXITCODE -ne 0) { throw '创建环境失败' }
$runtime = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
& $runtime -m pip install numpy==1.26.4 opencv-python==4.10.0.84
if ($LASTEXITCODE -ne 0) { throw '安装图像依赖失败' }
& $runtime -m pip install torch==2.5.1+cu124 torchvision==0.20.1+cu124 --index-url https://download.pytorch.org/whl/cu124 --retries 1
if ($LASTEXITCODE -ne 0) { throw '安装依赖失败' }
if (-not (Test-Path vendor)) { git clone https://github.com/DepthAnything/Depth-Anything-V2.git vendor }
if ($LASTEXITCODE -ne 0) { throw '下载源码失败' }
git -C vendor checkout a561b849ebae10a6f5ef49e26c83cbbcd36c71bf
if ($LASTEXITCODE -ne 0) { throw '固定源码版本失败' }
& $runtime download_model.py
if ($LASTEXITCODE -ne 0) { throw '下载模型失败' }
& $runtime -c "import torch; assert torch.cuda.is_available(), 'CUDA unavailable'; print(torch.cuda.get_device_name())"
if ($LASTEXITCODE -ne 0) { throw 'CUDA 检查失败' }
