# Propuesta: Modo de Evaluacion Segura

## Problema

El cliente WPF (`.NET 8`, asInvoker, Velopack) reduce el uso de IA/navegacion durante
evaluaciones presenciales, pero los controles actuales son blandos o estan dispersos,
y el riesgo MAS alto (copia entre alumnos via repos publicos / repo preparado) no esta
cubierto. No existe una maquina de estados de evaluacion ni un baseline verificable.

No se busca prevencion perfecta ni un detector infalible de IA. Se busca:
prevencion razonable + deteccion de eventos observables + evidencia para revision
docente, distinguiendo claramente cada capa y sus limites.

## Objetivo

Un **Modo de Evaluacion Segura** activable por gradiente, configurado por curso/seccion/
evaluacion desde Supabase/panel:

- `Off` — sin controles (practicas libres).
- `AuditOnly` — observa y reporta, no bloquea (rodaje + calibracion de falsos positivos).
- `SoftLock` — controles sin privilegios (lo que el cliente puede hacer solo).
- `HardLock` — controles fuertes via servicio Windows separado instalado por TI + infra.

## Alcance

### Lo que SI puede el cliente (sin admin)
- Maquina de estados de evaluacion con ID, baseline y hora de servidor.
- Integridad de repositorio: repo nuevo por examen, SHA inicial/final, validar remoto.
- Bloqueo/registro de VS Code u otros IDE cuando IDLE es el editor autorizado.
- CopilotBlockService (settings.json) como capa para evaluaciones que requieren VS Code.
- WebView cerrado durante `ExamActive` (sin `github.com` completo; solo flujo de entrega).
- SoftLock de red (proxy HKCU) DECLARADO como blando + monitoreo de procesos.
- Entrega controlada (offline -> ventana temporal -> push -> verify -> offline).
- Cola de eventos cifrada offline + reporte idempotente.

### Lo que NECESITA TI/infraestructura (HardLock)
- Servicio Windows privilegiado (firewall transaccional, IPC named pipe, watchdog).
- Politica administrada de VS Code / extensiones (Intune/GPO/AllowedExtensions/WDAC).
- Egress real: firewall/proxy de laboratorio, VLAN de evaluacion, DNS corporativo,
  bloqueo de VPN/DoH, cuentas sin admin.
- GitHub Classroom / org con repos privados + aprobar la OAuth App (GitHub CLI).

### Fuera del alcance tecnico del cliente (documentar, no simular)
- Telefono, segundo computador, hotspot personal, colaboracion presencial, usuario con
  admin/acceso fisico, codigo memorizado/transportado por medios externos.
  -> Mitigacion docente + infraestructura, no codigo WPF.

## No-goals
- No detectores de IA por estilo/entropia. No veredictos automaticos de "fraude".
- No keylog, captura de pantalla, portapapeles ni contenido de archivos.
- No cambios globales irreversibles de Windows desde el proceso del alumno.
- No publicar repos de evaluaciones activas. No bloquear la entrega a GitHub.

## Riesgos residuales (explicitos)
- Telefono / 2do equipo / hotspot: fuera de alcance del software.
- HardLock sin TI: la red sigue siendo control blando (declarado).
- VS Code AI integrada: en modo sin admin el bloqueo se vigila + reporta, no es inviolable.
- Ventana del daemon (~3 min) entre kill y relanzamiento.

## Decisiones que requieren aprobacion docente/TI
1. GitHub: repos PRIVADOS (Classroom/org) + aprobar OAuth App (hoy la org bloquea el token).
2. Equipos administrados? -> habilita HardLock (servicio + admin de TI).
3. Politica ante edicion de settings de IA: lockdown automatico vs `policy_tamper_event`+revision.
4. Editor por evaluacion: solo IDLE (bloquear VS Code) vs VS Code permitido.
5. Red durante examen: offline total (mas seguro) vs allowlist FQDN (requiere proxy/HardLock).
