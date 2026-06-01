"use client";

import { useState } from "react";
import { supabase } from "@/lib/supabase";

// Email/password login gate. Replicates the original #loginView.
export function LoginView() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function handleLogin() {
    setError("");
    if (!email.trim() || !password) {
      setError("Completa email y contraseña.");
      return;
    }
    setBusy(true);
    const { error: err } = await supabase.auth.signInWithPassword({
      email: email.trim(),
      password,
    });
    setBusy(false);
    if (err) setError(err.message || "Error al iniciar sesión.");
    // On success, the onAuthStateChange listener in App swaps to the panel.
  }

  return (
    <div className="login-view">
      <div className="card login-card">
        <h1>Panel del Profesor</h1>
        <p className="subtitle">Ingresa tus credenciales docentes.</p>
        <div className="field">
          <label htmlFor="email">Correo</label>
          <input
            type="email"
            id="email"
            autoComplete="email"
            placeholder="tu-email@duoc.cl"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleLogin()}
          />
        </div>
        <div className="field">
          <label htmlFor="password">Contraseña</label>
          <input
            type="password"
            id="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleLogin()}
          />
        </div>
        <button
          className="btn-primary"
          style={{ width: "100%", padding: 14, height: "auto" }}
          onClick={handleLogin}
          disabled={busy}
        >
          {busy ? "Ingresando..." : "Iniciar sesión"}
        </button>
        {error ? <div className="err">{error}</div> : null}
      </div>
    </div>
  );
}
