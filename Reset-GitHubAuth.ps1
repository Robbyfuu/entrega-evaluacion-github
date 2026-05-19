<#
.SYNOPSIS
    Limpia credenciales de GitHub (Credential Manager + gh CLI + git config opcional)
    y permite loguearse con cuenta nueva.

.DESCRIPTION
    - Borra entradas de github.com del Windows Credential Manager
    - Cierra sesión en gh CLI (si está instalado)
    - Opcionalmente limpia git config global (user.name/user.email)
    - Inicia flujo de login nuevo con gh auth login

.PARAMETER ResetGitConfig
    Si se pasa, también limpia user.name y user.email del git config global.

.PARAMETER SkipLogin
    Si se pasa, NO ejecuta gh auth login al final.

.EXAMPLE
    .\Reset-GitHubAuth.ps1
    .\Reset-GitHubAuth.ps1 -ResetGitConfig
    .\Reset-GitHubAuth.ps1 -SkipLogin
#>

[CmdletBinding()]
param(
    [switch]$ResetGitConfig,
    [switch]$SkipLogin
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "    [OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "    [WARN] $Message" -ForegroundColor Yellow
}

# 1. Limpiar Windows Credential Manager
Write-Step "Borrando credenciales de github.com del Credential Manager"
try {
    $targets = cmdkey /list | Select-String -Pattern 'github\.com' | ForEach-Object {
        ($_ -split 'Target:\s*')[1].Trim()
    }

    if ($targets) {
        foreach ($t in $targets) {
            cmdkey /delete:$t | Out-Null
            Write-Ok "Borrado: $t"
        }
    } else {
        Write-Warn "No se encontraron credenciales de github.com"
    }
} catch {
    Write-Warn "Error accediendo Credential Manager: $_"
}

# 2. gh CLI logout
Write-Step "Cerrando sesión en gh CLI"
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    try {
        $accounts = gh auth status 2>&1 | Select-String -Pattern 'Logged in to github\.com account (\S+)' | ForEach-Object {
            $_.Matches[0].Groups[1].Value
        }

        if ($accounts) {
            foreach ($acc in $accounts) {
                gh auth logout --hostname github.com --user $acc 2>&1 | Out-Null
                Write-Ok "Logout: $acc"
            }
        } else {
            Write-Warn "No hay sesiones activas en gh CLI"
        }
    } catch {
        Write-Warn "gh logout falló: $_"
    }
} else {
    Write-Warn "gh CLI no instalado (skip)"
}

# 3. Opcional: limpiar git config global
if ($ResetGitConfig) {
    Write-Step "Limpiando git config global (user.name / user.email)"
    try {
        git config --global --unset user.name 2>$null
        git config --global --unset user.email 2>$null
        Write-Ok "git config limpiado"
    } catch {
        Write-Warn "Error limpiando git config: $_"
    }
}

# 4. Limpiar caché del git credential helper
Write-Step "Limpiando caché del git credential helper"
try {
    git credential-cache exit 2>$null
    Write-Ok "Caché del helper limpiada"
} catch {
    Write-Warn "credential-cache no disponible (normal en Windows con manager)"
}

# 5. Login nuevo
if (-not $SkipLogin) {
    Write-Step "Iniciando login nuevo en gh CLI"
    if ($gh) {
        gh auth login --hostname github.com --web --git-protocol https
    } else {
        Write-Warn "gh CLI no instalado. Instalar desde: https://cli.github.com/"
        Write-Warn "O loguearse manualmente en el próximo 'git push' (Credential Manager pedirá creds)"
    }
} else {
    Write-Host ""
    Write-Host "Credenciales limpiadas. Para loguearse:" -ForegroundColor Cyan
    Write-Host "  gh auth login" -ForegroundColor White
    Write-Host "  o hacer 'git push' y autenticarse en el prompt" -ForegroundColor White
}

Write-Host ""
Write-Host "Listo." -ForegroundColor Green
