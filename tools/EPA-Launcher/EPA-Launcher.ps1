Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workerScript = Join-Path $PSScriptRoot 'EPA-Launcher.Worker.ps1'
$stateRoot = Join-Path $env:LOCALAPPDATA 'EngineeringPerformance\Launcher'
$statusFile = Join-Path $stateRoot 'status.json'
$logFile = Join-Path $stateRoot 'launcher.log'
New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Rectangle, [single]$Radius)
    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-TactileBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [System.Drawing.Color]$Top,
        [System.Drawing.Color]$Bottom,
        [bool]$Pressed = $false
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $outerRect = [System.Drawing.RectangleF]::new(3, 3, $Width - 6, $Height - 6)
    $outerPath = New-RoundedPath $outerRect 20
    $outerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(22, 25, 22))
    $g.FillPath($outerBrush, $outerPath)

    $bezelRect = [System.Drawing.RectangleF]::new(10, 10, $Width - 20, $Height - 20)
    $bezelPath = New-RoundedPath $bezelRect 16
    $bezelBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bezelRect,
        [System.Drawing.Color]::FromArgb(115, 111, 100),
        [System.Drawing.Color]::FromArgb(35, 38, 34),
        [single]90
    )
    $g.FillPath($bezelBrush, $bezelPath)

    $offset = if ($Pressed) { 7 } else { 0 }
    $faceRect = [System.Drawing.RectangleF]::new(18, 18 + $offset, $Width - 36, $Height - 43)
    $facePath = New-RoundedPath $faceRect 12
    $faceBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($faceRect, $Top, $Bottom, [single]90)
    $g.FillPath($faceBrush, $facePath)

    $highlightAlpha = if ($Pressed) { 55 } else { 190 }
    $highlightPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb($highlightAlpha, 255, 255, 255), 2)
    $g.DrawPath($highlightPen, $facePath)

    if (-not $Pressed) {
        $shadowRect = [System.Drawing.RectangleF]::new(24, $Height - 29, $Width - 48, 8)
        $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
        $g.FillEllipse($shadowBrush, $shadowRect)
        $shadowBrush.Dispose()
    }

    foreach ($x in @(29, ($Width - 29))) {
        foreach ($y in @(29, ($Height - 29))) {
            $screwOuter = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(65, 67, 61))
            $screwInner = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(185, 180, 168))
            $g.FillEllipse($screwOuter, $x - 6, $y - 6, 12, 12)
            $g.FillEllipse($screwInner, $x - 4, $y - 4, 8, 8)
            $g.DrawLine([System.Drawing.Pens]::Black, $x - 3, $y, $x + 3, $y)
            $screwOuter.Dispose()
            $screwInner.Dispose()
        }
    }

    $outerBrush.Dispose()
    $bezelBrush.Dispose()
    $faceBrush.Dispose()
    $highlightPen.Dispose()
    $outerPath.Dispose()
    $bezelPath.Dispose()
    $facePath.Dispose()
    $g.Dispose()
    return $bitmap
}

$form = [System.Windows.Forms.Form]::new()
$form.Text = 'EPA Launch Console'
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedSingle
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.ClientSize = [System.Drawing.Size]::new(780, 470)
$form.BackColor = [System.Drawing.Color]::FromArgb(15, 19, 17)
$form.ForeColor = [System.Drawing.Color]::FromArgb(240, 229, 208)
$form.Font = [System.Drawing.Font]::new('Segoe UI', 10)
$iconPath = Join-Path $repoRoot 'src\EngineeringPerformance.DesktopHost\Assets\app-icon.ico'
if (Test-Path -LiteralPath $iconPath) { $form.Icon = [System.Drawing.Icon]::new($iconPath) }

$plate = [System.Windows.Forms.Panel]::new()
$plate.Location = [System.Drawing.Point]::new(28, 28)
$plate.Size = [System.Drawing.Size]::new(724, 414)
$plate.BackColor = [System.Drawing.Color]::FromArgb(28, 34, 30)
$plate.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$form.Controls.Add($plate)

$title = [System.Windows.Forms.Label]::new()
$title.Text = 'EPA  •  ENGINEERING PERFORMANCE ANALYZER'
$title.Location = [System.Drawing.Point]::new(40, 32)
$title.Size = [System.Drawing.Size]::new(644, 34)
$title.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$title.ForeColor = [System.Drawing.Color]::FromArgb(236, 224, 202)
$title.Font = [System.Drawing.Font]::new('Segoe UI Semibold', 16, [System.Drawing.FontStyle]::Bold)
$plate.Controls.Add($title)

$button = [System.Windows.Forms.Button]::new()
$button.Location = [System.Drawing.Point]::new(62, 92)
$button.Size = [System.Drawing.Size]::new(600, 190)
$button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$button.FlatAppearance.BorderSize = 0
$button.BackgroundImageLayout = [System.Windows.Forms.ImageLayout]::Stretch
$button.ForeColor = [System.Drawing.Color]::FromArgb(255, 246, 229)
$button.Font = [System.Drawing.Font]::new('Segoe UI Semibold', 22, [System.Drawing.FontStyle]::Bold)
$button.Cursor = [System.Windows.Forms.Cursors]::Hand
$button.Text = "UPDATE  •  BUILD  •  TEST`r`nLAUNCH EPA"
$button.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$plate.Controls.Add($button)

$normalImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(244, 133, 44)) ([System.Drawing.Color]::FromArgb(174, 62, 6)) $false
$pressedImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(198, 82, 15)) ([System.Drawing.Color]::FromArgb(111, 34, 3)) $true
$errorImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(196, 62, 48)) ([System.Drawing.Color]::FromArgb(113, 24, 19)) $false
$successImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(78, 168, 60)) ([System.Drawing.Color]::FromArgb(31, 91, 29)) $false
$button.BackgroundImage = $normalImage

$status = [System.Windows.Forms.Label]::new()
$status.Location = [System.Drawing.Point]::new(52, 305)
$status.Size = [System.Drawing.Size]::new(620, 62)
$status.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$status.ForeColor = [System.Drawing.Color]::FromArgb(205, 197, 180)
$status.Font = [System.Drawing.Font]::new('Segoe UI', 11)
$status.Text = 'One click synchronizes main, builds, validates, and launches EPA.'
$plate.Controls.Add($status)

$detail = [System.Windows.Forms.Label]::new()
$detail.Location = [System.Drawing.Point]::new(52, 371)
$detail.Size = [System.Drawing.Size]::new(620, 24)
$detail.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$detail.ForeColor = [System.Drawing.Color]::FromArgb(121, 132, 123)
$detail.Font = [System.Drawing.Font]::new('Consolas', 8)
$detail.Text = 'No command prompt required.'
$plate.Controls.Add($detail)

$script:running = $false
$script:closeTimer = $null
$timer = [System.Windows.Forms.Timer]::new()
$timer.Interval = 250
$timer.Add_Tick({
    if (-not $script:running -or -not (Test-Path -LiteralPath $statusFile)) { return }
    try {
        $payload = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
        if ($payload.message) { $status.Text = $payload.message }
        if ($payload.stage) { $detail.Text = "$($payload.stage)  •  log: $logFile" }

        switch ($payload.state) {
            'LAUNCHED' {
                $button.BackgroundImage = $successImage
                $button.Text = "EPA READY`r`nLAUNCHED"
                $status.Text = 'EPA launched successfully.'
                $script:running = $false
                $timer.Stop()
                $script:closeTimer = [System.Windows.Forms.Timer]::new()
                $script:closeTimer.Interval = 900
                $script:closeTimer.Add_Tick({ $script:closeTimer.Stop(); $form.Close() })
                $script:closeTimer.Start()
            }
            'ERROR' {
                $button.BackgroundImage = $errorImage
                $button.Text = 'TRY AGAIN'
                $button.Enabled = $true
                $script:running = $false
                $timer.Stop()
                $detail.Text = "Details: $logFile"
            }
            default {
                $button.Text = "$($payload.stage)`r`nPLEASE WAIT"
            }
        }
    } catch {
        # The worker replaces the JSON atomically. Retry on the next timer tick.
    }
})

$button.Add_MouseDown({ if (-not $script:running) { $button.BackgroundImage = $pressedImage } })
$button.Add_MouseUp({ if (-not $script:running) { $button.BackgroundImage = $normalImage } })
$button.Add_Click({
    if ($script:running) { return }
    if (-not (Test-Path -LiteralPath $workerScript)) {
        [System.Windows.Forms.MessageBox]::Show(
            "Worker script not found:`r`n$workerScript",
            'EPA Launcher',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
        return
    }

    Remove-Item -LiteralPath $statusFile -Force -ErrorAction SilentlyContinue
    $button.Enabled = $false
    $button.BackgroundImage = $pressedImage
    $button.Text = "STARTING`r`nPLEASE WAIT"
    $status.Text = 'Preparing the EPA production workflow…'
    $detail.Text = 'Background worker starting…'
    $script:running = $true

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $workerScript + '"'),
        '-RepoRoot', ('"' + $repoRoot + '"'),
        '-StatusFile', ('"' + $statusFile + '"'),
        '-LogFile', ('"' + $logFile + '"')
    ) -join ' '

    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden | Out-Null
    $timer.Start()
})

$form.Add_FormClosed({
    $timer.Stop()
    if ($script:closeTimer) { $script:closeTimer.Stop(); $script:closeTimer.Dispose() }
    $normalImage.Dispose()
    $pressedImage.Dispose()
    $errorImage.Dispose()
    $successImage.Dispose()
    if ($form.Icon) { $form.Icon.Dispose() }
})

[void]$form.ShowDialog()
