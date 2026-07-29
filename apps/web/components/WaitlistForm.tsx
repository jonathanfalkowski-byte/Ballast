"use client";

import { useState } from "react";

export default function WaitlistForm() {
  const [email, setEmail] = useState("");
  const [status, setStatus] = useState<"idle" | "loading" | "ok" | "error">("idle");
  const [msg, setMsg] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!email) return;
    setStatus("loading");
    try {
      const res = await fetch("/api/waitlist", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, source: "landing" }),
      });
      if (!res.ok) throw new Error();
      setStatus("ok");
      setMsg("You're on the list. I'll email you when the first build is ready.");
      setEmail("");
    } catch {
      setStatus("error");
      setMsg("Something went wrong — try again in a moment.");
    }
  }

  return (
    <div className="w-full max-w-md">
      <form onSubmit={submit} className="flex gap-2">
        <input
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="you@email.com"
          className="flex-1 rounded-lg border border-[#2a333f] bg-[#0e141b] px-4 py-3 text-[15px] outline-none focus:border-[#3fb950]"
        />
        <button
          type="submit"
          disabled={status === "loading"}
          className="rounded-lg bg-[#3fb950] px-5 py-3 text-[15px] font-semibold text-[#08240f] disabled:opacity-60"
        >
          {status === "loading" ? "…" : "Get early access"}
        </button>
      </form>
      {msg && (
        <p className={`mt-3 text-sm ${status === "ok" ? "text-[#3fb950]" : "text-[#f4523b]"}`}>{msg}</p>
      )}
    </div>
  );
}
