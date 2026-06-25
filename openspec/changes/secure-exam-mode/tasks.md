# Tareas: Modo de Evaluacion Segura (plan por PR pequenos)

Leyenda: [APP] solo cliente/panel/Supabase (sin admin) · [TI] requiere infra/admin DUOC ·
[DEC] decision docente/TI antes de implementar.

## PR0 — Fundaciones (este cambio) [APP]
- [x] Auditoria + threat model + arquitectura + plan (este OpenSpec).
- [x] Validacion del CopilotBlockService endurecido (findings en design.md §5).
- [x] Migracion: `evaluations.exam_mode` enum (Off|AuditOnly|SoftLock|HardLock) DEFAULT 'Off'
      + `policy_json`. (csharp/migration-exam-mode.sql — CORRER en Supabase antes de deploy panel)
- [x] Panel: selector de modo por evaluacion (en "Evaluaciones y tareas").

## PR1 — Maquina de estados [APP]
- [x] `IExamSessionService` + `ExamState` + transiciones validas (tabla). (fundacion)
- [x] `ExamSessionId`, inicio por hora de servidor, persistencia local idempotente.
- [x] `RecoverAsync` idempotente. (Models/ExamSession.cs + Services/ExamSessionService.cs)
- [ ] PENDIENTE INTEGRACION: indicador visible en ExamActive + migrar bools dispersos de
      MainWindow a la maquina (sin romper lockdown/cierre). Requiere tocar MainWindow.
- [ ] Tests: transiciones validas/invalidas, recovery idempotente.

## PR2 — Integridad de repo (PRIORIDAD) [APP+DEC]
- [DEC] GitHub: repos PRIVADOS (Classroom/org) + aprobar OAuth App (la org bloquea el token).
- [ ] Deshabilitar "usar repo existente" para calificadas (flag por eval).
- [ ] Repo nuevo por examen + baseline SHA + ligar a `ExamSessionId` + hora de servidor.
- [ ] Validar en submit: remoto esperado, sin remotos extra/submodulos/workflows/binarios,
      sin historial previo no autorizado; guardar SHA inicial/final.
- [ ] Evento `repository_integrity_failed`. Tests de validacion de remoto/baseline.

## PR3 — WebView + Copilot FQDN [APP]
- [ ] Cerrar/limitar WebView en ExamActive (sin `github.com` completo; solo flujo entrega).
- [ ] Allowlist/denylist FQDN Copilot versionada y central (panel) + fecha de actualizacion.
- [ ] Tests: clasificacion de dominios; no romper clone/push/auth.

## PR4 — Editor/IA: fixes + AuditOnly [APP+DEC]
- [x] CopilotBlockService: escritura ATOMICA (temp+rename).
- [x] Snapshot del original SIEMPRE + Restore EXACTO (.copilot-orig/.copilot-created).
- [x] Quitar `catch {}` silenciosos en rutas de seguridad (log estructurado).
- [ ] Nivel efectivo (`NotApplicable..Unknown`); `Unknown` != protegido. (pendiente)
- [DEC] `policy_tamper_event` (revision) vs auto-lockdown ante edicion de settings.
- [ ] Modo IDLE: `ide_not_allowed_started` + bloquear/reportar VS Code si IDLE autorizado.
- [ ] Tests: merge/restore JSONC, escritura atomica, deteccion propio-vs-externo.

## PR5 — ProcessMonitor + taxonomia de eventos [APP]
- [ ] Enumerar procesos sin ventana; capturar nombre normalizado, ruta, PID, firma, ts.
- [ ] Clasificar bloqueado/permitido/desconocido/esencial; allow+block por eval; AuditOnly.
- [ ] Debounce/dedup; no matar criticos; no sancion auto.
- [ ] Taxonomia de eventos unificada (design.md §4.8). Tests de normalizacion/dedup.

## PR6 — Entrega controlada [APP]
- [ ] Flujo Preflight->Baseline->ExamActive->SubmissionOpening->push->verify->Completed.
- [ ] Conectividad temporal que expira sola; push desde la app; verificar SHA remoto.
- [ ] Reintento idempotente; restaurar politicas al completar. Tests del flujo + fallo de red.

## PR7 — Auditoria/privacidad/panel [APP+Supabase]
- [ ] Cola de eventos cifrada offline + reenvio idempotente + retencion.
- [ ] Migracion `exam_events` (schema design.md §4.8) + RLS + hora de servidor.
- [ ] Panel: estados "evento -> revision -> resolucion" (no "fraude confirmado");
      marcar falsos positivos; exportacion para revision/apelacion.

## PR8 — Servicio privilegiado HardLock [TI]
- [ ] Servicio Windows separado (cuenta minima), IPC named pipe + ACL + firma/sesion.
- [ ] Comandos limitados (StartExamPolicy/Open/Close/Stop/GetStatus/Recover);
      firewall transaccional con id propio; snapshot+rollback; watchdog; expiry; Event Log.
- [ ] Tool de recuperacion: elimina SOLO reglas/artefactos de esta app.

## PR9 — Despliegue institucional [TI]
- [ ] Docs/scripts: Intune/GPO/AppLocker/WDAC/VLAN evaluacion/DNS/bloqueo VPN-DoH/cuentas.
- [ ] Politica administrada VS Code (AllowedExtensions, AI policies) + Policy Diagnostics.

## PR10 — Pruebas + docs + riesgos residuales [APP+TI]
- [ ] Integracion Win10/11 (matriz: VS Code stable/Insiders/portable/perfil, navegadores,
      apps IA, terminal/WSL/VPN, kill, reinicio, perdida de red en push, repo con remoto/
      historial/workflow/publico).
- [ ] Matriz adversarial (intento -> esperado -> evento -> accion -> revision).
- [ ] Guia de recuperacion + guia docente + README + lista de riesgos residuales.

## Orden / gating
PR0 -> PR1 -> PR2 (prioridad) -> PR3 -> PR4 -> PR5 -> PR6 -> PR7. PR8/PR9 en paralelo por TI.
PR10 cierra. Cada PR: build verde, tests, sin secretos, RLS intacta, restore exacto.
