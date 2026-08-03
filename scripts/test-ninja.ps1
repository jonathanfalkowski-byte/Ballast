$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The .NET Framework C# compiler was not found at $compiler"
}

$sources = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'ninja\Ballast') -Filter '*.cs' |
        ForEach-Object FullName
)
$sources += @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'ninja\test') -Filter '*.cs' |
        ForEach-Object FullName
)

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testExe = Join-Path $tempRoot ("ballast-tests-" + [Guid]::NewGuid().ToString('N') + '.exe')

try {
    & $compiler /nologo /target:exe /out:$testExe `
        /reference:System.dll /reference:System.Core.dll `
        /reference:System.Drawing.dll /reference:System.Net.Http.dll `
        /reference:System.Windows.Forms.dll $sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Push-Location (Join-Path $repoRoot 'ninja')
    try {
        & $testExe
        exit $LASTEXITCODE
    }
    finally { Pop-Location }
}
finally {
    $resolvedExe = [IO.Path]::GetFullPath($testExe)
    if ($resolvedExe.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedExe)) {
        Remove-Item -LiteralPath $resolvedExe -Force
    }
}
