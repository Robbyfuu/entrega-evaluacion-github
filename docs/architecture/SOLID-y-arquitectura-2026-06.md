# Evaluación SOLID y Arquitectura — EntregaEvaluacion

> Evaluación de calidad arquitectónica (complementa la auditoría de seguridad `docs/audit/AUDIT-2026-06-26.md`). Pregunta distinta: ya no *"¿se puede romper/filtrar?"* sino *"¿se puede **evolucionar, testear, mantener**?"*. Basado en la disección real del código (clusters A–O de `MainWindow`, inventario de archivos grandes, auditoría DRY).

---

## 1. El diagnóstico de fondo (la raíz, antes de los principios)

Tres síntomas que en realidad son **una sola causa**:

1. **La lógica de negocio vive pegada al code-behind de WPF.** `MainWindow.xaml.cs` (2207 líneas) no es "la ventana": es la aplicación entera. El algoritmo que decide qué repo del alumno corresponde a qué evaluación (`PickAssignmentByLongestPrefix`), el cálculo de estado de entrega (5 señales → 3 buckets), el flujo de submit, la orquestación de lockdown… todo enterrado entre handlers de eventos y manipulación de controles XAML.

2. **Los servicios son `static class`.** Lockdown, InternetBlock, Copilot, Daemon, Theme, ExamPdf, NetworkProbe… 11 servicios estáticos. Un consumidor no los *recibe*, los *invoca directo*. No hay forma de sustituirlos por un doble de prueba.

3. **Cero tests.** Y no es por falta de ganas — es **consecuencia** de 1 y 2: **no se puede testear lo que no se puede instanciar**. Para testear el matcher de repos hoy habría que instanciar una `Window` de WPF entera con sus controles. Imposible en un unit test.

> **La raíz no es el tamaño de los archivos.** Es la **dirección de las dependencias**: todo apunta hacia afuera (a WPF, a estáticos globales) en vez de hacia adentro (a lógica pura). Arreglar eso es arreglar SOLID.

---

## 2. Evaluación SOLID — principio por principio

### S — Single Responsibility (VIOLADO, grave)

`MainWindow.xaml.cs` concentra **15 responsabilidades distintas** (clusters A–O del mapa): sesión/identidad, crear/clonar/subir repo, banner de assignments, cascada de selectores, visor de PDF, acción primaria, admin-poll, heartbeat, blocklist, lockdown, exit-guard, logging/toasts, tema, update remoto, sonda de red. Cada una es una **razón de cambio** independiente metida en la misma clase.

- **Evidencia**: `MainWindow.xaml.cs:1` (2207 líneas), clusters A–O.
- Segundo ofensor: `SupabaseClient.cs` (862 líneas) — un único objeto con TODAS las llamadas REST/auth/tablas. Es un "god-service".
- **Costo real**: un cambio en el flujo de submit te obliga a navegar 2207 líneas y arriesga romper el lockdown que está al lado.

### O — Open/Closed (VIOLADO, moderado)

Extender el sistema obliga a **modificar** código existente, no a agregar:

- Las **fuentes de lockdown** están hardcodeadas en 3 lambdas `checkStillLocked` casi idénticos (`:1845`, `:1908`, `:1949`). Agregar un cuarto tipo de bloqueo = editar MainWindow y copiar el patrón otra vez.
- `ComputeAssignmentStatusesAsync` (`:1196-1325`) cablea **5 señales → 3 buckets** con lógica condicional inline. Un nuevo estado de entrega = abrir y modificar ese método.
- No hay un punto de extensión (estrategia/polimorfismo) para "tipos de evaluación" o "tipos de trampa".

### L — Liskov Substitution (N/A en la práctica)

Casi no hay jerarquías de herencia que evaluar. Existen interfaces (`IExamControl`, `IExamSessionService`) pero **`ExamSessionService` está DORMANT** (escrita, no cableada a `MainWindow`). No hay violaciones de LSP porque **casi no hay polimorfismo** — que en sí es parte del problema (ver DIP).

### I — Interface Segregation (VIOLADO por ausencia, no por exceso)

El problema no son interfaces gordas — es que **casi no hay interfaces**. `SupabaseClient` es una clase concreta de 862 líneas: cualquiera que necesite *leer cursos* depende de TODO el cliente (auth, submit, heartbeat, storage…). No existe un `ICourseReader` chico. Los consumidores dependen de una superficie enorme de la que usan el 5%.

### D — Dependency Inversion (VIOLADO, el más grave)

**Este es el que sostiene a todos los demás.** Los módulos de alto nivel dependen de **concreciones**, no de abstracciones:

- **11 servicios `static class`** → invocados directo, imposibles de mockear (`LockdownService`, `InternetBlockService`, `CopilotBlockService`, `DaemonService`, `ThemeService`, `ExamPdfService`, `NetworkProbeService`, …).
- `MainWindow` hace `new GitService(...)` (`:964`, `:1018`) y `new SupabaseClient()` — instancia sus dependencias en vez de recibirlas.
- No hay contenedor de DI ni inyección por constructor.
- **Consecuencia directa**: testabilidad cero. La lógica de negocio no se puede aislar de la red, el filesystem, ni la UI.

| Principio | Veredicto | Ofensor principal |
|---|---|---|
| **S**RP | 🔴 Violado grave | `MainWindow` (15 responsabilidades), `SupabaseClient` (god-service) |
| **O**CP | 🟠 Violado moderado | lockdown sources hardcodeadas, buckets de estado inline |
| **L**SP | ⚪ N/A | casi sin herencia/polimorfismo |
| **I**SP | 🟠 Violado por ausencia | sin interfaces chicas; `SupabaseClient` monolítico |
| **D**IP | 🔴 Violado grave | 11 servicios estáticos, `new` de dependencias, sin DI → **0 tests** |

---

## 3. Arquitectura objetivo

La meta no es "MVVM perfecto de un día para el otro". Es **invertir la dirección de las dependencias** para que la lógica pura quede aislada y testeable.

```
   ┌─────────────────────────────────────────────┐
   │  UI (WPF)  — delgada                          │
   │  MainWindow/ViewModels: solo pinta + delega   │
   └───────────────┬─────────────────────────────┘
                   │ depende de ↓ (interfaces)
   ┌───────────────┴─────────────────────────────┐
   │  Servicios (I/O)  — detrás de interfaces      │
   │  ISupabase*, IGit, ILockdown, ILogSink, …     │
   └───────────────┬─────────────────────────────┘
                   │ depende de ↓
   ┌───────────────┴─────────────────────────────┐
   │  Dominio (puro, SIN I/O ni UI)  ← TESTEABLE   │
   │  ClassroomRepoMatcher, AssignmentStatus-      │
   │  Calculator, RepoNameSanitizer, LogClassifier │
   └─────────────────────────────────────────────┘
        Las flechas apuntan HACIA ADENTRO.
```

Tres movimientos que materializan esto:
1. **Extraer el dominio puro** (matchers, calculadoras, sanitizers) a clases sin estado → primer terreno testeable.
2. **Poner interfaces a los servicios** (DIP/ISP) → la UI y el dominio dependen de abstracciones; los estáticos se vuelven inyectables.
3. **Adelgazar la UI** → `MainWindow` solo pinta y delega; el estado mutable compartido sale a un `ExamSessionState` inyectable.

---

## 4. Roadmap incremental (behavior-preserving, sin tests existentes)

> Regla de oro: la única red de seguridad hoy es `dotnet build` / `tsc`. Cada paso **mueve código idéntico** (no reescribe lógica), compila, y es commiteable/reversible solo. **No tocar los clusters de integridad de examen (lockdown, exit-guard, admin-poll, copilot) durante exámenes en curso** — un bug ahí no rompe una build, rompe un examen en vivo.

| Fase | Qué | Riesgo | ¿En temporada de examen? |
|---|---|---|---|
| **1** | Panel TS: unificar `ONLINE_WINDOW_MS` ×3, usar `makeSuspChecker`, helper `isOnline` | Trivial/Bajo | ✅ Sí |
| **2** | Extraer estáticos puros del cliente (`RepoNameSanitizer`, **`ClassroomRepoMatcher`**, `LogClassifier`) + **AGREGAR LOS PRIMEROS TESTS** | Bajo | ✅ Sí |
| **3** | DRY intra-runtime C#: genérico `GetListAsync<T>` (6 métodos), consolidar `checkStillLocked` ×3 | Bajo | ⚠️ El de lockdown, fuera de examen |
| **4** | Golden tests de paridad cross-runtime (`NormalizeProcessName`, lista de procesos) — **NO unificar** | Bajo | ✅ Sí |
| **5** | SQL: generar `all-in-one` por build; una sola fuente del DDL de `suspicious_processes` | Medio | ❌ Fuera de examen |
| **6** | Prerrequisitos de desacople: `ILogSink`/`IUserNotifier` + `ExamSessionState` inyectable | Medio-Alto | ❌ Fuera de examen |
| **7** | Extraer servicios casi-puros (`PdfViewer`, `HeartbeatReporter`, `AssignmentStatusCalculator`, …) tras Fase 6 | Medio | ❌ Fuera de examen |
| **8** | Alto riesgo (MVVM real): cascada de selectores, `LockdownCoordinator`, `ExitGuard`, `SubmissionFlow` | Alto | ❌ DIFERIR |

### Primera tajada recomendada (segura HOY, incluso en examen)
**Fase 1 + Fase 2.** Por qué es la mejor relación riesgo/valor:
- Fase 1 es trivial (importar constantes/helpers ya existentes, borrar copias).
- Fase 2 **saca a la luz la lógica de negocio crítica** hoy enterrada (el matcher longest-prefix-wins que decide qué repo es de qué evaluación) **y, por primera vez, la testea**. Pasás de **0 tests a un harness de tests** — la red de seguridad que habilita todo lo demás.
- Ninguna toca lockdown/heartbeat/exit-guard → seguro en vivo.

---

## 5. Lo que NO se toca (mirroring deliberado, no es deuda)

- **`NormalizeProcessName` C#/TS** (`Config.cs:69` / `suspicious.ts:51`): dos runtimes que no comparten código por diseño. Unificar = WASM/codegen = sobre-ingeniería. Se **protege con golden tests**, no se elimina.
- **Listas de fallback de procesos** (`Config.cs:45`, `suspicious.ts:6`): fallback de **resiliencia** cuando la tabla `suspicious_processes` no responde. Es a propósito.
- **`GetBlocklistAsync`/`GetAllowlistAsync`**: devuelven `null` en error **a propósito** (semántica de fallback distinta de `[]`). NO meter en el genérico `GetListAsync<T>`.

---

## 6. Nota sobre tests (la pieza que falta)

Hoy hay **0 tests**. La Fase 2 introduce el primer proyecto de tests (xUnit) sobre la lógica pura extraída — empezando por el matcher de repos, que es **lógica de negocio crítica sin red**. Antes de tocar cualquier cosa de **alto riesgo** (Fase 6+, estado de lockdown/heartbeat), hay que escribir **characterization tests** (capturar el comportamiento actual como casos congelados) para detectar si un refactor lo cambia. Sin esa red, refactorizar lockdown a ciegas en producción es inaceptable.
