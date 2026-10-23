# Prepares a fresh clone for building: fetches the WolvenKit submodule (update_2.5). Safe to run more than once.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

git -C $root submodule update --init --recursive WolvenKit
if ($LASTEXITCODE -ne 0) { throw 'could not fetch the WolvenKit submodule' }

Write-Host 'WolvenKit submodule is ready.'
Write-Host ''
Write-Host 'Build:   dotnet build CP2077SaveEditor.Wpf -c Release'
Write-Host 'Release: dotnet publish CP2077SaveEditor.Wpf -c Release -r win-x64 -o publish\wpf'
