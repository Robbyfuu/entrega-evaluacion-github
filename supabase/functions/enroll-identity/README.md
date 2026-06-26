# Edge Function: `enroll-identity`

Enrola la identidad verificada de un alumno. Recibe el token de GitHub obtenido
por device-flow, lo valida contra la API de GitHub y emite un **JWT HS256**
firmado con el JWT Secret legacy del proyecto. Ese JWT lleva la identidad
GitHub verificada (`github_username`, `github_id`) y mantiene `role: "anon"`,
de modo que el alumno conserva exactamente las policies anon vigentes pero ahora
portando una identidad infalsificable.

## Contrato

```
POST {SUPABASE_URL}/functions/v1/enroll-identity
Headers:
  apikey: <anon key>
  Authorization: Bearer <anon key>
  Content-Type: application/json
Body:
  { "github_token": "<gho_... token del device-flow del alumno>" }
```

Respuesta `200`:

```json
{
  "token": "<jwt>",
  "github_username": "octocat",
  "exp": 1750000000
}
```

Claims del JWT emitido:

```json
{
  "role": "anon",
  "aud": "authenticated",
  "iss": "exam-enroll",
  "github_username": "octocat",
  "github_id": 583231,
  "iat": 1749957000,
  "exp": 1750000200
}
```

> **Seguridad:** `role` DEBE ser `"anon"`. Usar `"authenticated"` daria
> privilegios de profesor (god-mode). El JWT no eleva permisos; solo agrega
> identidad verificada a las policies anon existentes.

### Errores

| Situacion                          | Codigo |
| ---------------------------------- | ------ |
| Body sin `github_token`            | `400`  |
| Token de GitHub invalido / 401     | `401`  |
| Metodo distinto de POST            | `405`  |
| `JWT_SECRET` no configurado / error| `500`  |

## Deploy

```bash
supabase functions deploy enroll-identity
```

## Configurar el secret

`JWT_SECRET` debe ser **exactamente** el "JWT Secret" legacy del dashboard
(Settings -> API -> JWT Settings -> JWT Secret). Es la misma clave con la que
PostgREST valida los JWT; si no coincide, los tokens emitidos seran rechazados.

```bash
supabase secrets set JWT_SECRET=<el JWT Secret legacy del dashboard>
```

Para verificar:

```bash
supabase secrets list
```

## Prueba con curl

```bash
curl -i -X POST \
  "https://<project-ref>.supabase.co/functions/v1/enroll-identity" \
  -H "apikey: <ANON_KEY>" \
  -H "Authorization: Bearer <ANON_KEY>" \
  -H "Content-Type: application/json" \
  -d '{"github_token":"gho_xxxxxxxxxxxxxxxxxxxx"}'
```

Respuesta esperada (`200`):

```json
{ "token": "eyJhbGci...", "github_username": "octocat", "exp": 1750000200 }
```

Token invalido -> `401 { "error": "invalid_github_token" }`.

## Uso en el cliente

- Tras enrolar, usar el JWT devuelto como `Authorization: Bearer <jwt>` junto al
  `apikey: <anon>` en TODAS las llamadas REST/RPC.
- Si todavia no hay JWT (alumno no enrolado), usar el anon key como Bearer
  (comportamiento actual, backward-compatible).
- Refrescar el JWT llamando de nuevo a esta function cuando expire (12 h) o ante
  cualquier `401`.

## Lectura del claim en DB (RPC / RLS)

```sql
current_setting('request.jwt.claims', true)::json ->> 'github_username'
```

**Regla FASE 1 (actual, backward-compatible):**

- Si el claim es NOT NULL / no-vacio y `DISTINCT FROM` el username afirmado ->
  RECHAZAR (RPC: `RETURN` no-op; RLS: `WITH CHECK false`).
- Si el claim es NULL (cliente viejo con anon key crudo) -> permitir.

**FASE 2 (futuro):** invertir la regla para **requerir** el claim (rechazar
cuando sea NULL), una vez que todos los clientes esten enrolando identidad.
