# Especificación: Rediseño del Panel Docente

**Change:** panel-redesign
**Formato:** Requisitos + Escenarios (Given / When / Then)

---

## REQ-1: Layout dashboard con navegación lateral

El panel debe usar un layout de dashboard con sidebar de navegación persistente,
topbar con identidad + tema, y área de contenido principal.

### Escenario 1.1: Navegación entre secciones
- **Given** el profe está logueado en el panel
- **When** hace click en un item del sidebar (ej. "PCs conectados")
- **Then** el área de contenido hace scroll/ancla a esa sección
- **And** el item activo se resalta en el sidebar

### Escenario 1.2: Sidebar colapsable
- **Given** el panel en monitor de escritorio
- **When** el profe colapsa el sidebar
- **Then** queda solo iconos (rail 64px) y el contenido se expande

---

## REQ-2: Barra de métricas glanceable (KPIs)

La parte superior del contenido debe mostrar tarjetas de métricas clave que el
profe lee de un vistazo sin scrollear.

### Escenario 2.1: KPIs en tiempo real
- **Given** hay 12 PCs conectados, 3 con alertas, internet bloqueado
- **When** el panel carga o refresca (cada 20s)
- **Then** se ven KPI cards: "PCs conectados: 12", "Alertas: 3",
  "Internet: BLOQUEADO", "Lockdowns activos: N"
- **And** cada KPI usa color semántico (rojo si bloqueado, etc.)

### Escenario 2.2: KPI con cambio de estado
- **Given** el KPI "Internet: libre" en verde
- **When** el profe activa el bloqueo
- **Then** el KPI cambia a "Internet: BLOQUEADO" en rojo en el próximo refresh

---

## REQ-3: Zona de acciones críticas diferenciada

Las acciones destructivas (bloquear internet, lockdown global) deben estar
visualmente separadas de las acciones normales y requerir confirmación.

### Escenario 3.1: Confirmación de acción destructiva
- **Given** el profe ve el botón "LOCKDOWN global"
- **When** hace click
- **Then** aparece un diálogo de confirmación antes de aplicar
- **And** el botón usa color danger (rojo) distinto de los secundarios

### Escenario 3.2: Acción reversible visible
- **Given** internet está bloqueado
- **When** el profe mira la zona de controles
- **Then** el botón "Desbloquear" (verde) es claramente visible y opuesto al de bloquear

---

## REQ-4: Tabla de PCs conectados densa y legible

La tabla de PCs debe mostrar muchas filas con datos alineados, números
tabulares, badges de estado consistentes, y acción por fila.

### Escenario 4.1: Lectura de 30+ filas
- **Given** hay 30 PCs conectados
- **When** el profe escanea la tabla
- **Then** las columnas (PC, usuario, sección, última señal, apps, internet,
  lockdown) están alineadas con tipografía tabular
- **And** los tiempos ("hace 5s") usan fuente monoespaciada para comparar

### Escenario 4.2: Fila con alerta resaltada
- **Given** un PC tiene procesos sospechosos abiertos
- **When** se renderiza su fila
- **Then** la celda "apps" muestra badge rojo "⚠ N sosp."
- **And** la fila completa es identificable de un vistazo

### Escenario 4.3: Click en fila abre detalle
- **Given** la tabla de PCs
- **When** el profe hace click en una fila (no en el botón de acción)
- **Then** se abre el modal con los procesos de ese PC

---

## REQ-5: Tema claro/oscuro con toggle persistente

El panel debe soportar tema claro (default) y oscuro, con toggle que persiste
entre sesiones.

### Escenario 5.1: Cambio de tema
- **Given** el panel en tema claro
- **When** el profe hace click en el toggle de tema
- **Then** todo el panel cambia a oscuro sin recargar
- **And** la preferencia se guarda en localStorage

### Escenario 5.2: Persistencia
- **Given** el profe eligió tema oscuro
- **When** cierra y reabre el panel
- **Then** carga directo en oscuro

### Escenario 5.3: Contraste en ambos temas
- **Given** cualquier tema activo
- **When** se verifica el contraste de texto/fondo
- **Then** cumple WCAG AA (4.5:1 texto normal, 3:1 texto grande)

---

## REQ-6: Cero regresión funcional

Toda la funcionalidad del panel actual debe seguir operando idéntica.

### Escenario 6.1: Funciones intactas
- **Given** el panel rediseñado
- **When** el profe usa cualquier función (login, toggles internet/lockdown,
  mensaje, refrescar, tablas actividad/alertas/Classroom, lockdown dirigido,
  modal procesos, agregar/activar/eliminar assignment)
- **Then** cada una opera igual que en la versión previa
- **And** los IDs de elementos que el JS referencia se mantienen

### Escenario 6.2: Polling en vivo
- **Given** el panel abierto
- **When** pasan 20s
- **Then** state, online, activity, alerts se refrescan automáticamente
