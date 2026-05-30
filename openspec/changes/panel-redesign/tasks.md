# Tareas: Rediseño del Panel Docente

**Change:** panel-redesign
**Orden:** secuencial. Cada bloque deja el panel funcional.

---

## Fase 1 — Fundaciones (tokens + shell)

- [ ] T1.1 Agregar `<link>` Google Fonts (Inter + JetBrains Mono) con `display=swap`
- [ ] T1.2 Definir design tokens CSS (`:root` claro + `[data-theme="dark"]`)
- [ ] T1.3 Implementar toggle de tema + persistencia localStorage + carga inicial
- [ ] T1.4 Crear layout shell: topbar (56px) + sidebar (240px) + content
- [ ] T1.5 Sidebar colapsable (rail 64px) + responsive drawer <1024px
- [ ] T1.6 Verificar que login view sigue funcionando con nuevo shell

## Fase 2 — Barra de métricas (KPIs)

- [ ] T2.1 Componente KPI card (label + número mono + borde semántico)
- [ ] T2.2 KPIs: PCs conectados, Alertas, Internet, Lockdowns activos
- [ ] T2.3 Conectar KPIs a datos existentes (reusar loadOnline/loadState/alerts)
- [ ] T2.4 Color semántico dinámico según estado

## Fase 3 — Controles críticos diferenciados

- [ ] T3.1 Zona de controles con sub-zona destructiva (danger-bg + borde)
- [ ] T3.2 Botones tipados (.btn-primary/danger/success/secondary)
- [ ] T3.3 Confirmación en lockdown global (ya existe confirmLockdown, restyle)
- [ ] T3.4 Sección mensaje al aula con input + enviar/borrar

## Fase 4 — Tablas densas

- [ ] T4.1 Estilo tabla densa (fila 36px, header sticky, mono tabular)
- [ ] T4.2 Badges de estado consistentes (.badge-*) en todas las tablas
- [ ] T4.3 Migrar tabla PCs conectados (mantener IDs onlineBody, columnas, acción)
- [ ] T4.4 Migrar tabla alertas de procesos (alertsBody)
- [ ] T4.5 Migrar tabla actividad alumnos (activityBody + filtros)
- [ ] T4.6 Migrar tabla/form Classroom assignments (assignmentsBody + campos)
- [ ] T4.7 Migrar tabla eventos de trampa (eventsBody)

## Fase 5 — Modal + estado en vivo

- [ ] T5.1 Restyle modal de procesos (scrim, card, X/backdrop/Esc ya OK)
- [ ] T5.2 Pill "EN VIVO" + sección "Estado actual" con nuevo estilo
- [ ] T5.3 Verificar polling 20s refresca KPIs + tablas

## Fase 6 — Pulido y QA

- [ ] T6.1 Verificar contraste WCAG AA en claro y oscuro (todos los pares)
- [ ] T6.2 Focus rings + aria-labels en iconos/acciones
- [ ] T6.3 prefers-reduced-motion: desactivar transiciones
- [ ] T6.4 Probar con 30+ filas simuladas (densidad legible)
- [ ] T6.5 Regresión funcional: cada función opera igual (checklist REQ-6)
- [ ] T6.6 Commit + push (GitHub Pages auto-deploy) + hard refresh test

---

## Review Workload Forecast

- Archivos tocados: 1 (`admin/index.html`) — monolítico HTML/CSS/JS
- Líneas estimadas: ~600-800 cambiadas (reestructura completa)
- Chained PRs recomendado: No (single file, single concern)
- 400-line budget risk: Alto (pero justificado: rediseño atómico de 1 archivo)
- Decisión antes de apply: el archivo es indivisible; se hace en un commit
  grande pero revisable por fases visualmente.
