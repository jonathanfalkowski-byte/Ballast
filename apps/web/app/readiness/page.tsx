import type { Metadata } from "next";
import Link from "next/link";
import ReadinessTool from "@/components/ReadinessTool";

export const metadata: Metadata = {
  title: "Am I ready to pass a prop firm challenge? Free odds calculator",
  description:
    "Paste your recent trades and run thousands of rule-aware simulations against your firm's drawdown and profit target. Get an honest estimate of your odds of a payout before a breach — including whether you should skip buying the evaluation. Free, no signup.",
  alternates: { canonical: "https://tradeballast.com/readiness" },
  openGraph: {
    title: "Should you buy that prop firm evaluation? Find out first.",
    description:
      "Rule-aware Monte Carlo on your own trades. If your edge isn't there yet, it tells you — and saves you the fee.",
    url: "https://tradeballast.com/readiness",
  },
};

export default function ReadinessPage() {
  return (
    <main className="mx-auto max-w-3xl px-6 py-14">
      <Link href="/" className="text-sm text-[#4da3ff] hover:underline">← Ballast</Link>
      <h1 className="mt-4 text-3xl font-extrabold tracking-tight">Challenge-readiness check</h1>
      <p className="mt-3 text-[#9aa7b4]">
        Before you pay for another evaluation, find out what your own trades say about your odds. Paste your recent sim
        or live P&amp;L, pick your account rules, and Ballast runs thousands of rule-aware simulations from <em>your</em> distribution
        to estimate your chance of reaching a payout before breaching. If the edge isn&apos;t there yet, it&apos;ll tell you honestly —
        so you keep the fee.
      </p>
      <div className="mt-8">
        <ReadinessTool />
      </div>
    </main>
  );
}
