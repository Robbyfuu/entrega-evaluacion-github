<#
.SYNOPSIS
    GUI interactiva para alumnos: crear repo en GitHub y subir su evaluación.

.DESCRIPTION
    Levanta un formulario Windows Forms con:
    - Campos: Nombre completo, Forma de prueba
    - Selector de carpeta con los archivos
    - Botones: Crear Repo, Subir Archivos, Hacer Todo
    - Log de salida en pantalla

.NOTES
    Requiere: git + gh CLI instalados, y gh autenticado (gh auth login).
#>

# -- Carga de assemblies WinForms --
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# ============================================================
#                     FUNCIONES HELPER
# ============================================================

function Sanitize-RepoName {
    param([string]$Text)
    # Quita acentos
    $normalized = $Text.Normalize([Text.NormalizationForm]::FormD)
    $sb = New-Object System.Text.StringBuilder
    foreach ($c in $normalized.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$sb.Append($c)
        }
    }
    $clean = $sb.ToString().Normalize([Text.NormalizationForm]::FormC)
    # Lowercase, espacios a guion, solo alfanumericos y guiones
    $clean = $clean.ToLower() -replace '\s+', '-' -replace '[^a-z0-9\-]', ''
    $clean = $clean -replace '-+', '-' -replace '^-|-$', ''
    return $clean
}

function Test-Dependencies {
    $missing = @()
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { $missing += 'git' }
    if (-not (Get-Command gh  -ErrorAction SilentlyContinue)) { $missing += 'gh' }
    return $missing
}

function Install-Dependencies {
    param([string[]]$Missing)

    # Mapa de winget IDs
    $wingetMap = @{
        'git' = 'Git.Git'
        'gh'  = 'GitHub.cli'
    }

    # Chequeo winget
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        [System.Windows.Forms.MessageBox]::Show(
            "winget no está disponible en este sistema.`n`n" +
            "Instalá manualmente:`n" +
            "  - git:  https://git-scm.com/download/win`n" +
            "  - gh:   https://cli.github.com/`n`n" +
            "O instalá 'App Installer' desde Microsoft Store para obtener winget.",
            'winget no disponible', 'OK', 'Warning') | Out-Null
        return $false
    }

    $msg = "Faltan estas dependencias:`n  - $($Missing -join "`n  - ")`n`n" +
           "¿Querés instalarlas ahora con winget? (requiere permisos de admin)"
    $r = [System.Windows.Forms.MessageBox]::Show($msg, 'Instalar dependencias', 'YesNo', 'Question')
    if ($r -ne 'Yes') { return $false }

    foreach ($dep in $Missing) {
        $id = $wingetMap[$dep]
        if (-not $id) { continue }
        Log "→ Instalando $dep (winget id: $id)..." 'Yellow'
        Set-Status "Instalando $dep..."

        $proc = Start-Process winget -ArgumentList "install --id $id --silent --accept-source-agreements --accept-package-agreements" `
                              -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -eq 0) {
            Log "✓ $dep instalado." 'Green'
        } else {
            Log "✗ Falló instalación de $dep (exit code $($proc.ExitCode))" 'Red'
            return $false
        }
    }

    # Refrescar PATH del proceso actual para detectar los binarios recién instalados
    $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('Path', 'User')

    Log '✓ Dependencias listas. Puede que necesites cerrar y reabrir el script.' 'Cyan'
    [System.Windows.Forms.MessageBox]::Show(
        "Instalación completada.`n`nSi alguna acción falla, cerrá y reabrí el script para que tome el PATH actualizado.",
        'Listo', 'OK', 'Information') | Out-Null
    return $true
}

function Test-GhAuth {
    try {
        $null = gh auth status 2>&1
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

# ============================================================
#         DEVICE FLOW LOGIN (sin abrir navegador local)
# ============================================================

function Start-GitHubDeviceLogin {
    # OAuth Client ID público del GitHub CLI oficial
    $clientId = '178c6fc778ccc68e1d6a'

    # 1. Solicitar device code
    try {
        $resp = Invoke-RestMethod -Method Post `
            -Uri 'https://github.com/login/device/code' `
            -Body @{ client_id = $clientId; scope = 'repo workflow read:org gist' } `
            -Headers @{ 'Accept' = 'application/json' }
    } catch {
        [System.Windows.Forms.MessageBox]::Show(
            "Error contactando GitHub: $_`n`nVerificá tu conexión a internet.",
            'Error de red', 'OK', 'Error') | Out-Null
        return $false
    }

    $deviceCode = $resp.device_code
    $userCode   = $resp.user_code        # Formato: XXXX-XXXX
    $verifyUri  = $resp.verification_uri # https://github.com/login/device
    $interval   = [int]$resp.interval
    $expiresIn  = [int]$resp.expires_in
    $startTime  = Get-Date

    # 2. Construir dialog
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Iniciar sesión en GitHub'
    $dlg.Size = New-Object System.Drawing.Size(550, 480)
    $dlg.StartPosition = 'CenterParent'
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false

    # Paso 1
    $lblPaso1 = New-Object System.Windows.Forms.Label
    $lblPaso1.Text = 'PASO 1: Abrí esta URL en tu navegador o celular'
    $lblPaso1.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    $lblPaso1.Location = New-Object System.Drawing.Point(20, 20)
    $lblPaso1.Size = New-Object System.Drawing.Size(500, 22)
    $dlg.Controls.Add($lblPaso1)

    $txtUrl = New-Object System.Windows.Forms.TextBox
    $txtUrl.Text = $verifyUri
    $txtUrl.ReadOnly = $true
    $txtUrl.Font = New-Object System.Drawing.Font('Consolas', 11)
    $txtUrl.Location = New-Object System.Drawing.Point(20, 48)
    $txtUrl.Size = New-Object System.Drawing.Size(380, 25)
    $dlg.Controls.Add($txtUrl)

    $btnCopyUrl = New-Object System.Windows.Forms.Button
    $btnCopyUrl.Text = 'Copiar'
    $btnCopyUrl.Location = New-Object System.Drawing.Point(410, 47)
    $btnCopyUrl.Size = New-Object System.Drawing.Size(110, 27)
    $btnCopyUrl.Add_Click({
        [System.Windows.Forms.Clipboard]::SetText($verifyUri)
        $btnCopyUrl.Text = 'Copiado!'
    })
    $dlg.Controls.Add($btnCopyUrl)

    # Paso 2
    $lblPaso2 = New-Object System.Windows.Forms.Label
    $lblPaso2.Text = 'PASO 2: Ingresá este código'
    $lblPaso2.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    $lblPaso2.Location = New-Object System.Drawing.Point(20, 95)
    $lblPaso2.Size = New-Object System.Drawing.Size(500, 22)
    $dlg.Controls.Add($lblPaso2)

    $lblCode = New-Object System.Windows.Forms.Label
    $lblCode.Text = $userCode
    $lblCode.Font = New-Object System.Drawing.Font('Consolas', 28, [System.Drawing.FontStyle]::Bold)
    $lblCode.TextAlign = 'MiddleCenter'
    $lblCode.BackColor = [System.Drawing.Color]::FromArgb(33, 33, 33)
    $lblCode.ForeColor = [System.Drawing.Color]::LimeGreen
    $lblCode.Location = New-Object System.Drawing.Point(20, 125)
    $lblCode.Size = New-Object System.Drawing.Size(380, 60)
    $dlg.Controls.Add($lblCode)

    $btnCopyCode = New-Object System.Windows.Forms.Button
    $btnCopyCode.Text = 'Copiar código'
    $btnCopyCode.Location = New-Object System.Drawing.Point(410, 140)
    $btnCopyCode.Size = New-Object System.Drawing.Size(110, 32)
    $btnCopyCode.Add_Click({
        [System.Windows.Forms.Clipboard]::SetText($userCode)
        $btnCopyCode.Text = 'Copiado!'
    })
    $dlg.Controls.Add($btnCopyCode)

    # Botón abrir browser (opcional)
    $btnOpen = New-Object System.Windows.Forms.Button
    $btnOpen.Text = 'Abrir URL en navegador (opcional)'
    $btnOpen.Location = New-Object System.Drawing.Point(20, 205)
    $btnOpen.Size = New-Object System.Drawing.Size(500, 32)
    $btnOpen.Add_Click({
        Start-Process $verifyUri
    })
    $dlg.Controls.Add($btnOpen)

    # Status
    $lblStatus = New-Object System.Windows.Forms.Label
    $lblStatus.Text = 'Esperando que ingreses el código...'
    $lblStatus.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Italic)
    $lblStatus.ForeColor = [System.Drawing.Color]::DarkOrange
    $lblStatus.TextAlign = 'MiddleCenter'
    $lblStatus.Location = New-Object System.Drawing.Point(20, 255)
    $lblStatus.Size = New-Object System.Drawing.Size(500, 22)
    $dlg.Controls.Add($lblStatus)

    # Progress bar
    $progress = New-Object System.Windows.Forms.ProgressBar
    $progress.Style = 'Marquee'
    $progress.MarqueeAnimationSpeed = 30
    $progress.Location = New-Object System.Drawing.Point(20, 285)
    $progress.Size = New-Object System.Drawing.Size(500, 12)
    $dlg.Controls.Add($progress)

    # Tiempo restante
    $lblTime = New-Object System.Windows.Forms.Label
    $lblTime.Text = "Código válido por: $($expiresIn / 60) minutos"
    $lblTime.Font = New-Object System.Drawing.Font('Segoe UI', 8)
    $lblTime.ForeColor = [System.Drawing.Color]::Gray
    $lblTime.TextAlign = 'MiddleCenter'
    $lblTime.Location = New-Object System.Drawing.Point(20, 310)
    $lblTime.Size = New-Object System.Drawing.Size(500, 18)
    $dlg.Controls.Add($lblTime)

    # Botón cancelar
    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'Cancelar'
    $btnCancel.Location = New-Object System.Drawing.Point(210, 395)
    $btnCancel.Size = New-Object System.Drawing.Size(120, 32)
    $btnCancel.DialogResult = 'Cancel'
    $dlg.Controls.Add($btnCancel)
    $dlg.CancelButton = $btnCancel

    # Estado del polling (var de cierre)
    $script:authResult = $null
    $script:authToken = $null

    # Timer para polling
    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = $interval * 1000
    $timer.Add_Tick({
        # Chequear expiración
        $elapsed = (Get-Date) - $startTime
        $remaining = [int]($expiresIn - $elapsed.TotalSeconds)
        if ($remaining -le 0) {
            $timer.Stop()
            $lblStatus.Text = 'Código expirado. Cerrá y volvé a intentar.'
            $lblStatus.ForeColor = [System.Drawing.Color]::Red
            $progress.Style = 'Continuous'
            $progress.Value = 0
            return
        }
        $lblTime.Text = "Tiempo restante: $([int]($remaining / 60)) min $($remaining % 60) seg"

        # Poll al endpoint de token
        try {
            $tokenResp = Invoke-RestMethod -Method Post `
                -Uri 'https://github.com/login/oauth/access_token' `
                -Body @{
                    client_id   = $clientId
                    device_code = $deviceCode
                    grant_type  = 'urn:ietf:params:oauth:grant-type:device_code'
                } `
                -Headers @{ 'Accept' = 'application/json' }

            if ($tokenResp.access_token) {
                $timer.Stop()
                $script:authToken = $tokenResp.access_token
                $script:authResult = 'OK'
                $lblStatus.Text = 'Autorizado! Guardando credenciales...'
                $lblStatus.ForeColor = [System.Drawing.Color]::Green
                $progress.Style = 'Continuous'
                $progress.Value = 100
                $dlg.DialogResult = 'OK'
                Start-Sleep -Milliseconds 800
                $dlg.Close()
                return
            }

            switch ($tokenResp.error) {
                'authorization_pending' { return }  # Seguir esperando
                'slow_down' {
                    $timer.Interval = ($interval + 5) * 1000
                    return
                }
                'expired_token' {
                    $timer.Stop()
                    $lblStatus.Text = 'Código expirado.'
                    $lblStatus.ForeColor = [System.Drawing.Color]::Red
                }
                'access_denied' {
                    $timer.Stop()
                    $lblStatus.Text = 'Acceso denegado por el usuario.'
                    $lblStatus.ForeColor = [System.Drawing.Color]::Red
                }
                default {
                    $lblStatus.Text = "Estado: $($tokenResp.error)"
                }
            }
        } catch {
            $lblStatus.Text = "Error de red (reintentando)..."
        }
    })
    $timer.Start()

    # Mostrar dialog modal
    $result = $dlg.ShowDialog($form)
    $timer.Stop()
    $timer.Dispose()

    if ($result -ne 'OK' -or -not $script:authToken) {
        return $false
    }

    # Guardar token en gh CLI
    try {
        $script:authToken | gh auth login --hostname github.com --git-protocol https --with-token 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $script:authToken = $null  # Limpiar de memoria
            Log '✓ Sesión iniciada correctamente.' 'Green'
            return $true
        } else {
            Log '✗ Falló el guardado del token en gh.' 'Red'
            return $false
        }
    } catch {
        Log "✗ Error guardando token: $_" 'Red'
        return $false
    }
}

# ============================================================
#                     FORMULARIO PRINCIPAL
# ============================================================

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Subir Evaluación a GitHub'
$form.Size = New-Object System.Drawing.Size(640, 600)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false

# -- Título --
$lblTitulo = New-Object System.Windows.Forms.Label
$lblTitulo.Text = 'Evaluación → GitHub'
$lblTitulo.Font = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
$lblTitulo.Location = New-Object System.Drawing.Point(20, 15)
$lblTitulo.Size = New-Object System.Drawing.Size(420, 35)
$form.Controls.Add($lblTitulo)

# -- Botón login (esquina superior derecha) --
$btnLogin = New-Object System.Windows.Forms.Button
$btnLogin.Text = 'Iniciar sesión'
$btnLogin.Location = New-Object System.Drawing.Point(450, 20)
$btnLogin.Size = New-Object System.Drawing.Size(150, 30)
$btnLogin.BackColor = [System.Drawing.Color]::FromArgb(96, 125, 139)
$btnLogin.ForeColor = [System.Drawing.Color]::White
$btnLogin.FlatStyle = 'Flat'
$form.Controls.Add($btnLogin)

# -- Nombre completo --
$lblNombre = New-Object System.Windows.Forms.Label
$lblNombre.Text = 'Nombre completo:'
$lblNombre.Location = New-Object System.Drawing.Point(20, 60)
$lblNombre.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblNombre)

$txtNombre = New-Object System.Windows.Forms.TextBox
$txtNombre.Location = New-Object System.Drawing.Point(180, 58)
$txtNombre.Size = New-Object System.Drawing.Size(420, 22)
$form.Controls.Add($txtNombre)

# -- Forma de prueba --
$lblForma = New-Object System.Windows.Forms.Label
$lblForma.Text = 'Forma de prueba:'
$lblForma.Location = New-Object System.Drawing.Point(20, 95)
$lblForma.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblForma)

$cmbForma = New-Object System.Windows.Forms.ComboBox
$cmbForma.Location = New-Object System.Drawing.Point(180, 93)
$cmbForma.Size = New-Object System.Drawing.Size(420, 22)
$cmbForma.DropDownStyle = 'DropDown'  # Permite escribir personalizado
$cmbForma.Items.AddRange(@('Forma-A', 'Forma-B', 'Forma-C', 'Forma-D'))
$form.Controls.Add($cmbForma)

# -- Carpeta de archivos --
$lblCarpeta = New-Object System.Windows.Forms.Label
$lblCarpeta.Text = 'Carpeta del proyecto:'
$lblCarpeta.Location = New-Object System.Drawing.Point(20, 130)
$lblCarpeta.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblCarpeta)

$txtCarpeta = New-Object System.Windows.Forms.TextBox
$txtCarpeta.Location = New-Object System.Drawing.Point(180, 128)
$txtCarpeta.Size = New-Object System.Drawing.Size(330, 22)
$txtCarpeta.ReadOnly = $true
$form.Controls.Add($txtCarpeta)

$btnBuscar = New-Object System.Windows.Forms.Button
$btnBuscar.Text = 'Buscar...'
$btnBuscar.Location = New-Object System.Drawing.Point(520, 127)
$btnBuscar.Size = New-Object System.Drawing.Size(80, 25)
$form.Controls.Add($btnBuscar)

# -- Nombre del repo (preview) --
$lblRepoPreview = New-Object System.Windows.Forms.Label
$lblRepoPreview.Text = 'Repo:'
$lblRepoPreview.Location = New-Object System.Drawing.Point(20, 165)
$lblRepoPreview.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblRepoPreview)

$lblRepoValor = New-Object System.Windows.Forms.Label
$lblRepoValor.Text = '(rellenar nombre y forma)'
$lblRepoValor.ForeColor = [System.Drawing.Color]::Gray
$lblRepoValor.Font = New-Object System.Drawing.Font('Consolas', 10)
$lblRepoValor.Location = New-Object System.Drawing.Point(180, 165)
$lblRepoValor.Size = New-Object System.Drawing.Size(420, 20)
$form.Controls.Add($lblRepoValor)

# -- Visibilidad del repo --
$grpVis = New-Object System.Windows.Forms.GroupBox
$grpVis.Text = 'Visibilidad'
$grpVis.Location = New-Object System.Drawing.Point(20, 195)
$grpVis.Size = New-Object System.Drawing.Size(580, 55)
$form.Controls.Add($grpVis)

$rbPrivate = New-Object System.Windows.Forms.RadioButton
$rbPrivate.Text = 'Privado (recomendado)'
$rbPrivate.Location = New-Object System.Drawing.Point(15, 22)
$rbPrivate.Size = New-Object System.Drawing.Size(200, 22)
$rbPrivate.Checked = $true
$grpVis.Controls.Add($rbPrivate)

$rbPublic = New-Object System.Windows.Forms.RadioButton
$rbPublic.Text = 'Público'
$rbPublic.Location = New-Object System.Drawing.Point(230, 22)
$rbPublic.Size = New-Object System.Drawing.Size(120, 22)
$grpVis.Controls.Add($rbPublic)

# -- Botones de acción --
$btnCrearRepo = New-Object System.Windows.Forms.Button
$btnCrearRepo.Text = '1. Crear Repo'
$btnCrearRepo.Location = New-Object System.Drawing.Point(20, 265)
$btnCrearRepo.Size = New-Object System.Drawing.Size(180, 35)
$btnCrearRepo.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
$btnCrearRepo.ForeColor = [System.Drawing.Color]::White
$btnCrearRepo.FlatStyle = 'Flat'
$form.Controls.Add($btnCrearRepo)

$btnSubir = New-Object System.Windows.Forms.Button
$btnSubir.Text = '2. Subir Archivos'
$btnSubir.Location = New-Object System.Drawing.Point(220, 265)
$btnSubir.Size = New-Object System.Drawing.Size(180, 35)
$btnSubir.BackColor = [System.Drawing.Color]::FromArgb(76, 175, 80)
$btnSubir.ForeColor = [System.Drawing.Color]::White
$btnSubir.FlatStyle = 'Flat'
$form.Controls.Add($btnSubir)

$btnTodo = New-Object System.Windows.Forms.Button
$btnTodo.Text = 'Hacer TODO'
$btnTodo.Location = New-Object System.Drawing.Point(420, 265)
$btnTodo.Size = New-Object System.Drawing.Size(180, 35)
$btnTodo.BackColor = [System.Drawing.Color]::FromArgb(255, 87, 34)
$btnTodo.ForeColor = [System.Drawing.Color]::White
$btnTodo.FlatStyle = 'Flat'
$btnTodo.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnTodo)

# -- Log de salida --
$lblLog = New-Object System.Windows.Forms.Label
$lblLog.Text = 'Salida:'
$lblLog.Location = New-Object System.Drawing.Point(20, 315)
$lblLog.Size = New-Object System.Drawing.Size(100, 20)
$form.Controls.Add($lblLog)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(20, 340)
$txtLog.Size = New-Object System.Drawing.Size(580, 180)
$txtLog.Multiline = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.ReadOnly = $true
$txtLog.BackColor = [System.Drawing.Color]::Black
$txtLog.ForeColor = [System.Drawing.Color]::LimeGreen
$txtLog.Font = New-Object System.Drawing.Font('Consolas', 9)
$form.Controls.Add($txtLog)

# -- Status bar --
$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Location = New-Object System.Drawing.Point(20, 530)
$lblStatus.Size = New-Object System.Drawing.Size(580, 20)
$lblStatus.Text = 'Listo.'
$form.Controls.Add($lblStatus)

# ============================================================
#                     LÓGICA / EVENTOS
# ============================================================

function Log {
    param([string]$Msg, [string]$Color = 'LimeGreen')
    $txtLog.AppendText("[$([DateTime]::Now.ToString('HH:mm:ss'))] $Msg`r`n")
    $txtLog.SelectionStart = $txtLog.Text.Length
    $txtLog.ScrollToCaret()
    [System.Windows.Forms.Application]::DoEvents()
}

function Set-Status {
    param([string]$Msg)
    $lblStatus.Text = $Msg
    [System.Windows.Forms.Application]::DoEvents()
}

function Update-RepoPreview {
    $nombre = $txtNombre.Text.Trim()
    $forma = $cmbForma.Text.Trim()
    if ($nombre -and $forma) {
        $repo = Sanitize-RepoName "$nombre-$forma"
        $lblRepoValor.Text = $repo
        $lblRepoValor.ForeColor = [System.Drawing.Color]::Black
    } else {
        $lblRepoValor.Text = '(rellenar nombre y forma)'
        $lblRepoValor.ForeColor = [System.Drawing.Color]::Gray
    }
}

function Get-RepoName {
    $nombre = $txtNombre.Text.Trim()
    $forma = $cmbForma.Text.Trim()
    if (-not $nombre -or -not $forma) { return $null }
    return (Sanitize-RepoName "$nombre-$forma")
}

function Validate-Inputs {
    param([switch]$RequireFolder)

    # Dependencias
    $missing = Test-Dependencies
    if ($missing) {
        $installed = Install-Dependencies -Missing $missing
        if (-not $installed) { return $false }
        # Re-chequear
        $missing = Test-Dependencies
        if ($missing) {
            [System.Windows.Forms.MessageBox]::Show(
                "Aún faltan: $($missing -join ', ').`n`nCerrá y reabrí el script.",
                'Reiniciar requerido', 'OK', 'Warning') | Out-Null
            return $false
        }
    }

    # gh auth
    if (-not (Test-GhAuth)) {
        $r = [System.Windows.Forms.MessageBox]::Show(
            "No estás autenticado en GitHub.`n`n¿Querés iniciar sesión ahora?",
            'Sin autenticación', 'YesNo', 'Warning')
        if ($r -eq 'Yes') {
            Log '→ Iniciando login con device flow (sin abrir navegador)' 'Yellow'
            $ok = Start-GitHubDeviceLogin
            if (-not $ok) {
                Log '✗ Login cancelado o fallido.' 'Red'
                return $false
            }
            # Re-chequear
            if (-not (Test-GhAuth)) {
                Log '✗ Auth aún no detectada. Intentá de nuevo.' 'Red'
                return $false
            }
        } else {
            return $false
        }
    }

    # Campos requeridos
    if (-not $txtNombre.Text.Trim()) {
        [System.Windows.Forms.MessageBox]::Show('Ingresá tu nombre completo.', 'Falta dato', 'OK', 'Warning') | Out-Null
        return $false
    }
    if (-not $cmbForma.Text.Trim()) {
        [System.Windows.Forms.MessageBox]::Show('Seleccioná o escribí la forma de la prueba.', 'Falta dato', 'OK', 'Warning') | Out-Null
        return $false
    }

    if ($RequireFolder) {
        if (-not $txtCarpeta.Text -or -not (Test-Path $txtCarpeta.Text)) {
            [System.Windows.Forms.MessageBox]::Show('Seleccioná una carpeta válida con tu evaluación.', 'Falta carpeta', 'OK', 'Warning') | Out-Null
            return $false
        }
    }

    return $true
}

function Invoke-CreateRepo {
    if (-not (Validate-Inputs)) { return $false }
    $repo = Get-RepoName
    $visibility = if ($rbPrivate.Checked) { '--private' } else { '--public' }

    Set-Status "Creando repo $repo..."
    Log "→ Creando repo '$repo' ($visibility)"

    try {
        # Verifica si ya existe
        $existe = gh repo view $repo 2>&1
        if ($LASTEXITCODE -eq 0) {
            Log "Repo '$repo' ya existe en tu cuenta. Lo usaremos." 'Yellow'
            return $true
        }

        # Crea
        $output = gh repo create $repo $visibility --confirm 2>&1
        if ($LASTEXITCODE -eq 0) {
            Log "✓ Repo creado: $output"
            Set-Status "Repo $repo creado."
            return $true
        } else {
            Log "✗ Error creando repo: $output" 'Red'
            Set-Status 'Error al crear repo.'
            [System.Windows.Forms.MessageBox]::Show("No se pudo crear el repo:`n`n$output", 'Error', 'OK', 'Error') | Out-Null
            return $false
        }
    } catch {
        Log "✗ Excepción: $_" 'Red'
        return $false
    }
}

function Invoke-UploadFiles {
    if (-not (Validate-Inputs -RequireFolder)) { return $false }

    $repo = Get-RepoName
    $folder = $txtCarpeta.Text
    $nombre = $txtNombre.Text.Trim()
    $forma = $cmbForma.Text.Trim()

    # Obtener usuario de gh para construir URL
    $ghUser = (gh api user --jq .login 2>$null).Trim()
    if (-not $ghUser) {
        Log '✗ No se pudo obtener tu usuario de GitHub.' 'Red'
        return $false
    }

    $repoUrl = "https://github.com/$ghUser/$repo.git"

    Set-Status "Subiendo a $repo..."
    Log "→ Carpeta: $folder"
    Log "→ Repo URL: $repoUrl"

    Push-Location $folder
    try {
        # Init si no es repo
        if (-not (Test-Path .git)) {
            Log '→ git init'
            git init -b main 2>&1 | ForEach-Object { Log "  $_" }
        }

        # Configurar identidad local del repo (no afecta config global)
        # user.name = nombre real del alumno
        # user.email = email publico de GitHub o noreply si es privado
        Log "→ git config user.name = `"$nombre`""
        git config user.name "$nombre" 2>&1 | Out-Null

        $userInfo = gh api user 2>$null | ConvertFrom-Json
        $email = $userInfo.email
        if (-not $email) {
            # Email privado en GitHub → usar formato noreply
            $email = "$($userInfo.id)+$($userInfo.login)@users.noreply.github.com"
            Log "→ git config user.email = `"$email`" (email privado, usando noreply)"
        } else {
            Log "→ git config user.email = `"$email`""
        }
        git config user.email "$email" 2>&1 | Out-Null

        # Configurar remote
        $remotes = git remote 2>$null
        if ($remotes -contains 'origin') {
            Log '→ Actualizando remote origin'
            git remote set-url origin $repoUrl 2>&1 | ForEach-Object { Log "  $_" }
        } else {
            Log '→ Agregando remote origin'
            git remote add origin $repoUrl 2>&1 | ForEach-Object { Log "  $_" }
        }

        # Asegurar branch main
        git branch -M main 2>&1 | Out-Null

        # Add
        Log '→ git add .'
        git add . 2>&1 | ForEach-Object { Log "  $_" }

        # Verificar si hay cambios
        $status = git status --porcelain 2>$null
        if (-not $status) {
            Log '⚠ Sin cambios para commitear (working tree limpio).' 'Yellow'
        } else {
            # Commit
            $msg = "Entrega de evaluación - $nombre ($forma)"
            Log "→ git commit -m `"$msg`""
            git commit -m $msg 2>&1 | ForEach-Object { Log "  $_" }
        }

        # Push
        Log '→ git push -u origin main'
        $pushOutput = git push -u origin main 2>&1
        $pushOutput | ForEach-Object { Log "  $_" }

        if ($LASTEXITCODE -eq 0) {
            Log "✓ Subida completada. Ver en: https://github.com/$ghUser/$repo" 'Cyan'
            Set-Status 'Subida OK.'
            [System.Windows.Forms.MessageBox]::Show(
                "Evaluación subida correctamente.`n`nRepo: https://github.com/$ghUser/$repo",
                'Listo', 'OK', 'Information') | Out-Null
            return $true
        } else {
            Log '✗ Falló el push.' 'Red'
            Set-Status 'Error en push.'
            return $false
        }
    } catch {
        Log "✗ Excepción: $_" 'Red'
        return $false
    } finally {
        Pop-Location
    }
}

# -- Wiring de eventos --
$txtNombre.Add_TextChanged({ Update-RepoPreview })
$cmbForma.Add_TextChanged({ Update-RepoPreview })
$cmbForma.Add_SelectedIndexChanged({ Update-RepoPreview })

$btnBuscar.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Seleccioná la carpeta con tu evaluación'
    if ($dlg.ShowDialog() -eq 'OK') {
        $txtCarpeta.Text = $dlg.SelectedPath
        Log "Carpeta seleccionada: $($dlg.SelectedPath)"
    }
})

$btnLogin.Add_Click({
    if (Test-GhAuth) {
        $ghUser = (gh api user --jq .login 2>$null).Trim()
        $r = [System.Windows.Forms.MessageBox]::Show(
            "Ya estás logueado como: $ghUser`n`n¿Querés cerrar sesión y entrar con otra cuenta?",
            'Sesión activa', 'YesNo', 'Question')
        if ($r -eq 'Yes') {
            Log "→ Cerrando sesión de $ghUser..."
            gh auth logout --hostname github.com 2>&1 | Out-Null
            Log '→ Iniciando nuevo login...'
            [void](Start-GitHubDeviceLogin)
        }
    } else {
        Log '→ Iniciando login con device flow...'
        [void](Start-GitHubDeviceLogin)
    }
})

$btnCrearRepo.Add_Click({ [void](Invoke-CreateRepo) })
$btnSubir.Add_Click({ [void](Invoke-UploadFiles) })
$btnTodo.Add_Click({
    if (Invoke-CreateRepo) {
        Start-Sleep -Seconds 1
        [void](Invoke-UploadFiles)
    }
})

# -- Mostrar form --
Log 'Listo. Completá los datos y elegí una acción.'
Log 'Tip: usá "Hacer TODO" si es la primera vez.' 'Cyan'

# Chequeo proactivo de dependencias al abrir
$initMissing = Test-Dependencies
if ($initMissing) {
    Log "⚠ Dependencias faltantes detectadas: $($initMissing -join ', ')" 'Yellow'
    Log '  Te ofreceré instalarlas la primera vez que uses una acción.' 'Yellow'
} else {
    Log '✓ git y gh detectados.'
    if (Test-GhAuth) {
        Log '✓ gh autenticado.'
    } else {
        Log '⚠ gh NO autenticado. Te pediré login al crear repo.' 'Yellow'
    }
}

[void]$form.ShowDialog()
