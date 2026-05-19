<#
.SYNOPSIS
    GUI interactiva para alumnos: crear repositorio en GitHub y subir la evaluacion.

.DESCRIPTION
    Levanta un formulario Windows Forms con:
    - Campos: Nombre completo, Forma de prueba
    - Selector de carpeta con los archivos
    - Botones: Crear Repositorio, Subir Archivos, Hacer Todo
    - Panel de cuenta: muestra usuario logueado y permite cerrar sesion
    - Log de salida en pantalla

.NOTES
    Requiere: git + gh CLI instalados, y autenticacion de GitHub.
#>

# -- Auto-desbloqueo de archivos descargados (Zone.Identifier ADS) --
# Evita la advertencia "Windows protegio su PC" en cada ejecucion
try {
    Get-ChildItem -Path $PSScriptRoot -File -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue
} catch {}

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
            "Instala manualmente:`n" +
            "  - git:  https://git-scm.com/download/win`n" +
            "  - gh:   https://cli.github.com/`n`n" +
            "O instala 'App Installer' desde Microsoft Store para obtener winget.",
            'winget no disponible', 'OK', 'Warning') | Out-Null
        return $false
    }

    $msg = "Faltan estas dependencias:`n  - $($Missing -join "`n  - ")`n`n" +
           "¿Quieres instalarlas ahora con winget? (requiere permisos de admin)"
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
        "Instalación completada.`n`nSi alguna acción falla, cierra y reabre el script para que tome el PATH actualizado.",
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
            "Error contactando GitHub: $_`n`nVerifica tu conexión a internet.",
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
    $lblPaso1.Text = 'PASO 1: Abre esta URL en tu navegador o celular'
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
    $lblPaso2.Text = 'PASO 2: Ingresa este código'
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
            $lblStatus.Text = 'Código expirado. Cierra y vuelve a intentar.'
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
                # Limpiar token de whitespace/newlines defensivamente
                $script:authToken = ($tokenResp.access_token -as [string]).Trim()
                $script:authResult = 'OK'
                $script:authScopes = $tokenResp.scope
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

    # Guardar token en gh CLI usando .NET Process API
    # (mas confiable que pipe de PowerShell que puede romper encoding)
    return Save-GhToken -Token $script:authToken
}

function Save-GhToken {
    param([string]$Token)

    if (-not $Token) {
        Log '✗ No hay token para guardar.' 'Red'
        return $false
    }

    # Sanity check del token recibido
    $Token = $Token.Trim() -replace "`r", '' -replace "`n", ''
    if ($Token.Length -lt 20) {
        Log "✗ Token recibido es invalido (length=$($Token.Length))." 'Red'
        return $false
    }
    $prefix = $Token.Substring(0, 4)
    Log "→ Token recibido (length=$($Token.Length), prefix='$prefix...')"

    # 1. Validar el token PRIMERO contra la API antes de intentar guardarlo
    Log '→ Validando token contra api.github.com...'
    try {
        $userInfo = Invoke-RestMethod -Method Get `
            -Uri 'https://api.github.com/user' `
            -Headers @{
                'Authorization' = "Bearer $Token"
                'Accept' = 'application/vnd.github+json'
                'X-GitHub-Api-Version' = '2022-11-28'
                'User-Agent' = 'Subir-Evaluacion-Script'
            }
        Log "✓ Token valido. Usuario: @$($userInfo.login)" 'Green'
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        Log "✗ Token rechazado por GitHub API (HTTP $statusCode)." 'Red'
        Log "  Detalle: $_" 'Red'
        [System.Windows.Forms.MessageBox]::Show(
            "GitHub rechazo el token con HTTP $statusCode.`n`n" +
            "Esto puede pasar si:`n" +
            "  - El device flow expiro entre autorizar y guardar`n" +
            "  - El reloj del sistema esta muy desincronizado`n`n" +
            "Intenta de nuevo (cierra el dialog y vuelve a iniciar sesion).",
            'Token rechazado por GitHub', 'OK', 'Error') | Out-Null
        return $false
    }

    # 2. Limpiar sesion previa
    try {
        gh auth status --hostname github.com 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Log '→ Cerrando sesion previa...'
            gh auth logout --hostname github.com 2>&1 | Out-Null
        }
    } catch {}

    # 3. ESTRATEGIA A: gh auth login --with-token via Process API
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        Log '→ Estrategia A: gh auth login --with-token'
        $okA = Save-GhTokenViaProcess -Token $Token -GhPath $ghCmd.Source
        if ($okA) { return $true }
        Log '  Estrategia A fallo. Intentando B...' 'Yellow'
    }

    # 4. ESTRATEGIA B: escribir directamente al hosts.yml de gh
    Log '→ Estrategia B: escribir hosts.yml directamente'
    $okB = Save-GhTokenToHostsYml -Token $Token -Login $userInfo.login
    if ($okB) {
        # Validar
        $null = gh auth status 2>&1
        if ($LASTEXITCODE -eq 0) {
            Log '✓ Token guardado en hosts.yml correctamente.' 'Green'
            return $true
        }
    }

    Log '✗ Ambas estrategias de guardado fallaron.' 'Red'
    return $false
}

function Save-GhTokenViaProcess {
    param(
        [string]$Token,
        [string]$GhPath
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $GhPath
    $psi.Arguments = 'auth login --hostname github.com --git-protocol https --with-token'
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        # Usar Write (no WriteLine) para evitar CRLF
        $proc.StandardInput.Write($Token)
        $proc.StandardInput.Close()

        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()

        if (-not $proc.WaitForExit(15000)) {
            $proc.Kill()
            Log '  Timeout (>15s).' 'Red'
            return $false
        }

        if ($proc.ExitCode -eq 0) {
            Log '  ✓ Process A OK.' 'Green'
            return $true
        }

        Log "  Process A fallo (exit $($proc.ExitCode))." 'Yellow'
        if ($stdout) { Log "    stdout: $($stdout.Trim())" 'Yellow' }
        if ($stderr) { Log "    stderr: $($stderr.Trim())" 'Yellow' }
        return $false
    } catch {
        Log "  Excepcion en Process A: $_" 'Red'
        return $false
    }
}

function Save-GhTokenToHostsYml {
    param(
        [string]$Token,
        [string]$Login
    )

    # Path standard de gh CLI en Windows
    $ghDir = Join-Path $env:APPDATA 'GitHub CLI'
    if (-not (Test-Path $ghDir)) {
        New-Item -ItemType Directory -Path $ghDir -Force | Out-Null
    }
    $hostsFile = Join-Path $ghDir 'hosts.yml'

    # Formato YAML que gh espera
    $yaml = @"
github.com:
    git_protocol: https
    user: $Login
    oauth_token: $Token
"@

    try {
        # Escribir como UTF-8 sin BOM
        [System.IO.File]::WriteAllText($hostsFile, $yaml, (New-Object System.Text.UTF8Encoding $false))
        Log "  hosts.yml escrito en: $hostsFile"
        return $true
    } catch {
        Log "  Error escribiendo hosts.yml: $_" 'Red'
        return $false
    } finally {
        $Token = $null
    }
}

# ============================================================
#                     FORMULARIO PRINCIPAL
# ============================================================

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Subir Evaluación a GitHub'
$form.Size = New-Object System.Drawing.Size(640, 720)
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

# -- Panel de sesión (esquina superior derecha) --
$grpSesion = New-Object System.Windows.Forms.GroupBox
$grpSesion.Text = 'Cuenta de GitHub'
$grpSesion.Location = New-Object System.Drawing.Point(440, 5)
$grpSesion.Size = New-Object System.Drawing.Size(180, 50)
$form.Controls.Add($grpSesion)

$lblSesionUser = New-Object System.Windows.Forms.Label
$lblSesionUser.Text = 'Verificando...'
$lblSesionUser.Font = New-Object System.Drawing.Font('Segoe UI', 8, [System.Drawing.FontStyle]::Bold)
$lblSesionUser.Location = New-Object System.Drawing.Point(8, 18)
$lblSesionUser.Size = New-Object System.Drawing.Size(165, 14)
$lblSesionUser.ForeColor = [System.Drawing.Color]::Gray
$grpSesion.Controls.Add($lblSesionUser)

$lblSesionEmail = New-Object System.Windows.Forms.Label
$lblSesionEmail.Text = ''
$lblSesionEmail.Font = New-Object System.Drawing.Font('Segoe UI', 7)
$lblSesionEmail.Location = New-Object System.Drawing.Point(8, 32)
$lblSesionEmail.Size = New-Object System.Drawing.Size(165, 14)
$lblSesionEmail.ForeColor = [System.Drawing.Color]::DimGray
$grpSesion.Controls.Add($lblSesionEmail)

$btnLogin = New-Object System.Windows.Forms.Button
$btnLogin.Text = 'Iniciar sesión'
$btnLogin.Location = New-Object System.Drawing.Point(440, 58)
$btnLogin.Size = New-Object System.Drawing.Size(85, 28)
$btnLogin.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
$btnLogin.ForeColor = [System.Drawing.Color]::White
$btnLogin.FlatStyle = 'Flat'
$btnLogin.Font = New-Object System.Drawing.Font('Segoe UI', 8)
$form.Controls.Add($btnLogin)

$btnLogout = New-Object System.Windows.Forms.Button
$btnLogout.Text = 'Cerrar sesión'
$btnLogout.Location = New-Object System.Drawing.Point(530, 58)
$btnLogout.Size = New-Object System.Drawing.Size(90, 28)
$btnLogout.BackColor = [System.Drawing.Color]::FromArgb(198, 40, 40)
$btnLogout.ForeColor = [System.Drawing.Color]::White
$btnLogout.FlatStyle = 'Flat'
$btnLogout.Font = New-Object System.Drawing.Font('Segoe UI', 8)
$btnLogout.Enabled = $false
$form.Controls.Add($btnLogout)

# -- GroupBox: Modo de subida --
$grpModo = New-Object System.Windows.Forms.GroupBox
$grpModo.Text = 'Modo de subida'
$grpModo.Location = New-Object System.Drawing.Point(20, 95)
$grpModo.Size = New-Object System.Drawing.Size(600, 55)
$form.Controls.Add($grpModo)

$rbModoNuevo = New-Object System.Windows.Forms.RadioButton
$rbModoNuevo.Text = 'Crear repositorio nuevo'
$rbModoNuevo.Location = New-Object System.Drawing.Point(15, 22)
$rbModoNuevo.Size = New-Object System.Drawing.Size(220, 22)
$rbModoNuevo.Checked = $true
$grpModo.Controls.Add($rbModoNuevo)

$rbModoExistente = New-Object System.Windows.Forms.RadioButton
$rbModoExistente.Text = 'Usar repositorio existente de mi cuenta'
$rbModoExistente.Location = New-Object System.Drawing.Point(280, 22)
$rbModoExistente.Size = New-Object System.Drawing.Size(300, 22)
$grpModo.Controls.Add($rbModoExistente)

# -- Nombre completo (modo nuevo) --
$lblNombre = New-Object System.Windows.Forms.Label
$lblNombre.Text = 'Nombre completo:'
$lblNombre.Location = New-Object System.Drawing.Point(20, 165)
$lblNombre.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblNombre)

$txtNombre = New-Object System.Windows.Forms.TextBox
$txtNombre.Location = New-Object System.Drawing.Point(180, 163)
$txtNombre.Size = New-Object System.Drawing.Size(420, 22)
$form.Controls.Add($txtNombre)

# -- Forma de prueba (modo nuevo) --
$lblForma = New-Object System.Windows.Forms.Label
$lblForma.Text = 'Forma de prueba:'
$lblForma.Location = New-Object System.Drawing.Point(20, 200)
$lblForma.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblForma)

$cmbForma = New-Object System.Windows.Forms.ComboBox
$cmbForma.Location = New-Object System.Drawing.Point(180, 198)
$cmbForma.Size = New-Object System.Drawing.Size(420, 22)
$cmbForma.DropDownStyle = 'DropDown'
$cmbForma.Items.AddRange(@('Forma-A', 'Forma-B', 'Forma-C', 'Forma-D'))
$form.Controls.Add($cmbForma)

# -- Repositorio existente (modo existente) --
$lblRepoExist = New-Object System.Windows.Forms.Label
$lblRepoExist.Text = 'Tu repositorio:'
$lblRepoExist.Location = New-Object System.Drawing.Point(20, 235)
$lblRepoExist.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblRepoExist)

$cmbReposExistentes = New-Object System.Windows.Forms.ComboBox
$cmbReposExistentes.Location = New-Object System.Drawing.Point(180, 233)
$cmbReposExistentes.Size = New-Object System.Drawing.Size(330, 22)
$cmbReposExistentes.DropDownStyle = 'DropDownList'
$cmbReposExistentes.Enabled = $false
$form.Controls.Add($cmbReposExistentes)

$btnRefreshRepos = New-Object System.Windows.Forms.Button
$btnRefreshRepos.Text = 'Refrescar'
$btnRefreshRepos.Location = New-Object System.Drawing.Point(520, 232)
$btnRefreshRepos.Size = New-Object System.Drawing.Size(80, 25)
$btnRefreshRepos.Enabled = $false
$form.Controls.Add($btnRefreshRepos)

# -- Carpeta de archivos --
$lblCarpeta = New-Object System.Windows.Forms.Label
$lblCarpeta.Text = 'Carpeta del proyecto:'
$lblCarpeta.Location = New-Object System.Drawing.Point(20, 270)
$lblCarpeta.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblCarpeta)

$txtCarpeta = New-Object System.Windows.Forms.TextBox
$txtCarpeta.Location = New-Object System.Drawing.Point(180, 268)
$txtCarpeta.Size = New-Object System.Drawing.Size(330, 22)
$txtCarpeta.ReadOnly = $true
$form.Controls.Add($txtCarpeta)

$btnBuscar = New-Object System.Windows.Forms.Button
$btnBuscar.Text = 'Buscar...'
$btnBuscar.Location = New-Object System.Drawing.Point(520, 267)
$btnBuscar.Size = New-Object System.Drawing.Size(80, 25)
$form.Controls.Add($btnBuscar)

# -- Nombre del repo (preview) --
$lblRepoPreview = New-Object System.Windows.Forms.Label
$lblRepoPreview.Text = 'Repo destino:'
$lblRepoPreview.Location = New-Object System.Drawing.Point(20, 305)
$lblRepoPreview.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblRepoPreview)

$lblRepoValor = New-Object System.Windows.Forms.Label
$lblRepoValor.Text = '(rellenar nombre y forma)'
$lblRepoValor.ForeColor = [System.Drawing.Color]::Gray
$lblRepoValor.Font = New-Object System.Drawing.Font('Consolas', 10)
$lblRepoValor.Location = New-Object System.Drawing.Point(180, 305)
$lblRepoValor.Size = New-Object System.Drawing.Size(420, 20)
$form.Controls.Add($lblRepoValor)

# -- Visibilidad del repo (solo modo nuevo) --
$grpVis = New-Object System.Windows.Forms.GroupBox
$grpVis.Text = 'Visibilidad (solo para repo nuevo)'
$grpVis.Location = New-Object System.Drawing.Point(20, 335)
$grpVis.Size = New-Object System.Drawing.Size(600, 55)
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
$btnCrearRepo.Text = '1. Crear/Validar Repo'
$btnCrearRepo.Location = New-Object System.Drawing.Point(20, 405)
$btnCrearRepo.Size = New-Object System.Drawing.Size(180, 35)
$btnCrearRepo.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
$btnCrearRepo.ForeColor = [System.Drawing.Color]::White
$btnCrearRepo.FlatStyle = 'Flat'
$form.Controls.Add($btnCrearRepo)

$btnSubir = New-Object System.Windows.Forms.Button
$btnSubir.Text = '2. Subir Archivos'
$btnSubir.Location = New-Object System.Drawing.Point(220, 405)
$btnSubir.Size = New-Object System.Drawing.Size(180, 35)
$btnSubir.BackColor = [System.Drawing.Color]::FromArgb(76, 175, 80)
$btnSubir.ForeColor = [System.Drawing.Color]::White
$btnSubir.FlatStyle = 'Flat'
$form.Controls.Add($btnSubir)

$btnTodo = New-Object System.Windows.Forms.Button
$btnTodo.Text = 'Hacer TODO'
$btnTodo.Location = New-Object System.Drawing.Point(420, 405)
$btnTodo.Size = New-Object System.Drawing.Size(180, 35)
$btnTodo.BackColor = [System.Drawing.Color]::FromArgb(255, 87, 34)
$btnTodo.ForeColor = [System.Drawing.Color]::White
$btnTodo.FlatStyle = 'Flat'
$btnTodo.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnTodo)

# -- Log de salida --
$lblLog = New-Object System.Windows.Forms.Label
$lblLog.Text = 'Salida:'
$lblLog.Location = New-Object System.Drawing.Point(20, 455)
$lblLog.Size = New-Object System.Drawing.Size(100, 20)
$form.Controls.Add($lblLog)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(20, 480)
$txtLog.Size = New-Object System.Drawing.Size(600, 170)
$txtLog.Multiline = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.ReadOnly = $true
$txtLog.BackColor = [System.Drawing.Color]::Black
$txtLog.ForeColor = [System.Drawing.Color]::LimeGreen
$txtLog.Font = New-Object System.Drawing.Font('Consolas', 9)
$form.Controls.Add($txtLog)

# -- Status bar --
$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Location = New-Object System.Drawing.Point(20, 660)
$lblStatus.Size = New-Object System.Drawing.Size(600, 20)
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
    if ($rbModoExistente.Checked) {
        if ($cmbReposExistentes.SelectedItem) {
            $lblRepoValor.Text = (Get-RepoName)
            $lblRepoValor.ForeColor = [System.Drawing.Color]::Black
        } else {
            $lblRepoValor.Text = '(selecciona un repositorio de la lista)'
            $lblRepoValor.ForeColor = [System.Drawing.Color]::Gray
        }
        return
    }
    # Modo: crear nuevo
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
    if ($rbModoExistente.Checked) {
        $sel = $cmbReposExistentes.SelectedItem
        if (-not $sel) { return $null }
        # Items vienen en formato "🔒 nombre-del-repo" — quitamos el emoji + espacios
        return ($sel -replace '^\S+\s+', '').Trim()
    }
    $nombre = $txtNombre.Text.Trim()
    $forma = $cmbForma.Text.Trim()
    if (-not $nombre -or -not $forma) { return $null }
    return (Sanitize-RepoName "$nombre-$forma")
}

function Load-UserRepos {
    if (-not (Test-GhAuth)) {
        Log '✗ Sin sesion. Inicia sesion primero para ver tus repos.' 'Red'
        return
    }
    Log '→ Cargando lista de repositorios de tu cuenta...'
    $cmbReposExistentes.Items.Clear()
    Set-Status 'Cargando repos...'
    try {
        $reposJson = gh repo list --json name,visibility,description,isArchived --limit 200 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $reposJson) {
            Log '✗ Error al listar repositorios.' 'Red'
            Set-Status 'Error.'
            return
        }
        $repos = $reposJson | ConvertFrom-Json | Sort-Object name
        $count = 0
        foreach ($r in $repos) {
            if ($r.isArchived) { continue }
            $vis = if ($r.visibility -eq 'PRIVATE') { '[Priv]' } else { '[Pub]' }
            [void]$cmbReposExistentes.Items.Add("$vis $($r.name)")
            $count++
        }
        Log "✓ $count repositorios cargados." 'Green'
        Set-Status "Repos disponibles: $count"
    } catch {
        Log "✗ Error: $_" 'Red'
        Set-Status 'Error.'
    }
}

function Set-ModoUI {
    if ($rbModoExistente.Checked) {
        # Habilitar selector existente
        $cmbReposExistentes.Enabled = $true
        $btnRefreshRepos.Enabled = $true
        # Deshabilitar campos de modo nuevo
        $txtNombre.Enabled = $false
        $cmbForma.Enabled = $false
        $grpVis.Enabled = $false
        # Cambiar texto del boton 1
        $btnCrearRepo.Text = '1. Validar Repo'
        # Cargar repos si no hay items
        if ($cmbReposExistentes.Items.Count -eq 0) {
            Load-UserRepos
        }
    } else {
        # Modo: crear nuevo
        $cmbReposExistentes.Enabled = $false
        $btnRefreshRepos.Enabled = $false
        $txtNombre.Enabled = $true
        $cmbForma.Enabled = $true
        $grpVis.Enabled = $true
        $btnCrearRepo.Text = '1. Crear Repo'
    }
    Update-RepoPreview
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
                "Aún faltan: $($missing -join ', ').`n`nCerrá y reabre el script.",
                'Reiniciar requerido', 'OK', 'Warning') | Out-Null
            return $false
        }
    }

    # gh auth
    if (-not (Test-GhAuth)) {
        $r = [System.Windows.Forms.MessageBox]::Show(
            "No tienes una sesión de GitHub activa.`n`n¿Quieres iniciar sesión ahora?",
            'Sin sesión', 'YesNo', 'Warning')
        if ($r -eq 'Yes') {
            Log '→ Iniciando sesión con código (sin abrir navegador)' 'Yellow'
            $ok = Start-GitHubDeviceLogin
            if (-not $ok) {
                Log '✗ Inicio de sesión cancelado o fallido.' 'Red'
                return $false
            }
            # Re-chequear
            if (-not (Test-GhAuth)) {
                Log '✗ Sesión aún no detectada. Intenta de nuevo.' 'Red'
                return $false
            }
            Update-SessionPanel
        } else {
            return $false
        }
    }

    # Campos requeridos segun modo
    if ($rbModoExistente.Checked) {
        if (-not $cmbReposExistentes.SelectedItem) {
            [System.Windows.Forms.MessageBox]::Show(
                'Selecciona un repositorio de la lista.',
                'Falta dato', 'OK', 'Warning') | Out-Null
            return $false
        }
    } else {
        if (-not $txtNombre.Text.Trim()) {
            [System.Windows.Forms.MessageBox]::Show('Ingresa tu nombre completo.', 'Falta dato', 'OK', 'Warning') | Out-Null
            return $false
        }
        if (-not $cmbForma.Text.Trim()) {
            [System.Windows.Forms.MessageBox]::Show('Selecciona o escribe la forma de la prueba.', 'Falta dato', 'OK', 'Warning') | Out-Null
            return $false
        }
    }

    if ($RequireFolder) {
        if (-not $txtCarpeta.Text -or -not (Test-Path $txtCarpeta.Text)) {
            [System.Windows.Forms.MessageBox]::Show('Selecciona una carpeta válida con tu evaluación.', 'Falta carpeta', 'OK', 'Warning') | Out-Null
            return $false
        }
    }

    return $true
}

function Invoke-CreateRepo {
    if (-not (Validate-Inputs)) { return $false }
    $repo = Get-RepoName

    # Modo: usar repo existente. Solo validar.
    if ($rbModoExistente.Checked) {
        Set-Status "Validando repo $repo..."
        Log "→ Modo: repositorio existente '$repo'"
        $null = gh repo view $repo 2>&1
        if ($LASTEXITCODE -eq 0) {
            Log "✓ Repo '$repo' encontrado en tu cuenta." 'Green'
            Set-Status "Repo $repo OK."
            return $true
        } else {
            Log "✗ No se pudo acceder al repo '$repo'." 'Red'
            [System.Windows.Forms.MessageBox]::Show(
                "No se encontró el repositorio '$repo' en tu cuenta.`n`nRefresca la lista o cambia a modo 'Crear repositorio nuevo'.",
                'Repo no accesible', 'OK', 'Warning') | Out-Null
            return $false
        }
    }

    # Modo: crear nuevo
    $visibility = if ($rbPrivate.Checked) { '--private' } else { '--public' }
    Set-Status "Creando repo $repo..."
    Log "→ Creando repo '$repo' ($visibility)"

    try {
        $null = gh repo view $repo 2>&1
        if ($LASTEXITCODE -eq 0) {
            Log "Repo '$repo' ya existe en tu cuenta. Lo usaremos." 'Yellow'
            return $true
        }

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

# -- Actualizar panel de sesión --
function Update-SessionPanel {
    if (Test-GhAuth) {
        try {
            $userInfo = gh api user 2>$null | ConvertFrom-Json
            $login = $userInfo.login
            $email = if ($userInfo.email) { $userInfo.email } else { "(email privado)" }
            $lblSesionUser.Text = "@$login"
            $lblSesionUser.ForeColor = [System.Drawing.Color]::DarkGreen
            $lblSesionEmail.Text = $email
            $btnLogin.Enabled = $false
            $btnLogout.Enabled = $true
        } catch {
            $lblSesionUser.Text = '(error al consultar)'
            $lblSesionUser.ForeColor = [System.Drawing.Color]::DarkOrange
            $lblSesionEmail.Text = ''
            $btnLogin.Enabled = $true
            $btnLogout.Enabled = $false
        }
    } else {
        $lblSesionUser.Text = 'Sin sesión'
        $lblSesionUser.ForeColor = [System.Drawing.Color]::Gray
        $lblSesionEmail.Text = '(no conectado a GitHub)'
        $btnLogin.Enabled = $true
        $btnLogout.Enabled = $false
    }
    [System.Windows.Forms.Application]::DoEvents()
}

# -- Wiring de eventos --
$txtNombre.Add_TextChanged({ Update-RepoPreview })
$cmbForma.Add_TextChanged({ Update-RepoPreview })
$cmbForma.Add_SelectedIndexChanged({ Update-RepoPreview })

# Modo de subida (radio buttons)
$rbModoNuevo.Add_CheckedChanged({ if ($rbModoNuevo.Checked) { Set-ModoUI } })
$rbModoExistente.Add_CheckedChanged({ if ($rbModoExistente.Checked) { Set-ModoUI } })

# Selector de repo existente
$cmbReposExistentes.Add_SelectedIndexChanged({ Update-RepoPreview })
$btnRefreshRepos.Add_Click({ Load-UserRepos })

$btnBuscar.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Selecciona la carpeta con tu evaluación'
    if ($dlg.ShowDialog() -eq 'OK') {
        $txtCarpeta.Text = $dlg.SelectedPath
        Log "Carpeta seleccionada: $($dlg.SelectedPath)"
    }
})

$btnLogin.Add_Click({
    Log '→ Iniciando sesión con código (sin abrir navegador)...'
    if (Start-GitHubDeviceLogin) {
        Update-SessionPanel
    }
})

$btnLogout.Add_Click({
    if (-not (Test-GhAuth)) {
        Update-SessionPanel
        return
    }
    $ghUser = (gh api user --jq .login 2>$null).Trim()
    $r = [System.Windows.Forms.MessageBox]::Show(
        "Se cerrará la sesión de @$ghUser y se borrarán las credenciales guardadas en este equipo.`n`n¿Confirmas?",
        'Cerrar sesión', 'YesNo', 'Warning')
    if ($r -ne 'Yes') { return }

    Log "→ Cerrando sesión de @$ghUser..."

    # 1. Logout en gh CLI (limpia token guardado por gh)
    gh auth logout --hostname github.com 2>&1 | Out-Null

    # 2. Limpiar Windows Credential Manager (entradas de github.com)
    try {
        $targets = cmdkey /list 2>$null | Select-String -Pattern 'github\.com' | ForEach-Object {
            ($_ -split 'Target:\s*')[1].Trim()
        }
        foreach ($t in $targets) {
            cmdkey /delete:$t 2>&1 | Out-Null
            Log "  borrada credencial: $t"
        }
    } catch {}

    # 3. Limpiar caché del git credential helper
    git credential-cache exit 2>$null | Out-Null

    Log '✓ Sesión cerrada y credenciales borradas.' 'Green'
    Update-SessionPanel
    [System.Windows.Forms.MessageBox]::Show(
        'Sesión cerrada. Ahora puedes iniciar sesión con otra cuenta usando el botón "Iniciar sesión".',
        'Listo', 'OK', 'Information') | Out-Null
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
Log 'Listo. Completa los datos y elige una acción.'
Log 'Tip: usa "Hacer TODO" si es la primera vez.' 'Cyan'

# Chequeo proactivo de dependencias al abrir
$initMissing = Test-Dependencies
if ($initMissing) {
    Log "⚠ Dependencias faltantes detectadas: $($initMissing -join ', ')" 'Yellow'
    Log '  Se te ofrecerá instalarlas la primera vez que uses una acción.' 'Yellow'
} else {
    Log '✓ git y gh detectados.'
    if (Test-GhAuth) {
        Log '✓ Sesión de GitHub activa.'
    } else {
        Log '⚠ Sin sesión de GitHub. Inicia sesión con el botón superior derecho.' 'Yellow'
    }
}

# Llenar panel de sesión con datos actuales
Update-SessionPanel

[void]$form.ShowDialog()
