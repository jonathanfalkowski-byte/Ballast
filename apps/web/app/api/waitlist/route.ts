import { NextResponse } from "next/server";
import { getPool } from "@/lib/db";

export async function POST(req: Request) {
  let email = "";
  let source = "landing";
  try {
    const body = await req.json();
    email = String(body.email ?? "").trim().toLowerCase();
    source = String(body.source ?? "landing");
  } catch {
    return NextResponse.json({ error: "bad_request" }, { status: 400 });
  }

  if (!email || !email.includes("@")) {
    return NextResponse.json({ error: "invalid_email" }, { status: 400 });
  }

  // Graceful in dev with no DB configured: accept and log rather than crash.
  if (!process.env.DATABASE_URL) {
    console.log(`[waitlist] (no DATABASE_URL) would store: ${email} from ${source}`);
    return NextResponse.json({ ok: true, stored: false });
  }

  try {
    // Self-provision the table so a fresh database works with no separate migration step.
    await getPool().query(
      `CREATE TABLE IF NOT EXISTS waitlist_signups (
         id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
         email text NOT NULL UNIQUE,
         source text,
         created_at timestamptz NOT NULL DEFAULT now()
       )`,
    );
    await getPool().query(
      `INSERT INTO waitlist_signups (email, source)
       VALUES ($1, $2)
       ON CONFLICT (email) DO NOTHING`,
      [email, source],
    );
    return NextResponse.json({ ok: true, stored: true });
  } catch (err) {
    console.error("[waitlist] insert failed", err);
    return NextResponse.json({ error: "server_error" }, { status: 500 });
  }
}
