# ENT-6 — Desacople de MainWindow (costuras DIP)

> Contrato de diseño de la **Fase 6** del proyecto _Arquitectura & Refactor SOLID_ (Linear `ENT-6`). Complementa el roadmap `docs/architecture/SOLID-y-arquitectura-2026-06.md` y la auditoría `docs/audit/AUDIT-2026-06-26.md` (hallazgos `principles-05/07/08`, anexo SOLID `DIP-1/DIP-3`, cableado `LSP-1`).
>
> **Restricción dominante**: se ejecuta en plena temporada de examen. El criterio que manda sobre cualquier otro es **cero cambio de comportamiento por paso**. Un bug en el lockdown rompe exámenes en vivo. Diseño sólido > velocidad.

---

## 1. Qué es ENT-6 (y qué NO)

ENT-6 crea las **costuras** (DIP) que permiten, recién en ENT-7, extraer servicios fuera del god-class `MainWindow.xaml.cs` (2082 líneas). ENT-6 **no extrae lógica de negocio**: introduce abstracciones inyectables y reordena la construcción de dependencias, sin mover algoritmos.

- **ENT-6 (esta fase)** — costuras: `ILogSink`/`IUserNotifier`, `ISelectionStore`, interfaces espejo `IGitHubService`/`ISupabaseClient`, composition root.
- **ENT-7** — extraer servicios casi-puros (AssignmentStatusCalculator, HeartbeatReporter, BlocklistRefresher, PdfViewer, NetworkProbeReporter, PrimaryActionResolver) + identidad (`ExamContext`) + `DIP-1` (estáticos → instancia) + `SRP-2` (partir `SupabaseClient` en repos) + colapso de instancias.
- **ENT-8** — **DIFERIDO en temporada de examen**: MVVM de alto riesgo (LockdownCoordinator/`IExamControl` wiring, cascada de selectores, ExitGuard, SubmissionFlow).

> **Hallazgo que reencuadra todo**: existe una fundación **dormida** y bien diseñada — `Models/ExamSession.cs` (doc serializable + enum `ExamState` + `IExamControl`) y `Services/ExamSessionService.cs` (máquina de estados con `AllowedTransitions`, reloj de servidor autoritativo, `RecoverAsync` idempotente, persistencia atómica, `SemaphoreSlim`). Tiene **cero callsites**. Cablearla es ENT-8. ENT-6/7 **no la tocan** y **no re-derivan** una máquina de estados.

---

## 2. Decisiones de diseño

### D1 — Dos conceptos de estado, no uno

El ticket dice "`ExamSessionState` inyectable ← los 4 flags + `StudentSection`". Esa redacción es una trampa: un único objeto mutable compartido con selección **y** flags de lockdown no encapsula nada — relocaliza el global (_anemic shared-mutable holder_).

Se parte en dos naturalezas distintas:

| Concepto | Qué es | Dónde vive en ENT-6 |
|---|---|---|
| **Selección** | curso/sección/`evaluationId` (+ `CurrentEvaluation`) | `ISelectionStore` **inyectado** (transversal; hoy en `StudentSection` HKCU) |
| **Estado de lockdown** | `_internetBlocked`, `_copilotBlocked`, `_remoteLockdownActive`, `_targetedLockdownActive` | **privados** del futuro `ProctoringCoordinator` (ENT-7/8) — NO compartidos |

Los 4 flags **no son verdad**: son espejos de fuentes externas (registry, `settings.json`/`_armed`, backend) y **guardas de reentrancia** alrededor de `CheatWindow.ShowDialog` (sólo una pantalla roja a la vez). Sacarlos a un objeto compartido inyectado es exactamente cómo se mete un bug en el lockdown. Quedan junto a la lógica de `ShowDialog` y al `+= / -=` de `CopilotBlockService.OnCheatDetected`, que debe permanecer balanceado.

### D2 — `ISelectionStore`

- **Alcance** (refinado en implementación, forzado por el código): el store envuelve **exactamente** lo que persistía `StudentSection` — `SectionText` (string), `SectionId` (long?), `EvaluationId` (long?). `CourseId`/`CurrentEvaluation` quedan FUERA: son estado in-memory de UI de MainWindow, no globales transversales. Superficie: lecturas `SectionText/SectionId/EvaluationId`; setters granulares `SetSectionText/SetSectionId/SetEvaluationId`; `Clear()`; evento `SelectionChanged`. La **identidad** (`_user`/`_enrollment`) NO entra ahora → ENT-7 como `ExamContext`.
- **Mutabilidad**: dueño único **mutable** + evento en la superficie pública, PERO internamente el store persiste un `SelectionSnapshot` **inmutable** a través de `ISelectionPersistence` (el snapshot es inmutable; lo que muta es el dueño). La cascada de combos WPF y las guardas `_initializing`/`_syncingTipo` leen in-place, así que el cambio queda encapsulado dentro del store sin swap de referencia (MVVM real es ENT-8).
- **Persistencia**: se **mantiene el backing HKCU**. (1) Sobrevive al auto-relaunch del lockdown. (2) Migrar el formato de storage a mitad de examen arriesga perder selecciones en vuelo al auto-actualizar. Se arregla lo que importa: envolver HKCU detrás del store y **matar los `catch` vacíos** (`principles-08`, hoy ocultan fallos). **Implementación**: el store vive en `Core` (testeable, cross-platform) y escribe a través de un puerto `ISelectionPersistence`; el adapter `RegistrySelectionPersistence` (proyecto WPF) toca las MISMAS keys HKCU que `StudentSection` (`Software\EntregaEvaluacion`: `Section`/`SectionId`/`EvaluationId`) y loguea fallos vía `Debug.WriteLine` en vez de tragarlos. El puerto es la costura de testeo (los tests usan un fake en memoria).
- **Desacople**: independiente del `exam-session.json` dormido. Converger sobre `ExamSession` es ENT-8.
- **Wiring**: el composition root construye las instancias e inyecta el store en `MainWindow` y en `LoginWindow`. `SupabaseClient.IsForceLockdownAsync` recibe `evaluationId` por **parámetro** (ya no lee `StudentSection`), cerrando `principles-07`/`DIP-3`. `StudentSection` tenía DOS lectores externos — `SupabaseClient.IsForceLockdownAsync` y `LoginWindow.OpenEmbedded_Click` (descubierto durante la implementación) — ambos cerrados en el paso 4 y la clase **eliminada**.

### D3 — Interfaces `_gh`/`_sb`: espejo transicional (Opción B)

Extraer `IGitHubService` e `ISupabaseClient` como **espejos fieles 1:1** de los métodos actuales. Mecánico, sin cambio de comportamiento. Objetivo: romper el acoplamiento `new()` para que `MainWindow` y los servicios sean inyectables/testeables — las interfaces deben **existir**, no estar perfectamente talladas.

- La segregación ISP **angosta** (por consumidor) + `SRP-2` (partir `SupabaseClient` en `IControlRepository`/`IRosterRepository`/`IProctoringRepository`/`IAssignmentRepository` + `EffectiveControlResolver` puro) cae en **ENT-7**, guiada por consumidores reales, no especulativa. Partir el god-service antes de que existan sus consumidores es diseñar a ciegas, y toca la cache fail-safe `_lastKnownLock`.
- La `ISupabaseClient` gorda (~30 métodos) es una violación de ISP **declarada como transicional**. Es estrictamente mejor que `new()`; ENT-7 la talla.
- Se **mantiene el closure** `sb.SetGitHubTokenProvider(() => _gh.Token)` — `_sb` no conoce el tipo de `_gh`. No se convierte en dependencia directa `ISupabaseClient → IGitHubService`.
- Se **deja el reach-in estático** `gh.Http → InternetBlockService.IsBlocked()`. Convertir `InternetBlockService` a instancia es `DIP-1` (ENT-7).
- **Diferida** la inyección de `HttpClient`. La interfaz no arregla los ctors con efectos (DPAPI, `HttpClient`); refinamiento posterior.

### D4 — NO colapsar las instancias de `SupabaseClient` en ENT-6

Hoy hay **dos** instancias sin compartir: `MainWindow.xaml.cs:23` y el throwaway de `App.xaml.cs:72` (para el `CheatWindow` de arranque). Colapsarlas es el **único** cambio no mecánico de la fase: toca el compartir de la cache degrade-closed (`_lastKnownLock`/`_lastKnownTargeted`) en el path de lockdown de arranque.

**Diferirlo deja ENT-6 100% behavior-preserving.** `MainWindow` recibe su instancia inyectada para la sesión; el `CheatWindow` de arranque mantiene la suya; ambas detrás de `ISupabaseClient`. El colapso pasa a ser limpieza posterior **con test**, en ENT-7.

### D5 — Orden de extracción

Cada paso: build-green, reversible, **behavior-idéntico**.

| # | Paso | Riesgo | Tests |
|---|---|---|---|
| 1 | `ILogSink` / `IUserNotifier` (← `Log`/`Status`/`ShowToast`) | Bajo | — (mecánico) |
| 2 | Interfaces espejo `IGitHubService` / `ISupabaseClient` (type-widening; siguen en `new()`) | Bajo | — (mecánico) |
| 3 | `ISelectionStore` adapter sobre HKCU + migrar las ~44 lecturas | Medio | **TDD** del impl del store |
| 4 | Cerrar el reach-in del data-layer (`evaluationId` por parámetro a `_sb`) | Medio | — (mecánico) |
| 5 | Composition root (`App.OnStartup`): ctor-inyección en `MainWindow` | Alto | **gate: smoke manual del lockdown** |

**Smoke del paso 5** (manual): bloqueo de internet → fin de examen libera; pantalla roja remota; trap local; password de salida.

---

## 3. Reconciliación con strict-TDD

ENT-6 cambia **estructura**, no lógica. Los pasos mecánicos (interfaz = type-widening; store = adapter al mismo HKCU; composition root = mismos objetos, otro sitio de construcción) **no nacen lógica nueva** → no se TDD-ean. La disciplina TDD pega donde nace comportamiento:

- **Ahora**: el impl de `ISelectionStore` (set/clear/notify + manejo de error que reemplaza el `catch` vacío) → tests de tabla primero.
- **ENT-7**: lógica pura real (`AssignmentStatusCalculator`, `HeartbeatReporter`) → red-green.

**No hay characterization tests del god-class en ENT-6** — es chicken-and-egg: `MainWindow` no es testeable hasta descomponerlo, y lo testeable es ENT-7. La seguridad de ENT-6 viene de que cada paso es mecánico + build-green + (en el paso 5) smoke manual.

---

## 4. Invariantes de lockdown a preservar (no negociable)

Cualquier paso debe mantener intactos:

- **InternetBlockService** — internet se libera en fin de examen y cuando el toggle efectivo se apaga; `ReconcileOnStartup()` desbloquea proxy huérfano; degrada **cerrado** (si no se resuelve el control, no libera un bloqueo activo). El flag `_internetBlocked` debe quedar espejo-sincronizado con el registry.
- **LockdownService** — `Release()` deshace **todo** (marker, Run key, `DisableTaskMgr`, exe copiado). `VerifyPassword` (PBKDF2 200k) es la única salida local; sus constantes son load-bearing. `HasPersistentMarker()` gatea la pantalla roja de arranque.
- **CopilotBlockService** — `Unblock()` restaura `settings.json` exacto; `_armed` gatea callbacks; el `OnCheatDetected += / -=` queda balanceado (subscribe en Block; unsubscribe en Unblock **y** en `FinishExamCleanupAsync`). Self-healing (watcher + timer 5s) intacto.
- **NetworkProbeService** — sólo detección; nunca bloquea, nunca `throw`; el allowlist excluye `github.com` a propósito.

> Aclaración: `StillLockedByForce`/`StillLockedByTargetOrForce` (`MainWindow.xaml.cs:1713-1735`) son predicados **read-only** que sólo deciden si la pantalla roja sigue arriba; **no re-arman** el bloqueo. El re-armado vive en el watcher/timer de Copilot y en `AdminTickAsync`.

---

## 5. Estado de ejecución

Todos los pasos implementados en la rama `feat/ent6-log-notifier-seams` (desde `main` `6f89859`). Build + **69 tests verde** en cada paso; cada paso con review adversarial de contexto fresco.

| Paso | Estado | Commit |
|---|---|---|
| Sub-paso: `LogDetailWindow` → archivo propio | ✅ landed | PR #33 (`6f89859`) |
| 1 — `ILogSink` / `IUserNotifier` | ✅ hecho | `7c7a9ff` (+ doc `29988c7`) |
| 2 — interfaces espejo `IGitHubService`/`ISupabaseClient` | ✅ hecho | `981afd1` |
| 3 — `ISelectionStore` (TDD) | ✅ hecho | `98501e4` |
| 4 — cerrar reach-ins + eliminar `StudentSection` | ✅ hecho | `ab9bca5` |
| 5 — composition root | ✅ hecho | `d5dc0a4` |

**Hallazgo cerrado (MEDIUM transitorio)**: el paso 3 introdujo brevemente un *split de fuente de lectura del lockdown* — la apertura (`CheckAdminConfigAsync`) leía `_selection.EvaluationId` (in-memory) mientras la liberación (`SupabaseClient.IsForceLockdownAsync`) seguía leyendo `StudentSection.GetEvaluationId()` (HKCU). El paso 4 lo cerró: ambas rutas usan el valor del store. Review adversarial confirmó que `_selection.EvaluationId` no puede ser null cuando el viejo HKCU devolvía valor (la dirección peligrosa, la que liberaría a un alumno bloqueado).

---

## 6. Gate del paso 5 — smoke manual del lockdown (PENDIENTE, lo corre el humano)

El paso 5 cambió el arranque (composition root). **Antes de mergear la rama**, validar en una máquina Windows real:

1. **Arranque normal**: la app abre, login GitHub funciona, los combos curso/sección/evaluación pueblan y la selección persiste tras reiniciar la app.
2. **Bloqueo de internet**: con control efectivo activo, internet se bloquea; al finalizar el examen se **libera**.
3. **Pantalla roja remota**: el profe dispara lockdown remoto → aparece; libera por PC → desaparece.
4. **Trap local**: reactivar Copilot en `settings.json` → pantalla roja + reporte.
5. **Lockdown persistente**: con marker de sesión anterior, al arrancar la pantalla roja aparece ANTES del MainWindow (usa su propia instancia de `SupabaseClient`, no colapsada — D4).
6. **Salida**: password del profe (PBKDF2) libera; cierre con evaluación activa bloqueado por `OnClosing`.

Todo verde → mergear. La rama **no debe liberarse a alumnos sin este smoke**.

---

> **Pendiente ENT-7** (no en esta fase): colapsar las dos instancias de `SupabaseClient` (ahora ambas visibles en `App.StartShell`: la temprana del CheatWindow y `mainSb`); extraer servicios casi-puros (AssignmentStatusCalculator, HeartbeatReporter, …); `DIP-1` (servicios estáticos → instancia); `SRP-2` (partir `ISupabaseClient` en repos por agregado); identidad `ExamContext`.
