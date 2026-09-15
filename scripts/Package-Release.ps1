[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repositoryRoot 'bbc-cassette-loader.sln'
$buildOutput = Join-Path $repositoryRoot (Join-Path 'bin' $Configuration)
$outputRoot = Join-Path $repositoryRoot $OutputDirectory
$stageRoot = Join-Path $outputRoot ".package-stage-$Configuration"
$packageName = "bbc-cassette-loader-$Configuration"
$packageRoot = Join-Path $stageRoot $packageName
$archivePath = Join-Path $outputRoot "$packageName.zip"
$helpPath = Join-Path $buildOutput 'help.html'

if (-not $SkipBuild) {
    $msbuildCommand = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -eq $msbuildCommand) {
        $knownMsBuild = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'),
            (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($null -eq $knownMsBuild) {
            throw 'MSBuild was not found. Run from a Visual Studio Developer PowerShell or pass -SkipBuild after building.'
        }
        $msbuildPath = $knownMsBuild
    } else {
        $msbuildPath = $msbuildCommand.Source
    }

    & $msbuildPath $solutionPath "/p:Configuration=$Configuration" /m /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $buildOutput -PathType Container)) {
    throw "Build output was not found: $buildOutput"
}
if (-not (Test-Path -LiteralPath $helpPath -PathType Leaf)) {
    throw "Bundled help file was not found in $buildOutput"
}

$runtimeFiles = @(Get-ChildItem -LiteralPath $buildOutput -File | Where-Object {
    $_.Extension -in '.exe', '.config', '.dll'
})
if (-not ($runtimeFiles.Name -contains 'bbc-cassette-loader.exe')) {
    throw "The Release executable was not found in $buildOutput"
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

foreach ($file in $runtimeFiles) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $packageRoot $file.Name)
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NOTICE.txt') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'distribution\README.txt') -Destination $packageRoot
Copy-Item -LiteralPath $helpPath -Destination $packageRoot

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
Remove-Item -LiteralPath $stageRoot -Recurse -Force

Write-Output "Created $archivePath"
