import { NextResponse } from "next/server";
import { getPool } from "@/lib/db";
import {
  WAITLIST_MAX_BODY_BYTES,
  clientAddress,
  normalizeWaitlistInput,
  waitlistLimiter,
} from "@/lib/waitlistGuard";

export async function POST(req: Request) {
  const contentType = req.headers.get("content-type")?.toLowerCase() ?? "";
  if (!contentType.startsWith("application/json")) {
    return NextResponse.json({ error: "unsupported_media_type" }, { status: 415 });
  }

  const declaredLength = Number(req.headers.get("content-length") ?? 0);
  if (Number.isFinite(declaredLength) && declaredLength > WAITLIST_MAX_BODY_BYTES) {
    return NextResponse.json({ error: "payload_too_large" }, { status: 413 });
  }

  if (!waitlistLimiter.allow(clientAddress(req.headers))) {
    return NextResponse.json({ error: "rate_limited" }, { status: 429 });
  }

  let input;
  try {
    const rawBody = await req.text();
    if (new TextEncoder().encode(rawBody).byteLength > WAITLIST_MAX_BODY_BYTES) {
      return NextResponse.json({ error: "payload_too_large" }, { status: 413 });
    }
    input = normalizeWaitlistInput(JSON.parse(rawBody));
  } catch {
    return NextResponse.json({ error: "bad_request" }, { status: 400 });
  }

  if (!input) {
    return NextResponse.json({ error: "invalid_email" }, { status: 400 });
  }

  if (!process.env.DATABASE_URL) {
    if (process.env.NODE_ENV === "production") {
      return NextResponse.json({ error: "service_unavailable" }, { status: 503 });
    }
    return NextResponse.json({ ok: true, stored: false });
  }

  try {
    await getPool().query(
      `INSERT INTO waitlist_signups (email, source)
       VALUES ($1, $2)
       ON CONFLICT (email) DO NOTHING`,
      [input.email, input.source],
    );
    return NextResponse.json({ ok: true, stored: true });
  } catch (err) {
    console.error("[waitlist] insert failed", err instanceof Error ? err.name : "UnknownError");
    return NextResponse.json({ error: "server_error" }, { status: 500 });
  }
}
