param(
    [string]$NinjaTraderInstall = (Join-Path $env:ProgramFiles 'NinjaTrader 8'),
    [string]$NinjaTraderUser = (Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8')
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$installBin = Join-Path $NinjaTraderInstall 'bin'
$customBin = Join-Path $NinjaTraderUser 'bin\Custom'

function Find-GacAssembly([string]$name) {
    $assemblyRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
    $match = Get-ChildItem -LiteralPath $assemblyRoot -Recurse -Filter ($name + '.dll') |
        Select-Object -First 1
    if ($null -eq $match) { return (Join-Path $assemblyRoot ($name + '.dll')) }
    return $match.FullName
}

$windowsBase = Find-GacAssembly 'WindowsBase'
$presentationCore = Find-GacAssembly 'PresentationCore'
$presentationFramework = Find-GacAssembly 'PresentationFramework'
$systemXaml = Find-GacAssembly 'System.Xaml'
$required = @(
    $compiler,
    (Join-Path $installBin 'NinjaTrader.Core.dll'),
    (Join-Path $installBin 'NinjaTrader.Gui.dll'),
    (Join-Path $installBin 'SharpDX.dll'),
    (Join-Path $installBin 'SharpDX.Direct2D1.dll'),
    (Join-Path $customBin 'NinjaTrader.Custom.dll'),
    $windowsBase, $presentationCore, $presentationFramework, $systemXaml
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required NinjaTrader compile dependency was not found: $path"
    }
}

$sources = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'ninja\Ballast') -Filter '*.cs' |
        ForEach-Object FullName
)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$output = Join-Path $tempRoot ("ballast-nt8-real-" + [Guid]::NewGuid().ToString('N') + '.dll')
$references = @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Net.Http.dll',
    'System.ComponentModel.DataAnnotations.dll',
    'System.Windows.Forms.dll', 'System.Xml.Linq.dll',
    $windowsBase, $presentationCore, $presentationFramework, $systemXaml,
    (Join-Path $installBin 'NinjaTrader.Core.dll'),
    (Join-Path $installBin 'NinjaTrader.Gui.dll'),
    (Join-Path $installBin 'SharpDX.dll'),
    (Join-Path $installBin 'SharpDX.Direct2D1.dll'),
    (Join-Path $customBin 'NinjaTrader.Custom.dll')
)

try {
    $referenceArgs = @($references | ForEach-Object { '/reference:' + $_ })
    & $compiler /nologo /nowarn:0436 /target:library /out:$output $referenceArgs $sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $artifact = Get-Item -LiteralPath $output
    Write-Output ("Real NinjaTrader compile passed: {0:N0} bytes" -f $artifact.Length)
}
finally {
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    if ($resolvedOutput.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedOutput)) {
        Remove-Item -LiteralPath $resolvedOutput -Force
    }
}
