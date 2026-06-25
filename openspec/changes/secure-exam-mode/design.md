# Diseno: Modo de Evaluacion Segura

## 1. Auditoria del estado actual (codigo real)

### Controles existentes
| Control | Archivo | Que hace | Limite |
|---|---|---|---|
| Internet SoftLock | `Services/InternetBlockService.cs` | cierra navegadores + proxy HKCU `127.0.0.1:1`; reporta `internet_state` | solo apps que respetan proxy del sistema; CLI/WSL/VPN/clientes directos lo ignoran |
| Copilot/AI | `Services/CopilotBlockService.cs` | sabotea `settings.json` (chat.disableAIFeatures + claves Copilot) en Code/Insiders/VSCodium + perfiles; watchers + timer 5s; re-aplica y dispara cheat | NO atomico, re-serializa (pierde JSONC/orden/comentarios), no restaura el original EXACTO; auto-lockdown ante cualquier edicion; no cubre WSL/Remote/web/portable |
| Procesos | `Services/ProcessMonitor.cs` | procesos con ventana visible; `IsSuspicious` por nombre | no ve background/CLI; nombre renombrable; sin firma/ruta |
| Navegador embebido | `Windows/WebBrowserWindow.xaml.cs` + `allowed_urls` | allowlist dominio/exact-url; navegacion prohibida -> trampa | `github.com` completo abre Copilot/search/repos ajenos |
| Daemon | `Services/DaemonService.cs` | Scheduled Task relanza cada 3 min | ventana de ~3 min; user-level (borrable) |
| Cierre bloqueado | `Windows/MainWindow.xaml.cs` `OnClosing` (v2.7.8) | cancela cierre + aviso + reporta; escape por clave | no frena kill por Task Manager (lo cubre el daemon) |
| Lockdown / pantalla roja | `Windows/CheatWindow.xaml.cs` + `targeted_lockdowns` | kiosk; remoto/dirigido/trampa-local; liberable remoto (v2.7.4) | trampa local persistente solo se libera con clave si el alumno reinicia |
| Git | `Services/GitService.cs` | clone/commit/push SIEMPRE a `main` (force); `TestRepoIsClean` anti-repo-sucio | sin baseline SHA registrado; sin validacion de remoto en submit |
| Repos | org `Fundamentos-de-la-Programacion` | repos PUBLICOS | copia entre alumnos; OAuth App de la org bloquea el token del cliente |
| Update | `Services/UpdateService.cs` (v2.7.7) | Velopack autenticado con token del alumno (fix rate-limit por IP) | — |

### Estado disperso (sin fuente de verdad)
`_remoteLockdownActive`, `_targetedLockdownActive`, `_allowExit`, `_blocklist`, `_allowlist`,
`_lastKnownLock`... = logica de sesion repartida en bools. No hay `ExamSessionId`, ni
baseline, ni hora de servidor para el inicio.

## 2. Modelo de amenaza (resumen)
Laboratorio administrado, alumno sin admin. Intenta: VS Code (stable/Insiders/portable/
perfil), navegadores, apps IA/mensajeria, editar settings.json, terminal/PowerShell/CMD/
Python/Node/curl/WSL/Git, matar la app, cambiar red/VPN/proxy, repo preparado, repos
publicos ajenos, crash/reinicio. **Fuera de alcance del software:** telefono, 2do equipo,
hotspot, colaboracion presencial, admin/acceso fisico, codigo memorizado.

## 3. Riesgos priorizados

| Riesgo | Control actual | Evasion | Severidad | Solucion | Capa |
|---|---|---|---:|---|---|
| Copia entre alumnos | repos publicos | clonar repo ajeno | CRITICA | repos PRIVADOS (Classroom/org) + repo nuevo por examen | GitHub |
| Repo preparado | "usar repo existente" permitido | codigo previo | ALTA | repo nuevo + baseline SHA + hora servidor | Integridad |
| Sin estado de sesion | bools dispersos | inconsistencia/recovery fragil | ALTA | `IExamSessionService` persistente | App |
| Navegador ignora proxy | proxy HKCU | CLI/WSL/VPN/directo | ALTA | egress administrado (HardLock) ; declarar HKCU como blando | Red/TI |
| `github.com` completo en WebView | allowlist amplia | Copilot/search/repos ajenos | ALTA | cerrar WebView en ExamActive; allowlist FQDN Copilot | Red/App |
| Copilot integrado VS Code | settings.json | perfil/portable/Insiders/WSL | ALTA | si IDLE -> bloquear VS Code; si VS Code -> politica administrada + sabotaje | IDE/SO |
| settings.json: no atomico/JSONC/restore | re-serializa | corrompe config alumno; no restaura exacto | MEDIA | snapshot exacto + escritura atomica (temp+rename) + restore literal | App |
| Auto-lockdown por editar settings | OnCheatDetected->lockdown | falso positivo / debido proceso | MEDIA | `policy_tamper_event` para revision (no sancion auto) | App/Politica |
| ProcessMonitor parcial | solo ventana | background/CLI/renombrado | MEDIA | enumerar sin-ventana + firma/ruta; allow/block por eval; AuditOnly | App |
| Recovery / reglas huerfanas | restore parcial | crash deja proxy/lockdown | MEDIA | Apply/Verify/Restore/Recover por control + tool de limpieza TI | App/TI |

## 4. Arquitectura recomendada

```
                  Panel admin-next  (feature flag: Off/AuditOnly/SoftLock/HardLock por eval)
                          | Supabase (politicas, eventos, RLS, hora de servidor)
                          v
  +----------------------- Cliente WPF (asInvoker) -----------------------+
  | IExamSessionService  (maquina de estados = FUENTE DE VERDAD)          |
  |   -> orquesta controles via interfaz comun:                          |
  |        Apply / Verify / Restore / Recover                            |
  |   controles: RepoIntegrity, EditorPolicy, Network(SoftLock),         |
  |              ProcessMonitor, WebViewLock, AuditQueue                  |
  +-----------------------------------|----------------------------------+
                                      | IPC named pipe (firma + ACL + sesion)
                                      v
              Servicio Windows privilegiado (HardLock, instalado por TI)
              firewall transaccional + watchdog + expiry + Event Log
                                      |
              Infra institucional: Intune/GPO/AppLocker/WDAC/VLAN/DNS/proxy
```

### 4.1 Maquina de estados (Etapa 1)
Estados: `Idle, Preflight, Ready, ExamActive, SubmissionOpening, Submitting, Completed,
RecoveryRequired, AbortedByTeacher`. Transiciones validas + testeables. `ExamSessionId`
unico, hora de inicio por servidor, persistencia local minima (idempotente al reiniciar),
indicador visible permanente en `ExamActive`, cierre autorizado solo por flujo docente.

```csharp
public interface IExamSessionService {
  ExamState CurrentState { get; }
  Task<ExamSession> StartAsync(StartExamRequest r, CancellationToken ct);
  Task BeginSubmissionAsync(CancellationToken ct);
  Task CompleteAsync(CancellationToken ct);
  Task AbortAsync(TeacherAuthorization auth, CancellationToken ct);
  Task RecoverAsync(CancellationToken ct);
}
public interface IExamControl { // cada control lo implementa
  Task ApplyAsync(ExamPolicy p, CancellationToken ct);
  Task<ControlStatus> VerifyAsync(CancellationToken ct);
  Task RestoreAsync(CancellationToken ct);   // restaura snapshot EXACTO
  Task RecoverAsync(CancellationToken ct);   // idempotente tras crash
}
```

### 4.2 Integridad de repo (Etapa 2 — prioridad)
Calificadas: deshabilitar "usar repo existente"; crear repo nuevo; preferir privado
(Classroom/org); baseline SHA inicial; hora de servidor; ligar a `ExamSessionId`; validar
remoto esperado en submit (rechazar remotos extra, submodulos, workflows, binarios fuera
de politica, historial previo); guardar SHA inicial/final. NO usar timestamps de commit
como unica evidencia.

### 4.3 Editor / IA (Etapa 3)
- IDLE autorizado -> `ide_not_allowed_started` + bloquear/reportar VS Code (verificable).
- VS Code requerido -> CopilotBlockService (corregido) + nivel efectivo:
  `NotApplicable|SoftDisabled|PolicyEnforced|ProcessBlocked|NetworkBlocked|Unknown`
  (`Unknown` != protegido). Politica administrada (Intune/AllowedExtensions) en HardLock.

### 4.4 Red (Etapa 4)
- Nivel A SoftLock (cliente): proxy HKCU con snapshot/restore (declarado blando), cerrar
  WebView en ExamActive, monitoreo, cola offline.
- Nivel B HardLock (servicio TI): firewall transaccional, IPC limitado
  (`StartExamPolicy/OpenSubmissionWindow/CloseSubmissionWindow/StopExamPolicy/GetStatus/
  Recover`), validacion de firma/version/sesion/caller, snapshot+rollback, watchdog,
  expiry, recovery, Event Log. Alternativas institucionales: GPO/Intune/AppLocker/WDAC/
  firewall-proxy de lab/VLAN/DNS/bloqueo VPN-DoH.

### 4.5 Copilot FQDN (Etapa 5)
Allowlist/denylist FQDN versionada y central (panel). NO bloquear `github.com` generico
(rompe clone/push/auth). Documentar que FQDN != offline total. Endpoints Copilot conocidos
(re-verificar en docs): `github.com/copilot/*`, `api.github.com/copilot_internal/*`,
`*.githubcopilot.com`, `copilot-proxy/telemetry.githubusercontent.com`, `default.exp-tas.com`.

### 4.6 Entrega controlada (Etapa 6)
`ExamActive` (sin navegacion) -> alumno solicita -> validar integridad local ->
`SubmissionOpening` (conectividad minima temporal, expira sola) -> push desde la app
(LibGit2Sharp) -> verificar SHA remoto + remoto esperado -> cerrar conectividad ->
`Completed`. Sin ventana de navegador para el alumno; reintento idempotente; restaurar
politicas al completar. Si HardLock no da allowlist por proceso: offline total + push
final bajo supervision.

### 4.7 Procesos / Auditoria / Recovery (Etapas 7-9)
ProcessMonitor: incluir sin-ventana, firma/ruta/PID, clasificar (bloqueado/permitido/
desconocido/esencial), allow+block por eval, AuditOnly, debounce/dedup, no matar criticos,
no sancion auto. Eventos (abajo) = evidencia, no veredicto. Privacidad: sin keylog/captura/
portapapeles/contenido. Cola cifrada offline + idempotente + RLS + hora de servidor.
Recovery: Apply/Verify/Restore/Recover por control + tool de limpieza TI (solo artefactos
propios). Restore = snapshot exacto, nunca "borrar lo actual".

### 4.8 Taxonomia de eventos
`exam_started, exam_completed, close_attempt, blocked_process_started,
unknown_process_started, browser_started, ide_not_allowed_started, ai_policy_changed,
policy_tamper_event, network_policy_changed, vpn_detected, remote_changed,
repository_integrity_failed, submission_started, submission_succeeded, submission_failed,
recovery_performed, teacher_override`.

## 5. Validacion del CopilotBlockService actual (ya endurecido)
**OK:** chat.disableAIFeatures + claves; Code/Insiders/VSCodium + perfiles; merge preserva
claves; WriteWithRetry; watchers + timer 5s; ReconcileOnStartup no desbloquea huerfanos.
**A corregir (Etapa 4 PR):**
1. Escritura NO atomica (`File.WriteAllText` en sitio) -> usar temp + `File.Move` (atomico).
2. Re-serializa el dict -> PIERDE comentarios/orden/JSONC; y en Unblock no restaura el
   original EXACTO (solo quita claves y re-serializa). Falta snapshot del original SIEMPRE
   (hoy solo respalda si esta malformado) + restore literal.
3. Auto-lockdown ante edicion -> evaluar `policy_tamper_event` (revision) vs sancion auto
   (decision docente; principio de debido proceso del propio spec).
4. `catch { }` vacios en rutas de seguridad (211, 288, 406, 434) -> log estructurado.
