param(
    [string]$AppPath = (Join-Path $PSScriptRoot '../ZZZBuffTracker.App/bin/Debug/net10.0-windows10.0.19041.0/ZZZBuffTracker.App.exe')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TrackerSmokeNative {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public static IntPtr FindOverlay(uint processId) { return FindNamedWindow(processId, "ZZZ Buff Tracker Overlay"); }
    public static IntPtr FindNamedWindow(uint processId, string name) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            uint id; GetWindowThreadProcessId(hwnd, out id);
            var title = new System.Text.StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
            if (id == processId && title.ToString() == name) { result = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

function Wait-For([scriptblock]$Condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for WPF condition: $Condition"
}

function Find-Element([string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $script:rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-Id([string]$Id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $script:rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Id([string]$Id) {
    $element = Find-Id $Id
    if ($null -eq $element) { throw "Missing control: $Id" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Select-Item([string]$Id, [string]$Name) {
    $combo = Find-Id $Id
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $item = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $item) { throw "Missing option: $Name" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
}
function Assert-PlayStyle([IntPtr]$Handle, [bool]$Play) {
    $style = [TrackerSmokeNative]::GetWindowLong($Handle, -20)
    $mask = 0x08000020
    if ($Play -and (($style -band $mask) -ne $mask)) { throw 'Play mode lacks transparent/no-activate flags.' }
    if (-not $Play -and (($style -band $mask) -ne 0)) { throw 'Edit mode still has Play flags.' }
    if (($style -band 0x00080088) -ne 0x00080088) { throw 'Overlay must be layered, topmost and a tool window.' }
}

$dataDirectory = Join-Path $PSScriptRoot ('../artifacts/smoke-' + [Guid]::NewGuid().ToString('N'))
$dataDirectory = [IO.Path]::GetFullPath($dataDirectory)
$arguments = '--data-dir "' + $dataDirectory + '"'
$appProcess = Start-Process -FilePath (Resolve-Path -LiteralPath $AppPath).Path -ArgumentList $arguments -PassThru -WindowStyle Hidden
try {
    Wait-For { $appProcess.Refresh(); $appProcess.MainWindowHandle -ne [IntPtr]::Zero }
    $script:rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    $panelHandle = $appProcess.MainWindowHandle
    Write-Output 'Smoke: startup and window styles'
    Wait-For { [TrackerSmokeNative]::FindOverlay($appProcess.Id) -ne [IntPtr]::Zero }
    $overlayHandle = [TrackerSmokeNative]::FindOverlay($appProcess.Id)
    Assert-PlayStyle $overlayHandle $true
    Write-Output 'Smoke: Phase 05 capture controls'
    $dashboardRoot = $script:rootElement
    Invoke-Id 'OpenCapture'
    Wait-For { [TrackerSmokeNative]::FindNamedWindow($appProcess.Id, 'Capture / Detection') -ne [IntPtr]::Zero }
    $captureHandle = [TrackerSmokeNative]::FindNamedWindow($appProcess.Id, 'Capture / Detection')
    $script:rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($captureHandle)
    if ($null -eq (Find-Id 'PickCapture')) { throw 'Missing WGC picker control.' }
    [void][TrackerSmokeNative]::PostMessage($captureHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    Wait-For { [TrackerSmokeNative]::FindNamedWindow($appProcess.Id, 'Capture / Detection') -eq [IntPtr]::Zero }
    $script:rootElement = $dashboardRoot
    Write-Output 'Smoke: tracking and shared snapshot'
    Invoke-Id 'StartTracking'
    Wait-For { $null -ne (Find-Element 'Inactive') }
    Invoke-Id 'TriggerBuff'
    Wait-For { $null -ne (Find-Element 'Active') }
    # Dispatch the same message Windows sends for the registered reset hotkey.
    [void][TrackerSmokeNative]::PostMessage($appProcess.MainWindowHandle, 0x0312, [IntPtr]3, [IntPtr]::Zero)
    Wait-For { $null -ne (Find-Element 'Inactive') }
    Invoke-Id 'TriggerBuff'
    Wait-For { $null -ne (Find-Element 'Active') }
    Wait-For { $null -ne (Find-Element 'Expired') }
    Write-Output 'Smoke: Phase 03 rule actions through the real pipeline'
    (Find-Id 'RuleEngineDemo').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Invoke-Id 'PendingBuff'
    Wait-For { $null -ne (Find-Element 'Pending') }
    Invoke-Id 'TriggerBuff'
    Wait-For { $null -ne (Find-Element 'Active') }
    Invoke-Id 'StackBuff'
    Wait-For { $null -ne (Find-Element 'Stack: 2') }
    Invoke-Id 'StackBuff'
    Wait-For { $null -ne (Find-Element 'Stack: 3') }
    Invoke-Id 'LoseSignal'
    Wait-For { $null -ne (Find-Element 'Unknown') }
    Invoke-Id 'RefreshBuff'
    Wait-For { $null -ne (Find-Element 'Active') }
    if ($null -eq (Find-Element 'Stack: 3')) { throw 'Refresh must preserve stacks.' }
    Invoke-Id 'DeactivateBuff'
    Wait-For { $null -ne (Find-Element 'Inactive') }
    Invoke-Id 'ExpireBuff'
    Wait-For { $null -ne (Find-Element 'Expired') }
    (Find-Id 'RuleEngineDemo').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    (Find-Id 'PreviewSamples').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Wait-For { $null -ne (Find-Element 'Unknown') }
    if ($null -eq (Find-Element '?')) { throw 'Unknown should show a question mark.' }
    $overlayRoot = [System.Windows.Automation.AutomationElement]::FromHandle($overlayHandle)
    $unknownCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Unknown')
    if ($null -eq $overlayRoot.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $unknownCondition)) {
        throw 'Overlay did not share the preview snapshot.'
    }
    (Find-Id 'HideInactive').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Write-Output 'Smoke: live display settings'
    Wait-For { $null -eq (Find-Element 'Inactive') }
    Select-Item 'DisplayMode' 'Text'
    Select-Item 'DisplayMode' 'Image'
    Select-Item 'DisplayMode' 'Hybrid'
    Select-Item 'OverlayOrientation' 'Horizontal'
    (Find-Id 'OverlayScale').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(1.25)
    (Find-Id 'OverlayOpacity').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(0.7)
    (Find-Id 'OverlaySpacing').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(16)
    [void][TrackerSmokeNative]::PostMessage($appProcess.MainWindowHandle, 0x0312, [IntPtr]2, [IntPtr]::Zero)
    Wait-For { ([TrackerSmokeNative]::GetWindowLong($overlayHandle, -20) -band 0x08000020) -eq 0 }
    Assert-PlayStyle $overlayHandle $false
    [void][TrackerSmokeNative]::SetWindowPos($overlayHandle, [IntPtr]::Zero, 120, 140, 720, 340, 0x0014)
    Invoke-Id 'SaveOverlay'
    Write-Output 'Smoke: save, Play mode, visibility and focus'
    Wait-For { Test-Path -LiteralPath (Join-Path $dataDirectory 'profiles.json') }
    Wait-For { ([TrackerSmokeNative]::GetWindowLong($overlayHandle, -20) -band 0x08000020) -eq 0x08000020 }
    Assert-PlayStyle $overlayHandle $true
    $saved = (Get-Content -LiteralPath (Join-Path $dataDirectory 'profiles.json') -Raw | ConvertFrom-Json).Overlay
    if ($saved.Scale -ne 1.25 -or $saved.Opacity -ne 0.7 -or $saved.Spacing -ne 16 -or $saved.Orientation -ne 'Horizontal') {
        throw 'Live settings were not persisted correctly.'
    }
    [void][TrackerSmokeNative]::PostMessage($appProcess.MainWindowHandle, 0x0312, [IntPtr]1, [IntPtr]::Zero)
    Wait-For { -not [TrackerSmokeNative]::IsWindowVisible($overlayHandle) }
    $foreground = [TrackerSmokeNative]::GetForegroundWindow()
    [void][TrackerSmokeNative]::PostMessage($appProcess.MainWindowHandle, 0x0312, [IntPtr]1, [IntPtr]::Zero)
    Wait-For { [TrackerSmokeNative]::IsWindowVisible($overlayHandle) }
    if ([TrackerSmokeNative]::GetForegroundWindow() -ne $foreground) { throw 'Showing Play overlay stole foreground focus.' }
    Write-Output 'Smoke: Phase 04 preset editor, persistence and manual hotkeys'
    # Disable static samples before testing the real selected preset.
    (Find-Id 'PreviewSamples').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-Id 'HideInactive').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $dashboardRoot = $script:rootElement
    Invoke-Id 'OpenProfiles'
    Wait-For { [TrackerSmokeNative]::FindNamedWindow($appProcess.Id, 'ZZZ Presets / Profiles') -ne [IntPtr]::Zero }
    $script:rootElement = [System.Windows.Automation.AutomationElement]::FromHandle([TrackerSmokeNative]::FindNamedWindow($appProcess.Id, 'ZZZ Presets / Profiles'))
    $profileRoot = $script:rootElement
    Write-Output 'Smoke: profile window ready, creating preset'
    Invoke-Id 'NewPreset'
    (Find-Id 'CharacterId').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('smoke-character')
    (Find-Id 'CharacterName').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Smoke character')
    Invoke-Id 'SavePreset'
    Wait-For { @((Get-Content -LiteralPath (Join-Path $dataDirectory 'profiles.json') -Raw | ConvertFrom-Json).Presets | Where-Object CharacterId -eq 'smoke-character').Count -eq 1 }
    Write-Output 'Smoke: preset created, updating'
    (Find-Id 'CharacterName').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Updated smoke character')
    Invoke-Id 'SavePreset'
    Wait-For { ((Get-Content -LiteralPath (Join-Path $dataDirectory 'profiles.json') -Raw | ConvertFrom-Json).Presets | Where-Object CharacterId -eq 'smoke-character').Version -eq 2 }
    Invoke-Id 'ApplyCharacter'
    $profileRoot.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $script:rootElement = $dashboardRoot
    Wait-For { $null -ne (Find-Element 'Inactive') }
    [void][TrackerSmokeNative]::PostMessage($panelHandle, 0x0312, [IntPtr]6, [IntPtr]::Zero)
    [void][TrackerSmokeNative]::PostMessage($panelHandle, 0x0312, [IntPtr]4, [IntPtr]::Zero)
    Wait-For { $null -ne (Find-Element 'Active') }
    [void][TrackerSmokeNative]::PostMessage($panelHandle, 0x0312, [IntPtr]5, [IntPtr]::Zero)
    [void][TrackerSmokeNative]::PostMessage($panelHandle, 0x0312, [IntPtr]3, [IntPtr]::Zero)
    Wait-For { $null -ne (Find-Element 'Inactive') }
    [void][TrackerSmokeNative]::PostMessage($panelHandle, 0x0312, [IntPtr]6, [IntPtr]::Zero)
    # WM_CLOSE works for a hidden window and exercises async shutdown while tracking.
    if (-not [TrackerSmokeNative]::PostMessage($appProcess.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not request window close.'
    }
    if (-not $appProcess.WaitForExit(10000)) { throw 'Application did not shut down cleanly.' }
    if ($appProcess.ExitCode -ne 0) { throw "Application exited with $($appProcess.ExitCode)." }
    foreach ($key in @(0x4F, 0x45, 0x52, 0x54, 0x46, 0x44)) {
        if (-not [TrackerSmokeNative]::RegisterHotKey([IntPtr]::Zero, 100, 0x4003, $key)) { throw 'Hotkey not released after shutdown (or occupied externally).' }
        [void][TrackerSmokeNative]::UnregisterHotKey([IntPtr]::Zero, 100)
    }
    $appProcess.Dispose()
    Write-Output 'Smoke: restart and restore'
    if (-not [TrackerSmokeNative]::RegisterHotKey([IntPtr]::Zero, 101, 0x4003, 0x54)) { throw 'Cannot reserve manual hotkey for conflict test.' }
    $appProcess = Start-Process -FilePath (Resolve-Path -LiteralPath $AppPath).Path -ArgumentList $arguments -PassThru -WindowStyle Hidden
    Wait-For { $appProcess.Refresh(); $appProcess.MainWindowHandle -ne [IntPtr]::Zero }
    $script:rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    $panelHandle = $appProcess.MainWindowHandle
    Wait-For { [TrackerSmokeNative]::FindOverlay($appProcess.Id) -ne [IntPtr]::Zero }
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $messages = $script:rootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
    $conflictReported = $false
    foreach ($element in $messages) { if ($element.Current.Name -like '*Ctrl+Alt+T*Control Panel.*') { $conflictReported = $true } }
    if (-not $conflictReported) { throw 'Hotkey conflict was not reported in the UI.' }
    $restoredScale = (Find-Id 'OverlayScale').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
    if ($restoredScale -ne 1.25) { throw 'Settings were not restored after restart.' }
    Invoke-Id 'SaveOverlay'
    Wait-For { Test-Path -LiteralPath (Join-Path $dataDirectory 'profiles.json.bak') }
    $restored = (Get-Content -LiteralPath (Join-Path $dataDirectory 'profiles.json') -Raw | ConvertFrom-Json).Overlay
    foreach ($field in @('Left', 'Top', 'Width', 'Height')) {
        if ([Math]::Abs($saved.Geometry.$field - $restored.Geometry.$field) -gt 2) { throw "Geometry not restored: $field" }
    }
    Write-Output 'Smoke: closing restarted panel'
    if (-not [TrackerSmokeNative]::PostMessage($panelHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Failed to post close to restarted panel.' }
    if (-not $appProcess.WaitForExit(10000)) { throw 'Restarted app shutdown timed out.' }
    if ($appProcess.ExitCode -ne 0) { throw "Restarted app exit code: $($appProcess.ExitCode)" }
    Write-Output 'PASS: tracking/rules, shared overlay, Play/Edit, profile editor create/update/apply, persistence, manual hotkeys while paused, six-hotkey cleanup, focus, geometry reload and shutdown.'
    Write-Output "Artifacts: $dataDirectory"
}
finally {
    [void][TrackerSmokeNative]::UnregisterHotKey([IntPtr]::Zero, 101)
    if (-not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id }
    $appProcess.Dispose()
}
