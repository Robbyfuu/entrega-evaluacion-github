# Entrega de Evaluación a GitHub

Herramienta para subir tu evaluación a GitHub de forma automática.

## Qué hace

Crea un repositorio en tu cuenta de GitHub con el formato:

```
<tu-nombre>-<forma-de-prueba>
```

Y sube todos los archivos de tu carpeta de evaluación con un solo click.

---

## Requisitos

- Windows 10 / 11
- Cuenta en [GitHub](https://github.com) (gratuita)
- Conexión a internet

**No necesitas instalar nada manualmente.** El script detecta y ofrece instalar
automáticamente `git` y `gh` (GitHub CLI) si no están presentes.

---

## Cómo usar

### 1. Descomprimir

Extrae el ZIP en una carpeta. Verás estos archivos:

```
EntregaEvaluacion/
├─ Subir-Evaluacion.bat         ← Doble-click aquí
├─ Subir-Evaluacion.ps1
├─ Reset-GitHubAuth.ps1
└─ README.md
```

### 2. Ejecutar

**Doble-click en `Subir-Evaluacion.bat`**.

Si Windows muestra una advertencia de seguridad:
- Click en **"Más información"**
- Click en **"Ejecutar de todas formas"**

El script desbloquea automáticamente los archivos al iniciar para evitar que esta
advertencia aparezca en futuras ejecuciones.

### 3. Primera vez: instalar dependencias

Si es la primera vez, el script te ofrecerá instalar `git` y `gh` con `winget`.

- Click **Sí** cuando pregunte.
- Acepta los permisos de administrador que aparezcan.
- Cuando termine, cierra y vuelve a abrir el script.

### 4. Primera vez: iniciar sesión en GitHub

Cuando hagas click en cualquier acción, el script te pedirá iniciar sesión en
GitHub **sin abrir un navegador automáticamente**:

1. El script muestra un **código** grande tipo `XXXX-XXXX`.
2. Abre en tu navegador (o **celular**): https://github.com/login/device
3. Ingresa el código (puedes copiarlo con el botón "Copiar código").
4. Inicia sesión en GitHub si no estás conectado.
5. Autoriza el acceso a "GitHub CLI".
6. El script detecta automáticamente la autorización y continúa.

**Ventaja:** puedes usar tu celular para hacer el login. No necesitas navegador
en la PC donde corre el script.

Esto es **solo una vez**. La sesión queda guardada.

### 5. Panel de cuenta

En la esquina superior derecha verás siempre quién está conectado:

- **Cuenta de GitHub**: `@usuario` + email
- **Botón "Iniciar sesión"**: cuando no hay sesión activa
- **Botón "Cerrar sesión"**: borra todas las credenciales del equipo y permite cambiar de cuenta

### 6. Subir tu evaluación

Hay **dos modos** de subida:

#### Modo A: Crear repositorio nuevo (default)

Para alumnos que vienen sin preparación previa.

1. Selecciona **"Crear repositorio nuevo"**.
2. Completa **Nombre completo** (ej: `Juan Pérez García`).
3. Elige o escribe **Forma de prueba** (ej: `Forma-A`).
4. Click **Buscar...** y selecciona la carpeta de tu evaluación.
5. Elige visibilidad: **Privado** (recomendado) o **Público**.
6. Click **Hacer TODO**.

El script crea el repositorio con formato `nombre-completo-forma` y sube todo.

#### Modo B: Usar repositorio existente

Para alumnos que ya crearon el repo en su casa.

1. Selecciona **"Usar repositorio existente de mi cuenta"**.
2. El script carga automáticamente la lista de repos de tu cuenta.
3. Elige tu repo del **dropdown** (los privados aparecen con `[Priv]`, públicos con `[Pub]`).
4. Si creaste un repo nuevo recientemente, click **Refrescar** para actualizar la lista.
5. Click **Buscar...** y selecciona la carpeta de tu evaluación.
6. Click **Hacer TODO**.

El script valida que el repo existe y sube los archivos sin crearlo.

#### Lo que hace el script en ambos modos

- Configura tu nombre y email en el repositorio local.
- Ejecuta `git init`, `git add`, `git commit`, `git push`.
- Te muestra el link al final.

---

## Botones disponibles

| Botón                 | Qué hace                                                 |
|-----------------------|----------------------------------------------------------|
| **1. Crear Repo**     | Solo crea el repositorio vacío en GitHub.                |
| **2. Subir Archivos** | Sube los archivos a un repositorio ya existente.         |
| **Hacer TODO**        | Hace los dos pasos. **Recomendado si es la primera vez.**|
| **Iniciar sesión**    | Inicia sesión en GitHub con código (sin navegador).      |
| **Cerrar sesión**     | Borra todas las credenciales del equipo.                 |

---

## Problemas comunes

### "winget no se reconoce..."

Tu Windows no tiene `winget`. Instala **App Installer** desde Microsoft Store
o descarga manualmente:
- Git: https://git-scm.com/download/win
- GitHub CLI: https://cli.github.com/

### "No tienes una sesión de GitHub activa"

Click **Sí** cuando el script ofrezca iniciar sesión. Sigue las instrucciones del
diálogo con el código.

### "Repositorio ya existe"

El script lo detecta y reutiliza ese mismo repositorio. No es un error.

### "Falló el push"

Posibles causas:
- Conexión a internet interrumpida.
- Token expirado: usa el botón **Cerrar sesión** y vuelve a iniciar sesión.
- El repositorio ya tenía commits desde otro lado: el script no sobrescribe historial.

### Quiero cambiar de cuenta de GitHub

Opción A (recomendada): usa el botón **Cerrar sesión** dentro del script y luego
**Iniciar sesión** con la nueva cuenta.

Opción B: ejecuta `Reset-GitHubAuth.ps1` (click derecho → Ejecutar con PowerShell).
Limpia todas las credenciales y permite iniciar sesión con otra cuenta.

```powershell
powershell -ExecutionPolicy Bypass -File Reset-GitHubAuth.ps1
```

---

## Cómo se genera el nombre del repositorio

El nombre se sanitiza automáticamente:

- Todo en minúsculas
- Espacios convertidos a guiones (`-`)
- Acentos y caracteres especiales eliminados

Ejemplos:

| Nombre + Forma                  | Repositorio generado           |
|---------------------------------|--------------------------------|
| `Juan Pérez García` + `Forma-A` | `juan-perez-garcia-forma-a`    |
| `María López` + `Forma B`       | `maria-lopez-forma-b`          |
| `Ñoño Iñíguez` + `Final`        | `nono-iniguez-final`           |

---

## Seguridad

- El script **no envía tus credenciales a ningún servidor externo**.
- Toda la autenticación pasa por `gh` (la herramienta oficial de GitHub).
- Las credenciales se guardan en el **Credential Manager de Windows**, encriptadas.
- Puedes revisar el código fuente abriendo `Subir-Evaluacion.ps1` con cualquier editor.
- El botón **Cerrar sesión** limpia todas las credenciales del equipo (gh CLI + Credential Manager + caché de git).

---

## Soporte

Si algo no funciona, contacta a tu profesor con:
- Captura de pantalla del error.
- Contenido del log negro de la ventana (cópialo y pégalo).
