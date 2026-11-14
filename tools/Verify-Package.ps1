param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$workspaceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$verificationRoot = Join-Path $workspaceRoot ('.tools/verification-' + [Guid]::NewGuid().ToString('N'))
$settingsRoot = Join-Path $verificationRoot 'settings'
New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
Set-Content -LiteralPath (Join-Path $settingsRoot 'game-theme.json') -Value '"Terminal"' -Encoding utf8
# Use an unconfigured ADB source so this packaging check cannot attach to a game.
Set-Content -LiteralPath (Join-Path $settingsRoot 'game-connection.json') -Value '{"Source":"Adb","AdbPath":"","AdbSerial":"","WindowTitle":"HongxueAssistant packaging verification"}' -Encoding utf8

foreach ($file in @(
    'HongxueAssistant.exe', 'HongxueAssistant.dll', 'HongxueAssistant.Core.dll',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll',
    'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll',
    'LICENSE', 'LICENSE.upstream', 'NOTICE',
    'data\operators.json', 'data\equipment.json', 'data\covenants.json',
    'data\model\ocr\det.onnx', 'data\model\ocr\rec.onnx', 'data\model\ocr\keys.txt',
    'data\maa_resource\pipeline\empty.json', 'data\maa_resource\image\op_card.png',
    'runtimes\win-x64\native\MaaFramework.dll',
    'runtimes\win-x64\native\MaaToolkit.dll',
    'runtimes\win-x64\native\MaaAdbControlUnit.dll',
    'runtimes\win-x64\native\onnxruntime_maa.dll'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $file) -PathType Leaf)) {
        throw "Missing package file: $file"
    }
}

foreach ($relativeModel in @('data\model\ocr\det.onnx', 'data\model\ocr\rec.onnx', 'data\model\ocr\keys.txt')) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $workspaceRoot $relativeModel) -Algorithm SHA256).Hash
    $packageHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot $relativeModel) -Algorithm SHA256).Hash
    if ($sourceHash -ne $packageHash) { throw "Model checksum mismatch: $relativeModel" }
}

$statusDirectory = Join-Path ([IO.Path]::GetTempPath()) ('HongxueAssistant-Update-Smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $statusDirectory | Out-Null
$statusPath = Join-Path $statusDirectory ('startup-' + [Guid]::NewGuid().ToString('N') + '.status')
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = Join-Path $packageRoot 'HongxueAssistant.exe'
$startInfo.WorkingDirectory = $packageRoot
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.Environment['HONGXUEASSISTANT_SETTINGS_DIR'] = $settingsRoot
$startInfo.Environment['HONGXUEASSISTANT_UPDATE_STARTUP_RESULT'] = $statusPath
# The smoke check needs no privileged access. Keep the distributed manifest intact.
$startInfo.Environment['__COMPAT_LAYER'] = 'RunAsInvoker'
# An executable that requires an installed .NET runtime will fail with this empty root.
$emptyRuntimeRoot = Join-Path $verificationRoot 'empty-dotnet'
New-Item -ItemType Directory -Path $emptyRuntimeRoot | Out-Null
$startInfo.Environment['DOTNET_ROOT'] = $emptyRuntimeRoot
$startInfo.Environment['DOTNET_ROOT_X64'] = $emptyRuntimeRoot
$startInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'

$appProcess = $null
try {
    if (Get-Process -Name 'HongxueAssistant','ASPAssistant.App' -ErrorAction SilentlyContinue) {
        throw 'An HongxueAssistant instance is already running; cannot safely test the single-instance application.'
    }
    $appProcess = [Diagnostics.Process]::Start($startInfo)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath $statusPath)) {
        if ($appProcess.HasExited) { throw "Application exited before reporting ready: $($appProcess.ExitCode)" }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Application startup timed out after 45 seconds.' }
        Start-Sleep -Milliseconds 250
    }
    $status = (Get-Content -LiteralPath $statusPath -Raw).Trim()
    if ($status -ne 'ready') { throw "Application startup failed: $status" }
    Start-Sleep -Seconds 3
    if ($appProcess.HasExited) { throw "Application exited after startup: $($appProcess.ExitCode)" }
    $appProcess.Refresh()
    $runtimeModule = $appProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1
    if (-not $runtimeModule -or -not $runtimeModule.FileName.StartsWith($packageRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Application did not load the bundled .NET runtime.'
    }
    $nativeModules = @($appProcess.Modules | Where-Object { $_.ModuleName -match '^(MaaFramework|MaaToolkit|onnxruntime_maa)\.dll$' } | ForEach-Object FileName)
    if ($nativeModules.Count -lt 3) { throw 'MaaFramework or OCR native dependencies were not loaded.' }
    $result = [ordered]@{
        Status = 'passed'
        PackageDirectory = $packageRoot
        StartupStatus = $status
        Execution = 'Original EXE with RunAsInvoker for the isolated startup check'
        MainWindowTitle = $appProcess.MainWindowTitle
        RuntimeModule = $runtimeModule.FileName
        NativeModules = $nativeModules
        VerifiedUtc = [DateTime]::UtcNow.ToString('o')
        Limits = 'Startup and packaged dependencies verified; no live MuMu connection or game recognition was exercised.'
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $verificationRoot 'result.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 5
}
finally {
    if ($appProcess) {
        if (-not $appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
        $appProcess.Dispose()
    }
    if (Test-Path -LiteralPath $statusPath) {
        Copy-Item -LiteralPath $statusPath -Destination (Join-Path $verificationRoot 'startup.status')
    }
}
