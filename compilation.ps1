param([switch]$SkipSignature)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$project = Join-Path $PSScriptRoot 'app\TermServMultiScreen.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
$out = Join-Path $PSScriptRoot "publication\$version"
New-Item -ItemType Directory -Force $out | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw 'La publication a echoue.' }
$wix = Join-Path $PSScriptRoot '.tools\wix.exe'
if (!(Test-Path $wix)) {
    dotnet tool install wix --tool-path (Join-Path $PSScriptRoot '.tools') --version 6.0.2
    if ($LASTEXITCODE) { throw 'Installation de WiX impossible.' }
}
& $wix build installer\FlowDesk.wxs -arch x64 -d "Version=$version" -d "PublishDir=$out" `
    -d "ProjectDir=$PSScriptRoot" -o "$out\FlowDesk-$version-x64.msi" -pdbtype none
if ($LASTEXITCODE) { throw 'La creation du MSI a echoue.' }
& $wix msi validate "$out\FlowDesk-$version-x64.msi"
if ($LASTEXITCODE) { throw 'La validation du MSI a echoue.' }
if (!$SkipSignature -and (Test-Path 'private_key.xml')) {
    & "$PSScriptRoot\signer-release.ps1" -exePath "$out\FlowDesk.exe" -sigPath "$out\FlowDesk.exe.sig"
    if ($LASTEXITCODE) { throw 'La signature a echoue.' }
} else {
    Write-Warning 'Executable sans signature RSA : mise a jour automatique indisponible pour cette release.'
}
Get-FileHash "$out\FlowDesk.exe", "$out\FlowDesk-$version-x64.msi" -Algorithm SHA256 |
    ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf) } |
    Set-Content "$out\SHA256SUMS.txt" -Encoding ascii
Write-Host "Release $version disponible : $out"
