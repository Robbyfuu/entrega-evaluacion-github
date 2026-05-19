<#
.SYNOPSIS
    Panel de administración remoto para el profesor.

.DESCRIPTION
    Login con cuenta Supabase (email docente + password). Una vez autenticado,
    panel con botones para:
    - Activar/desactivar bloqueo de internet en todos los PCs alumnos
    - Activar lockdown remoto (dispara ventana roja en todos los PCs)
    - Enviar mensaje arbitrario a todos los alumnos
    - Ver eventos de trampa detectados
    - Refrescar estado actual

.NOTES
    Requiere conexion a internet y credenciales del proyecto Supabase configuradas
    en las constantes de abajo.
#>

# Auto-desbloqueo Zone.Identifier
try {
    Get-ChildItem -Path $PSScriptRoot -File -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue
} catch {}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# ============================================================
#                   CONSTANTES SUPABASE
# ============================================================
$script:supabaseUrl = 'https://oiownlxyquarmqwauegf.supabase.co'
$script:supabaseAnonKey = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im9pb3dubHh5cXVhcm1xd2F1ZWdmIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzkyMDk5NTEsImV4cCI6MjA5NDc4NTk1MX0.MMODHCBz_xl3gnzJVfY-aIQPyINQDkwXyr-e6KPtrm4'

$script:accessToken = $null
$script:refreshToken = $null
$script:userEmail = $null

# ============================================================
#                    FUNCIONES HELPER
# ============================================================

function Get-AuthHeaders {
    param([switch]$Authenticated)
    $headers = @{
        'apikey'       = $script:supabaseAnonKey
        'Content-Type' = 'application/json'
    }
    if ($Authenticated -and $script:accessToken) {
        $headers['Authorization'] = "Bearer $($script:accessToken)"
    } else {
        $headers['Authorization'] = "Bearer $($script:supabaseAnonKey)"
    }
    return $headers
}

function Supabase-Login {
    param([string]$Email, [string]$Password)
    try {
        $body = @{ email = $Email; password = $Password } | ConvertTo-Json
        $resp = Invoke-RestMethod `
            -Uri "$($script:supabaseUrl)/auth/v1/token?grant_type=password" `
            -Method Post `
            -Headers @{
                'apikey'       = $script:supabaseAnonKey
                'Content-Type' = 'application/json'
            } `
            -Body $body `
            -TimeoutSec 10
        $script:accessToken = $resp.access_token
        $script:refreshToken = $resp.refresh_token
        $script:userEmail = $resp.user.email
        return @{ Ok = $true; Email = $script:userEmail }
    } catch {
        $errBody = ''
        try {
            $errBody = $_.ErrorDetails.Message
        } catch {}
        return @{ Ok = $false; Error = $errBody; Exception = $_.Exception.Message }
    }
}

function Supabase-Logout {
    $script:accessToken = $null
    $script:refreshToken = $null
    $script:userEmail = $null
}

function Supabase-GetControl {
    try {
        $resp = Invoke-RestMethod `
            -Uri "$($script:supabaseUrl)/rest/v1/control?id=eq.1&select=*" `
            -Method Get `
            -Headers (Get-AuthHeaders) `
            -TimeoutSec 10
        if ($resp.Count -gt 0) { return $resp[0] }
        return $null
    } catch {
        return $null
    }
}

function Supabase-UpdateControl {
    param(
        [Nullable[bool]]$InternetBlock,
        [Nullable[bool]]$ForceLockdown,
        [string]$Message
    )
    if (-not $script:accessToken) {
        return @{ Ok = $false; Error = 'No hay sesion activa.' }
    }
    $patch = @{}
    if ($null -ne $InternetBlock)   { $patch['internet_block']   = $InternetBlock }
    if ($null -ne $ForceLockdown)   { $patch['force_lockdown']   = $ForceLockdown }
    if ($PSBoundParameters.ContainsKey('Message')) { $patch['message'] = $Message }
    $patch['updated_by'] = $script:userEmail

    try {
        $resp = Invoke-RestMethod `
            -Uri "$($script:supabaseUrl)/rest/v1/control?id=eq.1" `
            -Method Patch `
            -Headers (Get-AuthHeaders -Authenticated) `
            -Body ($patch | ConvertTo-Json) `
            -TimeoutSec 10
        return @{ Ok = $true; Data = $resp }
    } catch {
        $msg = ''
        try { $msg = $_.ErrorDetails.Message } catch {}
        return @{ Ok = $false; Error = $msg; Exception = $_.Exception.Message }
    }
}

function Supabase-GetCheatEvents {
    if (-not $script:accessToken) { return @() }
    try {
        $resp = Invoke-RestMethod `
            -Uri "$($script:supabaseUrl)/rest/v1/cheat_events?select=*&order=detected_at.desc&limit=50" `
            -Method Get `
            -Headers (Get-AuthHeaders -Authenticated) `
            -TimeoutSec 10
        return @($resp)
    } catch {
        return @()
    }
}

# ============================================================
#                    DIALOG LOGIN
# ============================================================

function Show-LoginDialog {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Panel del Profesor - Inicio de sesión'
    $dlg.Size = New-Object System.Drawing.Size(450, 280)
    $dlg.StartPosition = 'CenterScreen'
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false

    $lblTitle = New-Object System.Windows.Forms.Label
    $lblTitle.Text = 'Panel del Profesor'
    $lblTitle.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $lblTitle.Location = New-Object System.Drawing.Point(20, 15)
    $lblTitle.Size = New-Object System.Drawing.Size(400, 30)
    $dlg.Controls.Add($lblTitle)

    $lblSub = New-Object System.Windows.Forms.Label
    $lblSub.Text = 'Ingresa tus credenciales docentes.'
    $lblSub.ForeColor = [System.Drawing.Color]::DimGray
    $lblSub.Location = New-Object System.Drawing.Point(20, 45)
    $lblSub.Size = New-Object System.Drawing.Size(400, 20)
    $dlg.Controls.Add($lblSub)

    $lblEmail = New-Object System.Windows.Forms.Label
    $lblEmail.Text = 'Correo:'
    $lblEmail.Location = New-Object System.Drawing.Point(20, 80)
    $lblEmail.Size = New-Object System.Drawing.Size(100, 22)
    $dlg.Controls.Add($lblEmail)

    $txtEmail = New-Object System.Windows.Forms.TextBox
    $txtEmail.Location = New-Object System.Drawing.Point(130, 78)
    $txtEmail.Size = New-Object System.Drawing.Size(280, 25)
    $txtEmail.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $dlg.Controls.Add($txtEmail)

    $lblPwd = New-Object System.Windows.Forms.Label
    $lblPwd.Text = 'Contraseña:'
    $lblPwd.Location = New-Object System.Drawing.Point(20, 115)
    $lblPwd.Size = New-Object System.Drawing.Size(100, 22)
    $dlg.Controls.Add($lblPwd)

    $txtPwd = New-Object System.Windows.Forms.TextBox
    $txtPwd.Location = New-Object System.Drawing.Point(130, 113)
    $txtPwd.Size = New-Object System.Drawing.Size(280, 25)
    $txtPwd.Font = New-Object System.Drawing.Font('Segoe UI', 10)
    $txtPwd.PasswordChar = '*'
    $dlg.Controls.Add($txtPwd)

    $lblErr = New-Object System.Windows.Forms.Label
    $lblErr.Text = ''
    $lblErr.ForeColor = [System.Drawing.Color]::Red
    $lblErr.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $lblErr.Location = New-Object System.Drawing.Point(20, 150)
    $lblErr.Size = New-Object System.Drawing.Size(400, 30)
    $dlg.Controls.Add($lblErr)

    $btnLogin = New-Object System.Windows.Forms.Button
    $btnLogin.Text = 'Iniciar sesión'
    $btnLogin.Location = New-Object System.Drawing.Point(130, 195)
    $btnLogin.Size = New-Object System.Drawing.Size(150, 32)
    $btnLogin.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
    $btnLogin.ForeColor = [System.Drawing.Color]::White
    $btnLogin.FlatStyle = 'Flat'
    $btnLogin.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
    $btnLogin.Add_Click({
        $lblErr.Text = ''
        $btnLogin.Enabled = $false
        $btnLogin.Text = 'Verificando...'
        [System.Windows.Forms.Application]::DoEvents()
        $r = Supabase-Login -Email $txtEmail.Text.Trim() -Password $txtPwd.Text
        if ($r.Ok) {
            $dlg.DialogResult = 'OK'
            $dlg.Close()
        } else {
            $msg = 'Credenciales invalidas o sin conexion.'
            if ($r.Error -match 'Invalid login') { $msg = 'Email o password incorrectos.' }
            if ($r.Exception -match 'Unable to connect') { $msg = 'Sin conexion a internet.' }
            $lblErr.Text = $msg
            $btnLogin.Enabled = $true
            $btnLogin.Text = 'Iniciar sesión'
            $txtPwd.Text = ''
            $txtPwd.Focus()
        }
    })
    $dlg.Controls.Add($btnLogin)
    $dlg.AcceptButton = $btnLogin

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'Cancelar'
    $btnCancel.Location = New-Object System.Drawing.Point(290, 195)
    $btnCancel.Size = New-Object System.Drawing.Size(100, 32)
    $btnCancel.DialogResult = 'Cancel'
    $dlg.Controls.Add($btnCancel)
    $dlg.CancelButton = $btnCancel

    $txtEmail.Focus()
    return ($dlg.ShowDialog() -eq 'OK')
}

# ============================================================
#                    FORM PRINCIPAL ADMIN
# ============================================================

function Show-AdminPanel {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "Panel del Profesor — $($script:userEmail)"
    $form.Size = New-Object System.Drawing.Size(820, 680)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false

    # Header
    $lblHdr = New-Object System.Windows.Forms.Label
    $lblHdr.Text = "Conectado como: $($script:userEmail)"
    $lblHdr.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
    $lblHdr.Location = New-Object System.Drawing.Point(20, 15)
    $lblHdr.Size = New-Object System.Drawing.Size(500, 25)
    $form.Controls.Add($lblHdr)

    $btnLogout = New-Object System.Windows.Forms.Button
    $btnLogout.Text = 'Cerrar sesión'
    $btnLogout.Location = New-Object System.Drawing.Point(670, 12)
    $btnLogout.Size = New-Object System.Drawing.Size(120, 30)
    $btnLogout.BackColor = [System.Drawing.Color]::FromArgb(198, 40, 40)
    $btnLogout.ForeColor = [System.Drawing.Color]::White
    $btnLogout.FlatStyle = 'Flat'
    $form.Controls.Add($btnLogout)

    # ===== Bloque: Estado actual =====
    $grpEstado = New-Object System.Windows.Forms.GroupBox
    $grpEstado.Text = 'Estado actual'
    $grpEstado.Location = New-Object System.Drawing.Point(20, 55)
    $grpEstado.Size = New-Object System.Drawing.Size(770, 110)
    $form.Controls.Add($grpEstado)

    $lblEstInternet = New-Object System.Windows.Forms.Label
    $lblEstInternet.Text = 'Internet: ...'
    $lblEstInternet.Font = New-Object System.Drawing.Font('Consolas', 10, [System.Drawing.FontStyle]::Bold)
    $lblEstInternet.Location = New-Object System.Drawing.Point(15, 25)
    $lblEstInternet.Size = New-Object System.Drawing.Size(400, 22)
    $grpEstado.Controls.Add($lblEstInternet)

    $lblEstLockdown = New-Object System.Windows.Forms.Label
    $lblEstLockdown.Text = 'Lockdown: ...'
    $lblEstLockdown.Font = New-Object System.Drawing.Font('Consolas', 10, [System.Drawing.FontStyle]::Bold)
    $lblEstLockdown.Location = New-Object System.Drawing.Point(15, 50)
    $lblEstLockdown.Size = New-Object System.Drawing.Size(400, 22)
    $grpEstado.Controls.Add($lblEstLockdown)

    $lblEstMsg = New-Object System.Windows.Forms.Label
    $lblEstMsg.Text = 'Mensaje activo: (ninguno)'
    $lblEstMsg.Font = New-Object System.Drawing.Font('Consolas', 9)
    $lblEstMsg.Location = New-Object System.Drawing.Point(15, 75)
    $lblEstMsg.Size = New-Object System.Drawing.Size(740, 22)
    $grpEstado.Controls.Add($lblEstMsg)

    $btnRefresh = New-Object System.Windows.Forms.Button
    $btnRefresh.Text = 'Refrescar'
    $btnRefresh.Location = New-Object System.Drawing.Point(670, 22)
    $btnRefresh.Size = New-Object System.Drawing.Size(85, 28)
    $grpEstado.Controls.Add($btnRefresh)

    # ===== Bloque: Controles =====
    $grpCtl = New-Object System.Windows.Forms.GroupBox
    $grpCtl.Text = 'Controles remotos (afectan a todos los alumnos en <60s)'
    $grpCtl.Location = New-Object System.Drawing.Point(20, 175)
    $grpCtl.Size = New-Object System.Drawing.Size(770, 180)
    $form.Controls.Add($grpCtl)

    $btnInternetOn = New-Object System.Windows.Forms.Button
    $btnInternetOn.Text = 'Bloquear internet'
    $btnInternetOn.Location = New-Object System.Drawing.Point(15, 25)
    $btnInternetOn.Size = New-Object System.Drawing.Size(180, 38)
    $btnInternetOn.BackColor = [System.Drawing.Color]::FromArgb(198, 40, 40)
    $btnInternetOn.ForeColor = [System.Drawing.Color]::White
    $btnInternetOn.FlatStyle = 'Flat'
    $btnInternetOn.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $grpCtl.Controls.Add($btnInternetOn)

    $btnInternetOff = New-Object System.Windows.Forms.Button
    $btnInternetOff.Text = 'Desbloquear internet'
    $btnInternetOff.Location = New-Object System.Drawing.Point(205, 25)
    $btnInternetOff.Size = New-Object System.Drawing.Size(180, 38)
    $btnInternetOff.BackColor = [System.Drawing.Color]::FromArgb(76, 175, 80)
    $btnInternetOff.ForeColor = [System.Drawing.Color]::White
    $btnInternetOff.FlatStyle = 'Flat'
    $btnInternetOff.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $grpCtl.Controls.Add($btnInternetOff)

    $btnLockOn = New-Object System.Windows.Forms.Button
    $btnLockOn.Text = 'LOCKDOWN remoto'
    $btnLockOn.Location = New-Object System.Drawing.Point(395, 25)
    $btnLockOn.Size = New-Object System.Drawing.Size(180, 38)
    $btnLockOn.BackColor = [System.Drawing.Color]::FromArgb(183, 28, 28)
    $btnLockOn.ForeColor = [System.Drawing.Color]::White
    $btnLockOn.FlatStyle = 'Flat'
    $btnLockOn.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $grpCtl.Controls.Add($btnLockOn)

    $btnLockOff = New-Object System.Windows.Forms.Button
    $btnLockOff.Text = 'Liberar lockdown'
    $btnLockOff.Location = New-Object System.Drawing.Point(585, 25)
    $btnLockOff.Size = New-Object System.Drawing.Size(170, 38)
    $btnLockOff.BackColor = [System.Drawing.Color]::FromArgb(96, 125, 139)
    $btnLockOff.ForeColor = [System.Drawing.Color]::White
    $btnLockOff.FlatStyle = 'Flat'
    $grpCtl.Controls.Add($btnLockOff)

    # Mensaje
    $lblMsg = New-Object System.Windows.Forms.Label
    $lblMsg.Text = 'Mensaje al aula:'
    $lblMsg.Location = New-Object System.Drawing.Point(15, 80)
    $lblMsg.Size = New-Object System.Drawing.Size(150, 22)
    $grpCtl.Controls.Add($lblMsg)

    $txtMsg = New-Object System.Windows.Forms.TextBox
    $txtMsg.Location = New-Object System.Drawing.Point(15, 105)
    $txtMsg.Size = New-Object System.Drawing.Size(560, 25)
    $grpCtl.Controls.Add($txtMsg)

    $btnSendMsg = New-Object System.Windows.Forms.Button
    $btnSendMsg.Text = 'Enviar mensaje'
    $btnSendMsg.Location = New-Object System.Drawing.Point(585, 103)
    $btnSendMsg.Size = New-Object System.Drawing.Size(170, 28)
    $btnSendMsg.BackColor = [System.Drawing.Color]::FromArgb(33, 150, 243)
    $btnSendMsg.ForeColor = [System.Drawing.Color]::White
    $btnSendMsg.FlatStyle = 'Flat'
    $grpCtl.Controls.Add($btnSendMsg)

    $btnClearMsg = New-Object System.Windows.Forms.Button
    $btnClearMsg.Text = 'Borrar mensaje activo'
    $btnClearMsg.Location = New-Object System.Drawing.Point(15, 140)
    $btnClearMsg.Size = New-Object System.Drawing.Size(180, 28)
    $grpCtl.Controls.Add($btnClearMsg)

    # ===== Bloque: Eventos de trampa =====
    $grpEv = New-Object System.Windows.Forms.GroupBox
    $grpEv.Text = 'Eventos de trampa detectados (últimos 50)'
    $grpEv.Location = New-Object System.Drawing.Point(20, 365)
    $grpEv.Size = New-Object System.Drawing.Size(770, 260)
    $form.Controls.Add($grpEv)

    $grid = New-Object System.Windows.Forms.DataGridView
    $grid.Location = New-Object System.Drawing.Point(15, 25)
    $grid.Size = New-Object System.Drawing.Size(740, 195)
    $grid.ReadOnly = $true
    $grid.AllowUserToAddRows = $false
    $grid.SelectionMode = 'FullRowSelect'
    $grid.AutoSizeColumnsMode = 'Fill'
    $grid.ColumnCount = 5
    $grid.Columns[0].Name = 'Fecha'
    $grid.Columns[1].Name = 'Usuario GH'
    $grid.Columns[2].Name = 'PC'
    $grid.Columns[3].Name = 'Repo'
    $grid.Columns[4].Name = 'Archivos'
    $grpEv.Controls.Add($grid)

    $btnRefreshEv = New-Object System.Windows.Forms.Button
    $btnRefreshEv.Text = 'Refrescar eventos'
    $btnRefreshEv.Location = New-Object System.Drawing.Point(15, 225)
    $btnRefreshEv.Size = New-Object System.Drawing.Size(180, 28)
    $grpEv.Controls.Add($btnRefreshEv)

    # ===== Funciones internas =====
    function Refresh-State {
        $ctl = Supabase-GetControl
        if ($ctl) {
            $lblEstInternet.Text = "Internet bloqueado: $(if($ctl.internet_block){'SI'}else{'no'})"
            $lblEstInternet.ForeColor = if ($ctl.internet_block) {
                [System.Drawing.Color]::FromArgb(198, 40, 40)
            } else { [System.Drawing.Color]::Black }

            $lblEstLockdown.Text = "Lockdown remoto: $(if($ctl.force_lockdown){'ACTIVO'}else{'inactivo'})"
            $lblEstLockdown.ForeColor = if ($ctl.force_lockdown) {
                [System.Drawing.Color]::FromArgb(198, 40, 40)
            } else { [System.Drawing.Color]::Black }

            $msgText = if ($ctl.message) { $ctl.message } else { '(ninguno)' }
            $lblEstMsg.Text = "Mensaje activo: $msgText"
        } else {
            $lblEstInternet.Text = 'Internet: error consultando'
            $lblEstLockdown.Text = 'Lockdown: error consultando'
        }
    }

    function Refresh-Events {
        $grid.Rows.Clear()
        $events = Supabase-GetCheatEvents
        foreach ($e in $events) {
            $sample = if ($e.files_sample) { ($e.files_sample -join ', ') } else { '' }
            if ($sample.Length -gt 60) { $sample = $sample.Substring(0, 60) + '...' }
            [void]$grid.Rows.Add(
                $e.detected_at, $e.username, $e.pc_name, $e.repo_name, "$($e.files_count): $sample"
            )
        }
    }

    function Do-Update {
        param([hashtable]$Patch)
        $r = Supabase-UpdateControl @Patch
        if ($r.Ok) {
            Refresh-State
        } else {
            [System.Windows.Forms.MessageBox]::Show(
                "Error: $($r.Error)`n`n$($r.Exception)",
                'Error en Supabase', 'OK', 'Error') | Out-Null
        }
    }

    # ===== Wiring =====
    $btnRefresh.Add_Click({ Refresh-State })
    $btnRefreshEv.Add_Click({ Refresh-Events })
    $btnInternetOn.Add_Click({  Do-Update -Patch @{ InternetBlock = $true  } })
    $btnInternetOff.Add_Click({ Do-Update -Patch @{ InternetBlock = $false } })
    $btnLockOn.Add_Click({
        $r = [System.Windows.Forms.MessageBox]::Show(
            'Esto activara el lockdown rojo en TODOS los PCs conectados. ¿Confirmas?',
            'Confirmar lockdown remoto', 'YesNo', 'Warning')
        if ($r -eq 'Yes') { Do-Update -Patch @{ ForceLockdown = $true } }
    })
    $btnLockOff.Add_Click({ Do-Update -Patch @{ ForceLockdown = $false } })
    $btnSendMsg.Add_Click({
        if (-not $txtMsg.Text.Trim()) {
            [System.Windows.Forms.MessageBox]::Show('Escribe un mensaje.', 'Vacio', 'OK', 'Warning') | Out-Null
            return
        }
        Do-Update -Patch @{ Message = $txtMsg.Text.Trim() }
        $txtMsg.Text = ''
    })
    $btnClearMsg.Add_Click({ Do-Update -Patch @{ Message = '' } })
    $btnLogout.Add_Click({
        Supabase-Logout
        $form.Close()
    })

    # Polling de eventos cada 30s
    $evTimer = New-Object System.Windows.Forms.Timer
    $evTimer.Interval = 30000
    $evTimer.Add_Tick({ Refresh-State; Refresh-Events })
    $evTimer.Start()

    Refresh-State
    Refresh-Events
    [void]$form.ShowDialog()
    $evTimer.Stop()
    $evTimer.Dispose()
}

# ============================================================
#                       MAIN
# ============================================================

if (Show-LoginDialog) {
    Show-AdminPanel
}
