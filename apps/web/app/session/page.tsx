import type { Metadata } from "next";
import Link from "next/link";
import SessionConsole from "@/components/SessionConsole";

export const metadata: Metadata = {
  title: "Live trading discipline engine — stop rules, tilt window, cushion",
  description:
    "Watch a discipline engine react to a trading session in real time: it flags the revenge-trade window after a loss, warns when your drawdown cushion gets thin, and calls a hard stop at your max losses. Interactive demo, no signup.",
  alternates: { canonical: "https://tradeballast.com/session" },
  openGraph: {
    title: "A discipline engine that tells you to stop — live demo",
    description:
      "Take two losses and watch it flip to Stop. The layer every trading journal is missing.",
    url: "https://tradeballast.com/session",
  },
};

export default function SessionPage() {
  return (
    <main className="mx-auto max-w-3xl px-6 py-14">
      <Link href="/" className="text-sm text-[#4da3ff] hover:underline">
        ← Ballast
      </Link>
      <h1 className="mt-4 text-3xl font-extrabold tracking-tight">Live session console</h1>
      <p className="mt-3 text-[#9aa7b4]">
        The engine, running. Log trades and watch the next-action card react — take two losers and it flips to
        <span className="text-[#f4523b]"> Stop</span>; trade fresh off a loss and it calls the
        <span className="text-[#e3b341]"> cooldown</span>. This is the core of the product, driven entirely by
        <code className="mx-1 rounded bg-[#0e141b] px-1.5 py-0.5 text-[13px] text-[#bcd6ef]">disciplineEngine.ts</code>.
      </p>
      <div className="mt-8">
        <SessionConsole />
      </div>
    </main>
  );
}
