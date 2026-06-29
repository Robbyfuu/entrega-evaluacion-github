import type { NextConfig } from "next";
import { fileURLToPath } from "url";
import { dirname } from "path";

const projectRoot = dirname(fileURLToPath(import.meta.url));

const nextConfig: NextConfig = {
  // Client-only SPA behind Supabase auth. No server secrets, no route handlers.
  // Static export produces an `out/` folder deployable to any static host.
  output: "export",
  images: { unoptimized: true },
  // Avoids hydration issues with extensions and keeps URLs clean on static hosts.
  trailingSlash: true,
  // Pin the workspace root so Next does not infer it from unrelated lockfiles
  // elsewhere on the machine. Next 16 requires outputFileTracingRoot and
  // turbopack.root to be the SAME value, so both are pinned to projectRoot
  // (otherwise Next infers outputFileTracingRoot from a parent lockfile and warns).
  outputFileTracingRoot: projectRoot,
  turbopack: { root: projectRoot },
};

export default nextConfig;
