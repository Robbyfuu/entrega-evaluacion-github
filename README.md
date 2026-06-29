# Entrega de Evaluación — Control de Integridad

Plataforma para que alumnos de DUOC entreguen sus evaluaciones a GitHub bajo
control de integridad durante evaluaciones presenciales. No promete prevención
perfecta: combina prevención razonable, detección de eventos observables y
evidencia para revisión docente, distinguiendo cada capa y sus límites.

Estado actual del cliente: **v2.7.13**.

---

## Componentes

| Componente | Tecnología | Rol |
|---|---|---|
| **Cliente** (`csharp/`) | C# WPF, .NET 8 (`net8.0-windows`), WPF-UI, WebView2, LibGit2Sharp, Velopack | App de escritorio Windows que usa el alumno para entregar y que aplica los controles de integridad. |
| **Panel** (`admin-next/`) | Next.js 16 (App Router), React 19, TypeScript, Tailwind v4, Supabase JS | Consola docente de monitoreo y configuración, en tiempo real. |
| **Backend** | Supabase (Postgres + Auth + Realtime + Storage) | Fuente de verdad: políticas, eventos, roster, enunciados. Protegido por RLS. |
| **Entrega** | GitHub + GitHub Classroom | Los repos de los alumnos viven en GitHub; las tareas se distribuyen vía Classroom. |

### Flujo de datos

```
   Cliente WPF (alumno, asInvoker)                Panel Next.js (docente)
        |  heartbeat / eventos / entrega                |  config / liberar lockdown
        v                                                v
                         Supabase (Postgres + RLS + Realtime + Storage)
                                          |
                                   GitHub / GitHub Classroom
```

El cliente usa la **anon key** de Supabase (pública por diseño; RLS protege las
escrituras). La autenticación de GitHub se hace por **device flow** con el OAuth
Client ID público del GitHub CLI; las credenciales nunca se envían a un servidor
propio (se guardan en el Credential Manager de Windows).

---

## Cliente (app del alumno)

- **Login GitHub por device flow**: muestra un código `XXXX-XXXX`; el alumno lo
  ingresa en `github.com/login/device` desde la PC **o el celular**. No abre un
  navegador automáticamente. La sesión queda guardada en el equipo.
- **Selección de Curso / Sección / Evaluación**: las opciones vienen de Supabase
  (tablas `courses` / `sections` / `evaluations`), con fallback a una lista
  hardcodeada en `Config.cs` si el fetch falla. Al **aceptar/iniciar** la
  evaluación la selección queda **bloqueada** para evitar cambios a mitad de prueba.
- **Aceptar tareas de Classroom**: ventana de tareas que registra la aceptación
  (`assignment_acceptances`).
- **Clonar y subir**: clona el repo en el Escritorio y, al terminar, hace
  `commit` + `push` **siempre a `main`**. Incluye protección anti–repo-sucio
  (`TestRepoIsClean`) y protección contra confusión de cuentas (no deja subir si
  el `.git` pertenece a otra cuenta de GitHub).
- **Navegador embebido (WebView2) con allowlist**: solo permite navegar a los
  dominios y URLs exactas permitidos. La lista es **dinámica** (tabla
  `allowed_urls`, editable por sección desde el panel) con fallback a la lista
  hardcodeada de `Config.cs`; un fetch fallido nunca amplía lo permitido.
  Cualquier navegación fuera de la lista dispara la pantalla roja.
- **Bloqueo de internet (SoftLock)**: cierra navegadores externos y fija un proxy
  inválido en `HKCU` (`127.0.0.1:1`). Declarado explícitamente como control
  **blando**: CLI, WSL, VPN o clientes directos lo ignoran.
- **Bloqueo de Copilot en VS Code**: sabotea `settings.json`
  (`chat.disableAIFeatures` + claves Copilot) en Code / Insiders / VSCodium y
  perfiles, con watchers + timer; reaplica si el alumno lo revierte.
- **Sonda de red (detección de Copilot)**: inspecciona conexiones hacia endpoints
  conocidos de Copilot y reporta hallazgos.
- **Lockdown / pantalla roja**: modo kiosk que cubre la pantalla. Tres orígenes:
  **remoto** (el profe bloquea a toda la sección), **dirigido** (a un PC) y
  **trampa local** (repo sucio, navegación prohibida, reactivación de Copilot).
  El cliente reporta su propio bloqueo a `targeted_lockdowns` (RPC
  `report_self_lock`) para que el docente lo **vea y lo libere desde el panel**.
  La trampa local también admite liberación con clave del profe en la máquina.
- **Bloqueo de cierre**: cancela el cierre de la ventana y avisa; el escape es la
  clave del profe. El cierre por Task Manager lo cubre el daemon.
- **Daemon**: una Scheduled Task relanza la app (ventana de ~3 min).
- **PDF de enunciado**: si la evaluación tiene enunciado, lo descarga del bucket
  privado de Storage, lo abre y lo borra al terminar la evaluación o cerrar sesión.
- **Tema oscuro** y **versión visible** en la propia ventana.
- **Actualización**: es **manual y disparada por el docente** (el profe setea
  `control.update_requested_at` en el panel; el cliente actualiza una vez vía
  Velopack autenticado con el token del alumno). El cliente no hace fetch
  automático a GitHub.

---

## Panel (consola docente)

Login = cuenta docente en Supabase Auth (email + contraseña). Secciones:

**Monitoreo**

- **Secciones** — workspace con drill-down: secciones → alumnos → detalle.
- **Bloqueados** — alumnos en pantalla roja (lockdown activo), con liberación.
- **Internet bloqueado (offline)** — alumnos con internet bloqueado que no
  reportan (offline).
- **Resumen** — KPIs globales.
- **Controles** — controles remotos (lockdown, internet, etc.).

**Gestión / Global**

- **Cursos** y **Config. secciones** — administración de la jerarquía.
- **Evaluaciones y tareas** — vista unificada de evaluaciones y tareas de
  Classroom: link de Classroom **editable**, **modo de evaluación**, **PDF de
  enunciado** (subir/reemplazar/borrar) y acción **"ver alumnos"** por evaluación.
- **Roster** — import del roster (PII; acceso solo authenticated) y asignación de
  usuario de GitHub.
- **PCs conectados** — clientes online con su **versión por PC** y acción de
  **solicitar actualización**.
- **Alertas** — alertas de procesos sospechosos.
- **Navegación** — historial del navegador embebido (permitido / bloqueado).
- **Procesos** — blocklist de procesos editable por sección.
- **URLs permitidas** — allowlist del navegador embebido editable por sección.
- **Actividad** — actividad de los alumnos.
- **Trampas** — eventos de trampa (`cheat_events`).

---

## Modo de Evaluación Segura

Cada evaluación tiene un campo `exam_mode` (columna `evaluations.exam_mode`, más
`policy_json` opcional) con cuatro niveles:

- **Off** — sin controles (prácticas libres).
- **AuditOnly** — observa y reporta, no bloquea (rodaje / calibración de falsos
  positivos).
- **SoftLock** — controles sin privilegios (lo que el cliente puede hacer solo,
  como `asInvoker`).
- **HardLock** — reservado a infraestructura administrada por TI.

Lo que el modo **NO** hace (por diseño y por alcance):

- **No corre con privilegios de administrador** ni instala un servicio de Windows
  privilegiado. **HardLock está fuera del alcance de este repo** (requiere TI:
  firewall transaccional, Intune/GPO/AppLocker/WDAC, VLAN de evaluación). El techo
  real del cliente es **SoftLock asInvoker**.
- No hay firewall real ni bloqueo de egress por proceso; el bloqueo de red es
  blando (proxy HKCU + allowlist del WebView + cierre de navegadores).
- No hay keylogging, capturas de pantalla, lectura de portapapeles ni del
  contenido de los archivos del alumno.
- No emite veredictos automáticos de "fraude": los eventos son **evidencia para
  revisión docente**, no una sanción automática.

Detalle de diseño y modelo de amenaza:
`openspec/changes/secure-exam-mode/proposal.md` y `design.md`.

---

## Distribución y actualización

- El build del cliente lo dispara un **tag `exe-v*`**, que ejecuta GitHub Actions
  (`.github/workflows/build-csharp.yml`): `dotnet publish` → empaquetado
  **Velopack** (`vpk pack`, con delta updates) → publicación como GitHub Release.
- El alumno descarga el **instalador directo** desde Releases (app
  autocontenida; la app ofrece instalar `git` y `gh` con `winget` si faltan).
- **Auto-update autenticado**: las actualizaciones usan Velopack con el token de
  GitHub del alumno (evita el rate-limit por IP). El update es manual/disparado
  (ver sección Cliente).

---

## Setup mínimo

### Supabase

1. Crear un proyecto en Supabase.
2. Correr las migraciones en el **SQL Editor**, en orden de dependencia. Todas son
   idempotentes. El bundle `csharp/supabase-all-in-one.sql` es **GENERADO** desde el
   manifiesto `csharp/migrations.order` (`sh csharp/build-all-in-one.sh`) y **no debe
   editarse a mano**; corrido completo es seguro porque deja el endurecimiento de
   identidad **FASE 2 (JWT)** al final. La lista canónica y ordenada —incluidas las 4
   migraciones de FASE 2— está en `admin-next/README.md` y en `csharp/migrations.order`.
   ⚠️ Correr una migración PRE-FASE-2 standalone (rls-identity-hardening /
   version-visibility / self-lock / setup-supabase) **después** de FASE 2 revierte el
   endurecimiento: re-correr las 4 migraciones de FASE 2 al terminar.
3. Crear al menos una cuenta docente en Supabase Auth.

### Panel (Vercel)

```bash
cd admin-next
pnpm install
pnpm dev          # http://localhost:3000
pnpm build        # export estático -> out/
```

Deploy en Vercel: Root Directory = `admin-next`, framework Next.js, y las env vars
`NEXT_PUBLIC_SUPABASE_URL` + `NEXT_PUBLIC_SUPABASE_ANON_KEY`. Ver
`admin-next/README.md`.

### Cliente

`Config.cs` trae la URL y la anon key de Supabase, el Client ID del device flow y
las listas de fallback. Build/empaquetado: ver sección Distribución.

---

## Limitaciones y riesgos residuales (honestos)

Esto **no** garantiza la prevención del fraude. Fuera del alcance del software
(mitigación docente + infraestructura, no código WPF):

- **Teléfono, segundo computador o hotspot personal**: el software no los ve.
- **Colaboración presencial** y **código memorizado o traído por medios externos**.
- **Usuario con admin / acceso físico** a la máquina.
- **Red blanda**: el SoftLock (proxy HKCU) lo ignoran CLI, WSL, VPN y clientes
  directos. El egress real requiere TI (HardLock, fuera de alcance).
- **Copilot en VS Code**: en modo sin admin el bloqueo se **vigila y reporta**,
  no es inviolable (perfiles, portable, Insiders, WSL/Remote).
- **Daemon**: ventana de ~3 min entre un kill y el relanzamiento.

Para reducir la copia entre alumnos, las evaluaciones calificadas deberían usar
**repos privados** (GitHub Classroom / org); el cliente lo verifica y avisa, pero
no puede forzar la privacidad (la fija Classroom).

---

## Documentación adicional

- `docs/Guia-Alumno.pdf` — guía paso a paso para el alumno (uso de la app).
- `docs/Instructivo-Instalacion.pdf` — instructivo de instalación.
- `admin-next/README.md` — stack del panel, env vars, deploy y migraciones.
- `openspec/changes/secure-exam-mode/` — propuesta y diseño del modo seguro.

> El cliente legacy basado en PowerShell (`Subir-Evaluacion.ps1` y compañía) fue
> removido: la app C# autocontenida lo reemplaza por completo.
</content>
</invoke>
