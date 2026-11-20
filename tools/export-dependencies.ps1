param(
    [string]$AssetsPath = 'app/obj/project.assets.json',
    [string]$OutputDirectory = 'publication/compliance'
)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$licenseRoot = Join-Path $OutputDirectory 'dependency-licenses'
New-Item -ItemType Directory -Force $licenseRoot | Out-Null
$records = foreach ($entry in $assets.libraries.PSObject.Properties) {
    if ($entry.Value.type -ne 'package') { continue }
    $packageFolder = $null
    foreach ($root in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $root $entry.Value.path
        if (Test-Path -LiteralPath $candidate) { $packageFolder = $candidate; break }
    }
    if (!$packageFolder) { throw "Package introuvable : $($entry.Name)" }
    $nuspec = Get-ChildItem -LiteralPath $packageFolder -Filter '*.nuspec' | Select-Object -First 1
    if (!$nuspec) { throw "Nuspec introuvable : $($entry.Name)" }
    $metadata = ([xml](Get-Content -LiteralPath $nuspec.FullName -Raw)).package.metadata
    $licenseType = [string]$metadata.license.type
    $licenseValue = [string]$metadata.license.InnerText
    $destination = Join-Path $licenseRoot ($entry.Name.Replace('/', '-'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination
    $files = @(Get-ChildItem -LiteralPath $packageFolder -File | Where-Object {
        $_.Name -match '^(license|licence|notice|third.?party)'
    })
    if ($licenseType -eq 'file') {
        $declared = Join-Path $packageFolder $licenseValue
        if (Test-Path -LiteralPath $declared -PathType Leaf) { $files += Get-Item -LiteralPath $declared }
    }
    $files = @($files | Sort-Object FullName -Unique)
    foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination $destination }
    [ordered]@{
        package = $entry.Name
        licenseType = $licenseType
        license = $licenseValue
        licenseUrl = [string]$metadata.licenseUrl
        projectUrl = [string]$metadata.projectUrl
        notices = @($files.Name)
        reviewRequired = ($licenseType -ne 'expression' -or $files.Count -eq 0)
    }
}
ConvertTo-Json -InputObject @($records) -Depth 6 |
    Set-Content (Join-Path $OutputDirectory 'dependencies.json') -Encoding utf8
Write-Host "Inventaire : $($records.Count) paquets ; textes tiers copies sans modification."
