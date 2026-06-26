// Edge Function: enroll-identity
//
// Proposito: enrolar la identidad verificada de un alumno a partir de su token
// de GitHub (obtenido por device-flow). La function valida el token contra la
// API de GitHub, extrae el login y el id reales del usuario y emite un JWT
// HS256 firmado con el JWT Secret legacy del proyecto.
//
// El JWT resultante lleva role "anon" a proposito: el alumno conserva las
// mismas policies anon vigentes, pero ahora portando una identidad GitHub
// verificada e infalsificable. Usar role "authenticated" daria privilegios de
// profesor (god-mode), por lo que esta PROHIBIDO.
//
// Contrato:
//   POST /functions/v1/enroll-identity
//   Headers: apikey, Authorization: Bearer <anon>, Content-Type: application/json
//   Body: { "github_token": "<gho_... token del device-flow>" }
//   200:  { "token": "<jwt>", "github_username": login, "exp": <epoch> }

import {
  create,
  getNumericDate,
  type Header,
  type Payload,
} from "https://deno.land/x/djwt@v3.0.2/mod.ts";

// Duracion del JWT emitido: 12 horas.
const TOKEN_TTL_SECONDS = 12 * 60 * 60;

// User-Agent es obligatorio para la API de GitHub; sin el responde 403.
const GITHUB_USER_AGENT = "exam-enroll-edge-function";

const JWT_ISSUER = "exam-enroll";

// CORS basico. El cliente es una app de escritorio/CLI, asi que "*" alcanza.
const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

// Forma minima del usuario de GitHub que nos interesa.
interface GitHubUser {
  login: string;
  id: number;
}

// Helper para respuestas JSON con CORS ya aplicado.
function jsonResponse(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS_HEADERS, "Content-Type": "application/json" },
  });
}

// Construye la CryptoKey HMAC-SHA256 a partir del JWT Secret legacy.
async function buildSigningKey(secret: string): Promise<CryptoKey> {
  const keyData = new TextEncoder().encode(secret);
  return await crypto.subtle.importKey(
    "raw",
    keyData,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign", "verify"],
  );
}

// Verifica el token contra GitHub y devuelve el usuario o null si es invalido.
async function fetchGitHubUser(githubToken: string): Promise<GitHubUser | null> {
  const response = await fetch("https://api.github.com/user", {
    headers: {
      Authorization: `Bearer ${githubToken}`,
      Accept: "application/vnd.github+json",
      "User-Agent": GITHUB_USER_AGENT,
    },
  });

  if (!response.ok) {
    return null;
  }

  const user = (await response.json()) as Partial<GitHubUser>;

  // Validacion defensiva: GitHub debe devolver login (string) e id (number).
  if (typeof user.login !== "string" || typeof user.id !== "number") {
    return null;
  }

  return { login: user.login, id: user.id };
}

Deno.serve(async (request: Request): Promise<Response> => {
  // Preflight CORS.
  if (request.method === "OPTIONS") {
    return new Response("ok", { headers: CORS_HEADERS });
  }

  if (request.method !== "POST") {
    return jsonResponse({ error: "method_not_allowed" }, 405);
  }

  // El secret debe estar configurado en el entorno de la function.
  const jwtSecret = Deno.env.get("JWT_SECRET");
  if (!jwtSecret) {
    console.error("JWT_SECRET no esta configurado en el entorno");
    return jsonResponse({ error: "server_misconfigured" }, 500);
  }

  // 1. Leer github_token del body.
  let githubToken: string;
  try {
    const body = await request.json();
    githubToken = body?.github_token;
  } catch {
    return jsonResponse({ error: "invalid_json_body" }, 400);
  }

  if (typeof githubToken !== "string" || githubToken.length === 0) {
    return jsonResponse({ error: "missing_github_token" }, 400);
  }

  // 2 y 3. Verificar el token contra GitHub y extraer login e id reales.
  let githubUser: GitHubUser | null;
  try {
    githubUser = await fetchGitHubUser(githubToken);
  } catch (error) {
    console.error("Error consultando la API de GitHub:", error);
    return jsonResponse({ error: "github_request_failed" }, 500);
  }

  if (!githubUser) {
    return jsonResponse({ error: "invalid_github_token" }, 401);
  }

  // 4. Emitir el JWT HS256 con los claims del contrato.
  try {
    const signingKey = await buildSigningKey(jwtSecret);
    const issuedAt = getNumericDate(0); // ahora, en epoch seconds
    const expiresAt = getNumericDate(TOKEN_TTL_SECONDS); // ahora + 12h

    const header: Header = { alg: "HS256", typ: "JWT" };

    const payload: Payload = {
      // role DEBE ser "anon" (ver nota de seguridad arriba).
      role: "anon",
      aud: "authenticated",
      iss: JWT_ISSUER,
      github_username: githubUser.login,
      github_id: githubUser.id,
      iat: issuedAt,
      exp: expiresAt,
    };

    const token = await create(header, payload, signingKey);

    return jsonResponse(
      {
        token,
        github_username: githubUser.login,
        exp: expiresAt,
      },
      200,
    );
  } catch (error) {
    console.error("Error firmando el JWT:", error);
    return jsonResponse({ error: "token_signing_failed" }, 500);
  }
});
