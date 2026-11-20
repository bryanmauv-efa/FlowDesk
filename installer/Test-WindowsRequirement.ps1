param([Parameter(Mandatory)][string]$MsiPath)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$installer.UILevel = 2
# Open a session and run only registry detection, never installation actions.
$session = $installer.OpenPackage((Resolve-Path -LiteralPath $MsiPath).Path, 1)
try {
    $legacyBuild = $session.Property('WindowsBuild')
    if ($session.DoAction('AppSearch') -ne 1) { throw 'AppSearch failed.' }
    $detectedBuild = $session.Property('WINDOWSBUILDNUMBER')
    $expectedBuild = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    if ($detectedBuild -ne $expectedBuild) { throw "Incorrect build detection: $detectedBuild vs $expectedBuild" }
    $database = $installer.OpenDatabase((Resolve-Path -LiteralPath $MsiPath).Path, 0)
    $view = $database.OpenView('SELECT `Condition` FROM `LaunchCondition`')
    $view.Execute()
    $condition = $null
    while ($record = $view.Fetch()) {
        if ($record.StringData(1) -match 'WINDOWSBUILDNUMBER') { $condition = $record.StringData(1) }
    }
    $view.Close()
    if (!$condition) { throw 'Windows build condition missing from MSI.' }
    $session.Property('Installed') = ''
    if ($session.EvaluateCondition($condition) -ne 1) { throw 'Current Windows build rejected.' }
    Write-Host "Current Windows accepted: actual=$detectedBuild; legacy MSI WindowsBuild=$legacyBuild"
    foreach ($case in @(@('9600', 0), @('18363', 0), @('19041', 1), @('22000', 1), @('26100', 1), @('', 0))) {
        $session.Property('WINDOWSBUILDNUMBER') = $case[0]
        if ($session.EvaluateCondition($condition) -ne $case[1]) { throw "Unexpected result for build '$($case[0])'." }
        Write-Host "OK: build '$($case[0])', accepted=$($case[1])"
    }
    $session.Property('Installed') = '1'
    if ($session.EvaluateCondition($condition) -ne 1) { throw 'Maintenance/uninstall blocked.' }
    Write-Host 'OK: maintenance/uninstall remains available.'
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
