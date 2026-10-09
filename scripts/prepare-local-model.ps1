# Build-time only. End users receive these verified files inside the installer.
$ErrorActionPreference = 'Stop'
$destination = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Wyspa.App\Tools\LocalDefault'
New-Item -ItemType Directory -Force $destination | Out-Null
$source = 'https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17/resolve/d42f2d9f7ca24806fb667456a18a9f1b60f70d16'
$files = @(
    @('encoder-epoch-99-avg-1.int8.onnx', '3810755ce7c3ab26b42a8bcf39d191308fa27fb0f53358823ba46141d03b7eb3'),
    @('decoder-epoch-99-avg-1.onnx', '45a7f940ecfb53d89fa270ad11b88b961e53a317203eb24b1c8e95ed208b0f30'),
    @('joiner-epoch-99-avg-1.int8.onnx', 'e085d73b593cf9b0707f370dbd656d58327d3fe36d80d849202ef81df02cb01e'),
    @('tokens.txt', '49e3c2646595fd907228b3c6787069658f67b17377c60aeb8619c4551b2316fb')
)
foreach ($entry in $files) {
    $target = Join-Path $destination $entry[0]
    if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash -eq $entry[1]) { continue }
    $temporary = "$target.partial"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$source/$($entry[0])" -OutFile $temporary
        if ((Get-FileHash $temporary -Algorithm SHA256).Hash -ne $entry[1]) { throw "Default model checksum failed: $($entry[0])" }
        Move-Item -Force $temporary $target
    } finally { if (Test-Path $temporary) { Remove-Item $temporary -Force } }
}
