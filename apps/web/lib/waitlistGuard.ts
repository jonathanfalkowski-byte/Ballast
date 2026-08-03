import { createHash, randomBytes } from "node:crypto";

export const WAITLIST_MAX_BODY_BYTES = 4096;
export const WAITLIST_WINDOW_MS = 10 * 60 * 1000;
export const WAITLIST_MAX_ATTEMPTS = 5;

const ALLOWED_SOURCES = new Set(["landing", "addon", "cushion", "readiness", "rules"]);

export type WaitlistInput = { email: string; source: string };

export function normalizeWaitlistInput(body: unknown): WaitlistInput | null {
  if (!body || typeof body !== "object" || Array.isArray(body)) return null;
  const record = body as Record<string, unknown>;
  if (typeof record.email !== "string") return null;

  const email = record.email.trim().toLowerCase();
  if (
    email.length === 0 ||
    email.length > 254 ||
    !/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(email)
  ) {
    return null;
  }

  const requestedSource = typeof record.source === "string" ? record.source.trim() : "landing";
  const source = ALLOWED_SOURCES.has(requestedSource) ? requestedSource : "landing";
  return { email, source };
}

export function clientAddress(headers: Headers): string {
  const realIp = headers.get("x-real-ip")?.trim();
  if (realIp) return realIp;
  const forwarded = headers.get("x-forwarded-for")?.split(",", 1)[0]?.trim();
  return forwarded || "unknown";
}

type Counter = { count: number; windowStartedAt: number };

/**
 * Single-process abuse brake. Production should also enforce an edge/platform
 * limit because serverless instances do not share memory.
 */
export class WaitlistRateLimiter {
  private readonly counters = new Map<string, Counter>();

  constructor(
    private readonly secret: string,
    private readonly maxAttempts = WAITLIST_MAX_ATTEMPTS,
    private readonly windowMs = WAITLIST_WINDOW_MS,
    private readonly maxKeys = 10_000,
  ) {}

  allow(address: string, now = Date.now()): boolean {
    const key = createHash("sha256").update(this.secret).update("\0").update(address).digest("hex");
    const existing = this.counters.get(key);

    if (!existing || now - existing.windowStartedAt >= this.windowMs) {
      if (this.counters.size >= this.maxKeys) this.prune(now);
      this.counters.set(key, { count: 1, windowStartedAt: now });
      return true;
    }

    existing.count += 1;
    return existing.count <= this.maxAttempts;
  }

  private prune(now: number) {
    for (const [key, counter] of this.counters) {
      if (now - counter.windowStartedAt >= this.windowMs) this.counters.delete(key);
    }
    while (this.counters.size >= this.maxKeys) {
      const oldest = this.counters.keys().next().value as string | undefined;
      if (!oldest) break;
      this.counters.delete(oldest);
    }
  }
}

const limiterSecret = process.env.WAITLIST_RATE_LIMIT_SECRET || randomBytes(32).toString("hex");
export const waitlistLimiter = new WaitlistRateLimiter(limiterSecret);
