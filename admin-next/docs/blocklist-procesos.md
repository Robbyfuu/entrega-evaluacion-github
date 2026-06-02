# Blocklist de procesos sospechosos (editable por sección)

El profesor define, desde el panel, qué procesos se marcan como sospechosos durante
el examen — y puede hacerlo **por sección** (cada asignatura permite herramientas
distintas; ej.: en Python solo se permite el IDLE, en otra puede ser otro editor).

## Modelo

Tabla `suspicious_processes(id, process_name, section, created_at)`:

- `section IS NULL` → regla **global**, heredada por todas las secciones.
- `section = 'X'` → regla **extra** solo para la sección X.
- Lista efectiva de un alumno de la sección X = `(section IS NULL) ∪ (section = X)`.

`process_name` se guarda **normalizado**: minúsculas, sin sufijo `.exe`, sin espacios.
La misma normalización corre en el cliente C# (`Config.NormalizeProcessName`) y en el
panel (`lib/suspicious.ts → normalizeProcessName`) — deben coincidir siempre.

## Flujo end-to-end

1. El profe agrega/borra procesos en la sección **Procesos** del panel (escritura
   autenticada; anon solo lee).
2. El cliente C# baja su lista efectiva (global ∪ su sección) en cada ciclo de polling
   (`AdminTickAsync`, ~20 s) y la cachea.
3. Al detectar un proceso de la lista, el cliente reporta vía la RPC
   `report_process_alert` (rate-limit 30 s server-side). El profe ve la alerta en vivo.
4. El resaltado en "PCs conectados" lee la misma tabla (fuente única de verdad).

## Fallback (invariante crítico)

Si el cliente no puede bajar la tabla (error de red, o tabla vacía), cae a la lista
**hardcodeada** `Config.SuspiciousProcesses` (34 entradas base). La detección **nunca
se apaga**. El panel hace lo mismo con `FALLBACK_SUSPICIOUS_PROCESSES`.

## Alcance / threat model

Esto es **detección de trampa casual**, no anti-cheat resistente a un alumno con
control total de su PC:

- La blocklist es legible con la anon key (un alumno técnico puede ver qué evitar).
- El match es por nombre de proceso → evadible renombrando el ejecutable.

Sirve para disuadir y detectar lo obvio (browsers, mensajeros, IDEs no permitidos, IA),
no para frenar a un adversario sofisticado. Acción al detectar: **solo alerta** (sin
lockdown automático).
