import { describe, expect, it } from "vitest";
import {
  WaitlistRateLimiter,
  clientAddress,
  normalizeWaitlistInput,
} from "../waitlistGuard";

describe("waitlist input", () => {
  it("normalizes a bounded email and allowlisted source", () => {
    expect(normalizeWaitlistInput({ email: "  Founder@Example.com ", source: "addon" })).toEqual({
      email: "founder@example.com",
      source: "addon",
    });
  });

  it("rejects malformed or oversized emails", () => {
    expect(normalizeWaitlistInput({ email: "not-an-email" })).toBeNull();
    expect(normalizeWaitlistInput({ email: `${"a".repeat(250)}@x.com` })).toBeNull();
  });

  it("does not accept arbitrary source values", () => {
    expect(normalizeWaitlistInput({ email: "a@example.com", source: "<script>" })?.source).toBe(
      "landing",
    );
  });

  it("prefers the platform-provided real IP header", () => {
    const headers = new Headers({ "x-real-ip": "203.0.113.10", "x-forwarded-for": "198.51.100.1" });
    expect(clientAddress(headers)).toBe("203.0.113.10");
  });
});

describe("waitlist rate limiter", () => {
  it("blocks after the bounded number of attempts", () => {
    const limiter = new WaitlistRateLimiter("test-secret", 2, 1_000);
    expect(limiter.allow("203.0.113.10", 0)).toBe(true);
    expect(limiter.allow("203.0.113.10", 1)).toBe(true);
    expect(limiter.allow("203.0.113.10", 2)).toBe(false);
    expect(limiter.allow("198.51.100.1", 2)).toBe(true);
  });

  it("starts a fresh counter after the window", () => {
    const limiter = new WaitlistRateLimiter("test-secret", 1, 1_000);
    expect(limiter.allow("203.0.113.10", 0)).toBe(true);
    expect(limiter.allow("203.0.113.10", 999)).toBe(false);
    expect(limiter.allow("203.0.113.10", 1_000)).toBe(true);
  });
});
