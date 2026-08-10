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
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
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

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $outerRect = New-Object System.Drawing.RectangleF 3, 3, ($Width - 6), ($Height - 6)
    $outerPath = New-RoundedPath $outerRect 20
    $outerBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(22, 25, 22))
    $g.FillPath($outerBrush, $outerPath)

    $bezelRect = New-Object System.Drawing.RectangleF 10, 10, ($Width - 20), ($Height - 20)
    $bezelPath = New-RoundedPath $bezelRect 16
    $bezelBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bezelRect, ([System.Drawing.Color]::FromArgb(104, 101, 91)), ([System.Drawing.Color]::FromArgb(35, 38, 34)), 90
    $g.FillPath($bezelBrush, $bezelPath)

    $offset = if ($Pressed) { 7 } else { 0 }
    $faceRect = New-Object System.Drawing.RectangleF 18, (18 + $offset), ($Width - 36), ($Height - 43)
    $facePath = New-RoundedPath $faceRect 12
    $faceBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $faceRect, $Top, $Bottom, 90
    $g.FillPath($faceBrush, $facePath)

    $highlightPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($(if ($Pressed) { 55 } else { 190 }), 255, 255, 255)), 2
    $g.DrawPath($highlightPen, $facePath)

    if (-not $Pressed) {
        $shadowRect = New-Object System.Drawing.RectangleF 24, ($Height - 29), ($Width - 48), 8
        $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
        $g.FillEllipse($shadowBrush, $shadowRect)
        $shadowBrush.Dispose()
    }

    foreach ($x in @(28, ($Width - 28))) {
        foreach ($y in @(28, ($Height - 28))) {
            $screwBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF ($x - 5), ($y - 5), 10, 10), ([System.Drawing.Color]::FromArgb(210, 205, 193)), ([System.Drawing.Color]::FromArgb(62, 64, 58)), 45
            $g.FillEllipse($screwBrush, ($x - 5), ($y - 5), 10, 10)
            $g.DrawLine([System.Drawing.Pens]::Black, ($x - 3), $y, ($x + 3), $y)
            $screwBrush.Dispose()
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

$form = New-Object System.Windows.Forms.Form
$form.Text = 'EPA Launch Console'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedSingle
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.ClientSize = New-Object System.Drawing.Size 780, 470
$form.BackColor = [System.Drawing.Color]::FromArgb(15, 19, 17)
$form.ForeColor = [System.Drawing.Color]::FromArgb(240, 229, 208)
$form.Font = New-Object System.Drawing.Font 'Segoe UI', 10

$plate = New-Object System.Windows.Forms.Panel
$plate.Location = New-Object System.Drawing.Point 28, 28
$plate.Size = New-Object System.Drawing.Size 724, 414
$plate.BackColor = [System.Drawing.Color]::FromArgb(28, 34, 30)
$plate.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$form.Controls.Add($plate)

$title = New-Object System.Windows.Forms.Label
$title.Text = 'EPA  •  ENGINEERING PERFORMANCE ANALYZER'
$title.Location = New-Object System.Drawing.Point 40, 32
$title.Size = New-Object System.Drawing.Size 644, 34
$title.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$title.ForeColor = [System.Drawing.Color]::FromArgb(236, 224, 202)
$title.Font = New-Object System.Drawing.Font 'Segoe UI Semibold', 16, ([System.Drawing.FontStyle]::Bold)
$plate.Controls.Add($title)

$button = New-Object System.Windows.Forms.Button
$button.Location = New-Object System.Drawing.Point 62, 92
$button.Size = New-Object System.Drawing.Size 600, 190
$button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$button.FlatAppearance.BorderSize = 0
$button.BackgroundImageLayout = [System.Windows.Forms.ImageLayout]::Stretch
$button.ForeColor = [System.Drawing.Color]::FromArgb(255, 246, 229)
$button.Font = New-Object System.Drawing.Font 'Segoe UI Semibold', 22, ([System.Drawing.FontStyle]::Bold)
$button.Cursor = [System.Windows.Forms.Cursors]::Hand
$button.Text = "UPDATE  •  BUILD  •  TEST`r`nLAUNCH EPA"
$button.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$plate.Controls.Add($button)

$normalImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(244, 133, 44)) ([System.Drawing.Color]::FromArgb(174, 62, 6)) $false
$pressedImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(198, 82, 15)) ([System.Drawing.Color]::FromArgb(111, 34, 3)) $true
$errorImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(196, 62, 48)) ([System.Drawing.Color]::FromArgb(113, 24, 19)) $false
$successImage = New-TactileBitmap 600 190 ([System.Drawing.Color]::FromArgb(78, 168, 60)) ([System.Drawing.Color]::FromArgb(31, 91, 29)) $false
$button.BackgroundImage = $normalImage

$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point 52, 305
$status.Size = New-Object System.Drawing.Size 620, 62
$status.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$status.ForeColor = [System.Drawing.Color]::FromArgb(205, 197, 180)
$status.Font = New-Object System.Drawing.Font 'Segoe UI', 11
$status.Text = 'One click synchronizes main, builds, validates, and launches EPA.'
$plate.Controls.Add($status)

$detail = New-Object System.Windows.Forms.Label
$detail.Location = New-Object System.Drawing.Point 52, 371
$detail.Size = New-Object System.Drawing.Size 620, 24
$detail.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$detail.ForeColor = [System.Drawing.Color]::FromArgb(121, 132, 123)
$detail.Font = New-Object System.Drawing.Font 'Consolas', 8
$detail.Text = 'No command prompt required.'
$plate.Controls.Add($detail)

$running = $false
$workerProcess = $null

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 250
$timer.Add_Tick({
    if (-not $running -or -not (Test-Path -LiteralPath $statusFile)) { return }

    try {
        $payload = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
        if ($payload.message) { $status.Text = $payload.message }
        if ($payload.stage) { $detail.Text = "$($payload.stage)  •  log: $logFile" }

        switch ($payload.state) {
            'LAUNCHED' {
                $button.BackgroundImage = $successImage
                $button.Text = "EPA READY`r`nLAUNCHED"
                $status.Text = 'EPA launched successfully.'
                $running = $false
                $timer.Stop()
                $closeTimer = New-Object System.Windows.Forms.Timer
                $closeTimer.Interval = 900
                $closeTimer.Add_Tick({ $closeTimer.Stop(); $form.Close() })
                $closeTimer.Start()
            }
            'ERROR' {
                $button.BackgroundImage = $errorImage
                $button.Text = "TRY AGAIN"
                $button.Enabled = $true
                $running = $false
                $timer.Stop()
                $detail.Text = "Details: $logFile"
            }
            default {
                $button.Text = "$($payload.stage)`r`nPLEASE WAIT"
            }
        }
    }
    catch {
        # Status file may be between atomic replacements; simply retry on next tick.
    }
})

$button.Add_MouseDown({ if (-not $running) { $button.BackgroundImage = $pressedImage } })
$button.Add_MouseUp({ if (-not $running) { $button.BackgroundImage = $normalImage } })

$button.Add_Click({
    if ($running) { return }

    if (-not (Test-Path -LiteralPath $workerScript)) {
        [System.Windows.Forms.MessageBox]::Show("Worker script not found:`r`n$workerScript", 'EPA Launcher', 'OK', 'Error') | Out-Null
        return
    }

    Remove-Item -LiteralPath $statusFile -Force -ErrorAction SilentlyContinue
    $button.Enabled = $false
    $button.BackgroundImage = $pressedImage
    $button.Text = "STARTING`r`nPLEASE WAIT"
    $status.Text = 'Preparing the EPA production workflow…'
    $detail.Text = 'Background worker starting…'
    $running = $true

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $workerScript + '"'),
        '-RepoRoot', ('"' + $repoRoot + '"'),
        '-StatusFile', ('"' + $statusFile + '"'),
        '-LogFile', ('"' + $logFile + '"')
    ) -join ' '

    $workerProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $timer.Start()
})

$form.Add_FormClosed({
    $timer.Stop()
    $normalImage.Dispose()
    $pressedImage.Dispose()
    $errorImage.Dispose()
    $successImage.Dispose()
})

[void]$form.ShowDialog()
