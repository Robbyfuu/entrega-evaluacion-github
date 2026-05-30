# Diseño Técnico: Rediseño del Panel Docente

**Change:** panel-redesign
**Design system:** "Consola Ops" (UI/UX Pro Max skill)

---

## 1. Decisiones de arquitectura

| Decisión | Elección | Razón |
|----------|----------|-------|
| Pattern | Dashboard (sidebar + topbar + content) | Skill recomienda para admin/monitoring |
| Estilo | Consola Ops (neutro denso, Linear/Vercel) | Datos densos, baja fatiga sesiones largas |
| Contexto | Monitor escritorio | Aguanta tablas densas, info simultánea |
| Tema | Claro default + toggle oscuro | Aula iluminada (claro) + monitoreo (oscuro) |
| Stack | HTML+CSS+JS vanilla (sin cambios) | GitHub Pages, sin build step |
| Migración | Reusar IDs + funciones JS existentes | Cero regresión funcional |

> Nota: la skill sugirió Glassmorphism pero ella misma lo marca
> "Avoid for: data-dense interfaces". Por eso elegimos Consola Ops (neutro).

---

## 2. Design Tokens (CSS Custom Properties)

### 2.1 Color — Tema Claro (default)

```css
:root {
  /* Superficies */
  --bg:            #F7F8FA;   /* fondo app */
  --surface:       #FFFFFF;   /* cards, tablas */
  --surface-2:     #F0F2F5;   /* hover, zebra */
  --border:        #E2E5EA;   /* bordes, dividers */
  --border-strong: #CBD0D8;

  /* Texto */
  --text:          #1A1D23;   /* principal (contraste 14:1) */
  --text-muted:    #6B7280;   /* secundario (4.6:1) */
  --text-faint:    #9CA3AF;   /* terciario */

  /* Semántico */
  --primary:       #2563EB;   /* azul acciones primarias */
  --primary-fg:    #FFFFFF;
  --danger:        #DC2626;   /* rojo bloqueo/lockdown */
  --danger-bg:     #FEF2F2;
  --success:       #16A34A;   /* verde libre/ok */
  --success-bg:    #F0FDF4;
  --warning:       #D97706;   /* ámbar alerta */
  --warning-bg:    #FFFBEB;
  --info:          #7C3AED;   /* violeta secciones */
}
```

### 2.2 Color — Tema Oscuro (toggle, base Linear Dark)

```css
[data-theme="dark"] {
  --bg:            #0D0E14;
  --surface:       #1A1B26;
  --surface-2:     #22232F;
  --border:        #2A2C3A;
  --border-strong: #3A3D4D;

  --text:          #E0E0E6;   /* (12:1 sobre surface) */
  --text-muted:    #9CA3AF;   /* (4.8:1) */
  --text-faint:    #6B7280;

  --primary:       #5E6AD2;
  --primary-fg:    #FFFFFF;
  --danger:        #F87171;   /* rojo desaturado p/ oscuro */
  --danger-bg:     #2A1416;
  --success:       #4ADE80;
  --success-bg:    #0F2417;
  --warning:       #FBBF24;
  --warning-bg:    #2A1F0A;
  --info:          #A78BFA;
}
```

### 2.3 Tipografía

```css
:root {
  --font-ui:   'Inter', -apple-system, 'Segoe UI', system-ui, sans-serif;
  --font-mono: 'JetBrains Mono', ui-monospace, 'SF Mono', Consolas, monospace;

  /* Escala (Major Third 1.25) */
  --fs-xs:  11px;   /* badges, captions */
  --fs-sm:  13px;   /* tabla, body denso */
  --fs-md:  14px;   /* body */
  --fs-lg:  16px;   /* subtítulos */
  --fs-xl:  20px;   /* títulos sección */
  --fs-2xl: 28px;   /* KPI numbers */

  --fw-regular: 400;
  --fw-medium:  500;
  --fw-semibold:600;
  --fw-bold:    700;
}
```

Inter vía Google Fonts (`display=swap`). JetBrains Mono para **números
tabulares** (`font-variant-numeric: tabular-nums`) en KPIs, contadores,
tiempos "hace Xs", PIDs.

### 2.4 Espaciado / radios / sombras

```css
:root {
  --sp-1: 4px;  --sp-2: 8px;  --sp-3: 12px; --sp-4: 16px;
  --sp-5: 24px; --sp-6: 32px; --sp-8: 48px;

  --radius-sm: 6px;  --radius-md: 8px;  --radius-lg: 12px;

  --shadow-sm: 0 1px 2px rgba(0,0,0,0.04);
  --shadow-md: 0 2px 8px rgba(0,0,0,0.06);
  --z-sidebar: 40; --z-modal: 1000; --z-toast: 1100;
}
```

---

## 3. Layout

```
┌─────────────────────────────────────────────────────────────┐
│ TOPBAR: logo · "Panel Docente"      [tema◐] usuario [salir]  │
├──────────┬──────────────────────────────────────────────────┤
│ SIDEBAR  │  CONTENT                                          │
│ 240px    │  ┌────────────────────────────────────────────┐  │
│          │  │ KPIs: [PCs 12] [Alertas 3] [Net BLOQ] [Lck] │  │
│ ▸ Estado │  └────────────────────────────────────────────┘  │
│ ▸ Control│  ┌── Controles remotos ──────────────────────┐   │
│ ▸ PCs    │  │ [Bloquear] [Desbloquear] ‖ zona-danger     │   │
│ ▸ Alertas│  └────────────────────────────────────────────┘  │
│ ▸ Activid│  ┌── PCs conectados (tabla densa) ───────────┐   │
│ ▸ Tareas │  │ PC  usuario  secc  señal  apps  net  lck  ⋮│   │
│          │  └────────────────────────────────────────────┘  │
│ [colaps] │  ... (resto secciones)                            │
└──────────┴──────────────────────────────────────────────────┘
```

- **Topbar** (56px): sticky. Identidad izq, toggle tema + usuario + logout der.
- **Sidebar** (240px, colapsa a 64px rail): nav con icono+label, item activo
  resaltado. Botón colapsar abajo.
- **Content**: max-width fluido, padding `--sp-5`, secciones con anclas.
- Responsive: <1024px sidebar se vuelve drawer (off-canvas).

---

## 4. Componentes

### 4.1 KPI Card
- Surface, border, radius-md, padding sp-4.
- Label `--fs-sm --text-muted` arriba; número `--fs-2xl --font-mono` abajo.
- Borde-izquierdo 3px color semántico según estado.

### 4.2 Badge de estado
- Pill `--radius-sm`, `--fs-xs --fw-semibold`, padding 2px 8px.
- Variantes: `.badge-danger` (rojo), `.badge-success` (verde),
  `.badge-warn` (ámbar), `.badge-info` (violeta), `.badge-neutral` (gris).
- Color = fondo semántico claro + texto semántico oscuro (no solo color:
  incluye texto legible → cumple `color-not-only`).

### 4.3 Botones
- `.btn-primary` (azul), `.btn-danger` (rojo), `.btn-success` (verde),
  `.btn-secondary` (gris outline). Altura 36px, radius-md, hover sutil.
- Zona destructiva: contenedor con `--danger-bg` y borde, separa físicamente
  bloquear/lockdown de acciones normales.

### 4.4 Tabla densa
- `--fs-sm`, fila 36px, padding celda `sp-2 sp-3`.
- Header sticky, `--text-muted --fw-semibold`, border-bottom 2px.
- Zebra opcional `--surface-2`. Hover de fila `--surface-2`.
- Números/tiempos en `--font-mono tabular-nums`.
- Columna acción al final, `e.stopPropagation()` para no abrir modal.

### 4.5 Modal procesos
- Overlay scrim 50%. Card surface, radius-lg, max-width 720px.
- Cierre: botón X, click backdrop, tecla Esc (ya implementado).

---

## 5. Mapeo de IDs (cero regresión)

El JS actual referencia estos IDs — se MANTIENEN exactos:

```
loginView, panelView, email, password, loginErr, livePill, userBadge,
estInternet, estLockdown, estMessage, estUpdated, msgInput, ctlMsg,
onlineBody, onlineCount, alertsBody, alertCount, activityBody,
actionFilter, sectionFilter, assignmentsBody, asgTitle, asgSection,
asgOrg, asgUrl, asgMsg, eventsBody, processModal, modalTitle,
modalProcessBody
```

Las funciones JS (`loadState`, `loadOnline`, `loadProcessAlerts`,
`loadActivity`, `loadAssignments`, `setControl`, `targetLockdown`, etc.)
NO se tocan. Solo cambia el HTML contenedor + CSS.

---

## 6. Tema toggle (implementación)

```js
const saved = localStorage.getItem('panel-theme') || 'light';
document.documentElement.setAttribute('data-theme', saved);
function toggleTheme() {
  const next = document.documentElement.getAttribute('data-theme') === 'dark'
    ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  localStorage.setItem('panel-theme', next);
}
```

---

## 7. Accesibilidad

- Contraste verificado AA en ambos temas (tokens elegidos lo cumplen).
- Color nunca único indicador: badges incluyen texto ("BLOQUEADO", "libre").
- Focus rings visibles 2px en inputs/botones.
- `aria-label` en botones de icono (toggle tema, colapsar, acciones de fila).
- `aria-sort` en headers de tabla ordenables (si se agrega sort).
- Tablas con `<caption>` sr-only describiendo contenido.
- `prefers-reduced-motion`: desactivar transiciones de tema/hover.
- Modal: focus trap + Esc + retorno de foco al trigger.
