"use client";

import App from "@/components/App";

// Thin client entry: the whole admin panel is a client-side SPA behind Supabase
// auth, so the page just mounts the App auth gate (LoginView | Panel).
export default function Home() {
  return <App />;
}
