// Thin pg pool for API routes. Mirrors the shared @ballast/db helper.
import { Pool } from "pg";

let pool: Pool | undefined;

export function getPool(): Pool {
  if (!pool) {
    const connectionString = process.env.DATABASE_URL;
    // Hosted Postgres (Neon/Vercel) requires SSL; local Docker does not.
    const isLocal =
      !connectionString ||
      connectionString.includes("localhost") ||
      connectionString.includes("127.0.0.1");
    pool = new Pool({
      connectionString,
      ssl: isLocal ? undefined : { rejectUnauthorized: false },
    });
  }
  return pool;
}
