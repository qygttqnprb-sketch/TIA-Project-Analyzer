$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'TiaAnalyzer.App\bin\x64\Debug'
Add-Type -AssemblyName PresentationFramework
foreach ($name in 'TiaAnalyzer.Core','TiaAnalyzer.Openness','TiaAnalyzer.Export','TiaAnalyzer.App') {
    $extension = if ($name -eq 'TiaAnalyzer.App') { '.exe' } else { '.dll' }
    [void][Reflection.Assembly]::LoadFrom((Join-Path $output ($name + $extension)))
}
$real = (New-Object TiaAnalyzer.Openness.SiemensEnvironmentProbe).Inspect()
$real.Checks | Format-Table Name,Status,Detail -Wrap
if (-not $real.Available) { throw 'Installed V21 environment check failed.' }
$mock = (New-Object TiaAnalyzer.Openness.MockEnvironmentProbe).Inspect()
if (-not $mock.Available -or $mock.Checks.Count -ne 3) { throw 'Mock report failed.' }
$window = New-Object TiaAnalyzer.App.MainWindow
$window.Show()
function Pump-UntilIdle {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $frame = New-Object Windows.Threading.DispatcherFrame
        [void]$window.Dispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false })
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        if ([DateTime]::UtcNow -gt $deadline) { throw 'UI check timed out.' }
    } while (-not $window.FindName('CheckButton').IsEnabled)
}
try {
    Pump-UntilIdle
    if ($window.FindName('Checks').Items.Count -ne 6) { throw 'Real UI results missing.' }
    if ($window.FindName('ConnectButton').IsEnabled) { throw 'Real connection must be disabled.' }
    $window.FindName('MockMode').IsChecked = $true
    Pump-UntilIdle
    if ($window.FindName('Checks').Items.Count -ne 3) { throw 'Mock UI results missing.' }
    $window.FindName('ConnectButton').RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
    if ($window.FindName('SessionStatus').Text -notlike 'MOCK connesso*') { throw 'Mock connection failed.' }
    $window.FindName('MockMode').IsChecked = $false
    Pump-UntilIdle
    if ($window.FindName('SessionStatus').Text -ne 'Disconnesso') { throw 'Session was not reset.' }
    Write-Output 'PASS: real metadata, WPF startup, mock switch, mock connection, session reset.'
} finally { $window.Close() }
