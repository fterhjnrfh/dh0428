$ErrorActionPreference = 'Stop'

$cudaRoot = $env:CUDA_PATH
if ([string]::IsNullOrWhiteSpace($cudaRoot)) {
    $cudaRoot = 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.1'
}

$nvcc = Join-Path $cudaRoot 'bin\nvcc.exe'
if (-not (Test-Path $nvcc)) {
    throw "nvcc.exe was not found under $cudaRoot"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$outputDir = Join-Path $repoRoot 'src\DashCapture.App\native\win-x64'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$source = Join-Path $PSScriptRoot 'DashCapture.CudaFft.cu'
$output = Join-Path $outputDir 'DashCapture.CudaFft.dll'

$vcvarsCandidates = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
)
$vcvars = $vcvarsCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($vcvars)) {
    throw 'Visual Studio vcvars64.bat was not found.'
}

$vcvarsVersion = '14.29'
$command = "`"$vcvars`" -vcvars_ver=$vcvarsVersion && `"$nvcc`" -O3 --shared -Xcompiler `"/MD`" -o `"$output`" `"$source`" -lcufft"
& cmd.exe /c $command
if ($LASTEXITCODE -ne 0) {
    throw "nvcc failed with exit code $LASTEXITCODE"
}

Write-Host "Built $output"
