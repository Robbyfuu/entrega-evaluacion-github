# Propuesta de Cambio: Rediseño del Panel Docente

**ID:** panel-redesign
**Estado:** Propuesto
**Autor:** Roberto Arce (FPY1101)
**Fecha:** 2026-05-29

---

## 1. Intención

Rediseñar el panel de administración web del docente (`admin/index.html`) para
que pase de un layout plano de tarjetas apiladas a un **dashboard profesional
de monitoreo en tiempo real**, optimizado para vigilancia de exámenes con datos
densos legibles y acciones críticas claramente diferenciadas.

## 2. Problema

El panel actual funciona pero tiene problemas de UX para uso real en examen:

- **Sin jerarquía visual**: todas las secciones son tarjetas blancas iguales
  apiladas verticalmente. El profe scrollea para encontrar lo crítico.
- **Acciones críticas mezcladas**: "Bloquear internet" y "LOCKDOWN" están entre
  botones secundarios. Riesgo de click accidental en una acción destructiva.
- **Sin escaneo rápido**: durante un examen el profe necesita ver de un vistazo
  cuántos PCs hay, cuáles tienen alertas, sin leer tablas largas.
- **Sin tema oscuro**: monitoreo prolongado en penumbra cansa con fondo blanco.
- **Datos no alineados**: números (contadores, tiempos) sin alineación tabular,
  difíciles de comparar entre filas.
- **Densidad subóptima**: mucho padding desperdicia espacio en monitor de
  escritorio donde cabría más info simultánea.

## 3. Alcance

### Incluido
- Rediseño completo de `admin/index.html` (HTML + CSS + JS vanilla).
- Layout dashboard: sidebar de navegación + topbar + área de contenido.
- Sistema de design tokens (colores claro/oscuro, tipografía, espaciado).
- Toggle de tema claro/oscuro persistente.
- Jerarquía de acciones: zona destructiva separada y confirmada.
- Tablas densas con números tabulares y badges de estado consistentes.
- Métricas glanceable en la parte superior (KPI cards).
- Mantener TODA la funcionalidad actual (cero regresión funcional).

### Excluido
- Cambios al backend Supabase (esquema, RPC, RLS) — sin tocar.
- Cambios a la app cliente C# WinForms — fuera de este change (futuro).
- Nuevas features funcionales — solo rediseño visual/UX.

## 4. Enfoque

Aplicar el design system **"Consola Ops"** generado con la skill UI/UX Pro Max:

- **Pattern:** dashboard (sidebar 240px + topbar + content).
- **Estilo:** neutro denso tipo Linear/Vercel. Color = significado.
- **Tipografía:** Inter (UI) + JetBrains Mono / SF Mono (datos numéricos).
- **Color:** grises neutros base + semántico (rojo=peligro, verde=ok,
  ámbar=warning, azul=primary). Claro por defecto, oscuro vía toggle.
- **Densidad:** spacing 4/8px, tablas compactas, padding reducido.

Implementación incremental: tokens CSS → layout shell → migrar cada sección →
tema oscuro → pulido a11y. Sin romper la lógica JS de Supabase existente
(reusar todas las funciones `loadState`, `loadOnline`, etc.).

## 5. Riesgos

| Riesgo | Mitigación |
|--------|-----------|
| Romper la lógica JS existente al reestructurar HTML | Mantener los mismos IDs de elementos que el JS usa; solo cambiar layout/estilo |
| Tema oscuro con contraste insuficiente | Verificar pares de color WCAG AA (4.5:1) en ambos temas |
| Sobre-diseñar y perder densidad | Priorizar legibilidad de datos sobre estética; tablas compactas |
| GitHub Pages cachea el HTML viejo | Hard refresh + cache-bust si necesario |

## 6. Criterios de Éxito

- El profe ve el estado global (internet/lockdown/PCs/alertas) sin scrollear.
- Acciones destructivas requieren confirmación y están visualmente aisladas.
- Toda la funcionalidad actual sigue operando (login, toggles, tablas, lockdown
  dirigido, modal de procesos, Classroom).
- Contraste WCAG AA en ambos temas.
- Tabla de PCs legible con 30+ filas sin fatiga.
