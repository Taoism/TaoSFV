# Builds the release files into .\release:
#   TaoSFV-<version>-win-x64.zip        portable: unzip anywhere and run TaoSFV.exe
#   TaoSFV-<version>-setup-win-x64.exe  installer (Inno Setup 6)
# Both are self-contained: the .NET runtime is included, nothing else needs installing.
#
#   .\build-release.ps1 -Version 2.0.0
param(
	[Parameter(Mandatory = $true)][string]$Version,
	[string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$out = Join-Path $PSScriptRoot 'publish\win-x64'
$release = Join-Path $PSScriptRoot 'release'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out, $release | Out-Null

# both apps in one folder share the runtime files
foreach ($project in 'TaoSFV\TaoSFV.csproj', 'TaoSFV.Cli\TaoSFV.Cli.csproj') {
	dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -o $out
	if ($LASTEXITCODE -ne 0) { throw "publish failed: $project" }
}
Copy-Item LICENSE, THIRD-PARTY-NOTICES.txt $out

$zip = Join-Path $release "TaoSFV-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal

& $Iscc /Q "/DAppVersion=$Version" "/DSourceDir=$out" "/O$release" taosfv-setup.iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

Get-ChildItem $release | Where-Object Name -like "*$Version*" | ForEach-Object {
	'{0}  {1:N1} MB  sha256 {2}' -f $_.Name, ($_.Length / 1MB), (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
}
