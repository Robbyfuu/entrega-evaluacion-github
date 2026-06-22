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
- Cuenta en [GitHub](https://github.com) (gratuita — el script tiene un asistente para crearla)
- Conexión a internet

**No necesitas instalar nada manualmente.** El script detecta y ofrece instalar
automáticamente `git` y `gh` (GitHub CLI) si no están presentes.

### ¿No tienes cuenta de GitHub?

En la esquina superior derecha del script verás el link:

> **¿No tienes cuenta de GitHub? Créala aquí**

Al hacer clic se abre un asistente con instrucciones paso a paso para crear tu
cuenta. Te lleva al formulario de GitHub, te guía con el email + contraseña +
verificación, y al terminar te ofrece iniciar sesión directamente.

---

## Cómo usar

### 1. Descargar la app

Descarga el instalador desde la sección **Releases** de este repositorio
(app autocontenida generada con Velopack). No requiere instalar dependencias
manualmente: la app detecta y ofrece instalar `git` y `gh` (GitHub CLI) si
no están presentes.

> El cliente legacy basado en PowerShell (`Subir-Evaluacion.ps1`,
> `Subir-Evaluacion.bat`, `Reset-GitHubAuth.ps1`, `Reset-Internet.bat`)
> fue removido del repo. La app C# self-contained lo reemplaza.

### 2. Ejecutar

Doble-click en el ejecutable descargado.

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
3. Elige el **Tipo de evaluación**: `Evaluacion-1`, `Evaluacion-2`, `Evaluacion-3`, `Evaluacion-4` o `Examen`.
4. Click **Buscar...** y selecciona la carpeta de tu evaluación.
5. Click **Hacer TODO**.

El script crea el repositorio con formato `nombre-completo-tipo` (siempre **público** para que el profesor pueda verlo) y sube todo.

#### Modo B: Usar repositorio existente

Para alumnos que ya crearon el repo en su casa.

1. Selecciona **"Usar repositorio existente de mi cuenta"**.
2. El script carga automáticamente la lista de repos de tu cuenta.
3. Elige tu repo del **dropdown** (los privados aparecen con `[Priv]`, públicos con `[Pub]`).
4. Si creaste un repo nuevo recientemente, click **Refrescar** para actualizar la lista.
5. Click **Hacer TODO** (o **Clonar Repo**).

El script:
- Clona el repositorio en tu **Escritorio** (`Desktop\nombre-del-repo`).
- Te muestra la ruta donde quedó la carpeta.
- Abre **IDLE de Python** apuntando a esa carpeta para que edites tu evaluación.
- Auto-completa la ruta en "Carpeta del proyecto".

Cuando termines de editar:
1. Guarda los cambios en IDLE (`Ctrl+S`).
2. Vuelve a la ventana del script.
3. Click **Subir Archivos**.

#### Protección contra confusión de cuentas

Si seleccionas una carpeta que ya tiene un `.git` asociado a **otra cuenta de GitHub** (no a la tuya actual), el script:

- **NO permite** subir desde ahí.
- Muestra el nombre de la cuenta conflictiva.
- Sugiere cambiar de carpeta o cerrar sesión y entrar con la cuenta correcta.

Si el `.git` es de **tu cuenta** pero apunta a otro repo, el script pregunta antes de reinicializar.

#### Lo que hace el script en ambos modos

- Configura tu nombre y email en el repositorio local.
- Ejecuta `git init` (si hace falta), `git add`, `git commit`, `git push`.
- Te muestra el link final al repo en GitHub.

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

Usa el botón **Cerrar sesión** dentro de la app y luego **Iniciar sesión**
con la nueva cuenta. Limpia todas las credenciales y permite iniciar sesión
con otra cuenta.

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
- El botón **Cerrar sesión** limpia todas las credenciales del equipo (gh CLI + Credential Manager + caché de git).

---

## Soporte

Si algo no funciona, contacta a tu profesor con:
- Captura de pantalla del error.
- Contenido del log negro de la ventana (cópialo y pégalo).
