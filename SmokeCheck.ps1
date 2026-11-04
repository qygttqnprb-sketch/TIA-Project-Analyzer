param([switch]$RequireSiemens)
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'TiaAnalyzer.App\bin\x64\Debug'
Add-Type -AssemblyName PresentationFramework
foreach ($name in 'TiaAnalyzer.Core','TiaAnalyzer.Openness','TiaAnalyzer.Export','TiaAnalyzer.App') {
    $extension = if ($name -eq 'TiaAnalyzer.App') { '.exe' } else { '.dll' }
    [void][Reflection.Assembly]::LoadFrom((Join-Path $output ($name + $extension)))
}
$real = (New-Object TiaAnalyzer.Openness.SiemensEnvironmentProbe).Inspect()
$real.Checks | Format-Table Name,Status,Detail -Wrap
if ($RequireSiemens -and -not $real.Available) { throw 'Installed V21 metadata check failed.' }
[TiaAnalyzer.Openness.OpennessRuntime]::Configure()
$window = New-Object TiaAnalyzer.App.MainWindow
$exportCheckRoot = Join-Path ([IO.Path]::GetTempPath()) ('tia-ui-check-' + [Guid]::NewGuid().ToString('N'))
$window.Show()
function Pump-UntilIdle {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $frame = New-Object Windows.Threading.DispatcherFrame
        [void]$window.Dispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false })
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        if ([DateTime]::UtcNow -gt $deadline) { throw 'UI check timed out.' }
    } while ($window.DataContext.IsBusy -or -not $window.FindName('CheckButton').IsEnabled)
}
try {
    Pump-UntilIdle
    if ($window.FindName('Checks').Items.Count -ne 7) { throw 'Real diagnostics binding failed.' }
    if ($window.FindName('ConnectButton').IsEnabled) { throw 'No automatic selection or real connection permitted.' }
    $window.FindName('MockMode').IsChecked = $true
    Pump-UntilIdle
    if (-not $window.DataContext.IsDemo) { throw 'Mode binding failed.' }
    if ($window.FindName('Checks').Items.Count -ne 3) { throw 'Demo diagnostic binding failed.' }
    $window.FindName('ScanButton').Command.Execute($null)
    Pump-UntilIdle
    if ($window.FindName('InstancesList').Items.Count -ne 1) { throw 'Demo discovery failed.' }
    if ($window.FindName('ConnectButton').IsEnabled) { throw 'Instance selection must be explicit.' }
    $window.FindName('InstancesList').SelectedIndex = 0
    Pump-UntilIdle
    if (-not $window.FindName('ConnectButton').IsEnabled) { throw 'Demo connection must be enabled after selection.' }
    $window.FindName('ConnectButton').Command.Execute($null)
    Pump-UntilIdle
    if ($window.FindName('SessionStatus').Text -notlike 'MOCK connesso*') { throw 'Demo session binding failed.' }
    if ($window.FindName('ComponentsList').Items.Count -ne 6) { throw 'Synthetic components binding failed.' }
    $window.DataContext.ExportRoot = $exportCheckRoot
    if (-not $window.FindName('ExportXmlButton').IsEnabled) { throw 'Connected demo XML export is disabled.' }
    $window.FindName('ExportXmlButton').Command.Execute($null)
    Pump-UntilIdle
    $manifest = Join-Path $window.DataContext.LastExportDirectory 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifest)) { throw 'Demo export did not produce a manifest.' }
    $snapshot = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if (-not $snapshot.IsDemo -or $snapshot.Artifacts.Count -ne 6) { throw 'Demo export manifest provenance failed.' }
    $window.FindName('DisconnectButton').Command.Execute($null)
    Pump-UntilIdle
    if ($window.FindName('ComponentsList').Items.Count -ne 0) { throw 'Disconnect did not clear components.' }
    $window.FindName('ConnectButton').Command.Execute($null)
    Pump-UntilIdle
    $window.FindName('MockMode').IsChecked = $false
    Pump-UntilIdle
    if ($window.FindName('SessionStatus').Text -ne 'Disconnesso') { throw 'Session was not reset.' }
    if ($window.FindName('ComponentsList').Items.Count -ne 0) { throw 'Mode change left stale synthetic data.' }
    if ($window.FindName('ConnectButton').IsEnabled) { throw 'Real connection enabled after mode change.' }
    Write-Output 'PASS: diagnostics bindings, WPF startup, explicit demo discovery/selection, synthetic XML export, disconnect and mode reset.'
    Write-Output 'Siemens integration NOT validated. Optional metadata check does not establish API access.'
} finally {
    $window.Close()
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ($window.IsVisible -and [DateTime]::UtcNow -lt $deadline) {
        $frame = New-Object Windows.Threading.DispatcherFrame
        [void]$window.Dispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false })
        [Windows.Threading.Dispatcher]::PushFrame($frame)
    }
    if ($window.IsVisible) { throw 'Window cleanup timed out.' }
    $resolvedExportCheck = [IO.Path]::GetFullPath($exportCheckRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedExportCheck.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedExportCheck) -notlike 'tia-ui-check-*') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolvedExportCheck) { Remove-Item -LiteralPath $resolvedExportCheck -Recurse -Force }
}
