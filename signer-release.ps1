param(
    [string]$exePath = "publication\TermServMultiScreen.exe",
    [string]$privateKeyPath = "private_key.xml",
    [string]$sigPath = "publication\TermServMultiScreen.exe.sig"
)

if (-not (Test-Path $exePath)) {
    Write-Error "Fichier non trouve: $exePath"
    exit 1
}
if (-not (Test-Path $privateKeyPath)) {
    Write-Error "Cle privee non trouvee: $privateKeyPath. Impossible de signer."
    exit 1
}

$privateKeyXml = Get-Content $privateKeyPath -Raw
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.FromXmlString($privateKeyXml)

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$signature = $rsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

$base64Sig = [Convert]::ToBase64String($signature)
Set-Content -Path $sigPath -Value $base64Sig -Encoding UTF8
Write-Host "Signature RSA generée dans: $sigPath"
