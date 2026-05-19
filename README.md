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
automáticamente `git` y `gh` (GitHub CLI) si no los tienes.

---

## Cómo usar

### 1. Descomprimir

Extrae el ZIP en una carpeta. Vas a ver estos archivos:

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

### 3. Primera vez: instalar dependencias

Si es la primera vez, el script te ofrecerá instalar `git` y `gh` con `winget`.

- Click **Sí** cuando pregunte.
- Acepta los permisos de administrador que aparezcan.
- Cuando termine, **cerrá y reabrí el script**.

### 4. Primera vez: login en GitHub

Cuando hagas click en cualquier acción, el script te pedirá autenticarte en GitHub
con un flujo simple **sin abrir navegador automáticamente**:

1. El script te muestra un **código** grande tipo `XXXX-XXXX`.
2. Abrí en tu navegador (o **celular**): https://github.com/login/device
3. Ingresá el código (podés copiarlo con el botón "Copiar código").
4. Iniciá sesión en GitHub si no estás logueado.
5. Autorizá el acceso a "GitHub CLI".
6. El script detecta automáticamente la autorización y continúa.

**Ventaja:** podés usar tu celular para hacer el login. No necesitás navegador
en la PC donde corre el script.

Esto es **solo una vez**. Quedás logueado permanentemente.

**Botón "Iniciar sesión"** (esquina superior derecha): te permite volver a
loguearte o cambiar de cuenta cuando quieras.

### 5. Subir tu evaluación

1. Completá **Nombre completo** (ej: `Juan Pérez García`).
2. Elegí o escribí **Forma de prueba** (ej: `Forma-A`).
3. Click **Buscar...** y seleccioná la carpeta de tu evaluación.
4. Elegí visibilidad: **Privado** (recomendado) o **Público**.
5. Click **Hacer TODO**.

El script:
- Crea el repo en tu GitHub.
- Hace `git init`, `git add`, `git commit`, `git push`.
- Te muestra el link al final.

---

## Botones disponibles

| Botón              | Qué hace                                                 |
|--------------------|----------------------------------------------------------|
| **1. Crear Repo**  | Solo crea el repositorio vacío en GitHub.                |
| **2. Subir Archivos** | Sube los archivos a un repo ya existente.             |
| **Hacer TODO**     | Hace los dos pasos. **Usá este si es la primera vez.**   |

---

## Problemas comunes

### "winget no se reconoce..."

Tu Windows no tiene `winget`. Instalá **App Installer** desde Microsoft Store
o descargá manualmente:
- Git: https://git-scm.com/download/win
- GitHub CLI: https://cli.github.com/

### "No estás autenticado en GitHub"

Click **Sí** cuando ofrezca ejecutar `gh auth login`. Seguí las instrucciones
en la ventana que abre.

### "Repo ya existe"

El script lo detecta y reutiliza ese mismo repo. No es error.

### "Falló el push"

Posibles causas:
- Conexión a internet caída.
- Token expirado → usar `Reset-GitHubAuth.ps1` para limpiar y re-loguearte.
- El repo ya tenía commits desde otro lado → el script no sobrescribe historia.

### Quiero cambiar de cuenta de GitHub

Ejecutá `Reset-GitHubAuth.ps1` (botón derecho → Ejecutar con PowerShell).
Limpia todas las credenciales y te permite loguearte con otra cuenta.

```powershell
powershell -ExecutionPolicy Bypass -File Reset-GitHubAuth.ps1
```

---

## Cómo se genera el nombre del repo

El nombre se sanitiza automáticamente:

- Todo minúsculas
- Espacios → guiones (`-`)
- Quita acentos y caracteres especiales

Ejemplos:

| Nombre + Forma                  | Repo generado                  |
|---------------------------------|--------------------------------|
| `Juan Pérez García` + `Forma-A` | `juan-perez-garcia-forma-a`    |
| `María López` + `Forma B`       | `maria-lopez-forma-b`          |
| `Ñoño Iñíguez` + `Final`        | `nono-iniguez-final`           |

---

## Seguridad

- El script **no envía tus credenciales a ningún servidor externo**.
- Toda la autenticación pasa por `gh` (la herramienta oficial de GitHub).
- Las credenciales se guardan en el **Credential Manager de Windows**, encriptadas.
- Podés revisar el código abriendo `Subir-Evaluacion.ps1` con cualquier editor.

---

## Soporte

Si algo no funciona, contactá a tu profesor con:
- Captura de pantalla del error.
- Contenido del log negro de la ventana (copiá y pegá).
