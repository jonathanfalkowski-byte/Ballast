import type { Metadata } from "next";
import Link from "next/link";
import InsightsTool from "@/components/InsightsTool";

export const metadata: Metadata = {
  title: "What do revenge trades actually cost you? Free calculator",
  description:
    "Tag your trades by what drove them — plan, A+ setup, revenge, FOMO, boredom — and see your expectancy split by behaviour. Most traders find their planned trades are profitable and their emotional ones hand it all back. Free, no signup.",
  alternates: { canonical: "https://tradeballast.com/insights" },
  openGraph: {
    title: "What your revenge trades cost you",
    description:
      "Your win rate is an average, and averages hide what's killing the account. See your edge broken out by behaviour.",
    url: "https://tradeballast.com/insights",
  },
};

export default function InsightsPage() {
  return (
    <main className="mx-auto max-w-3xl px-6 py-14">
      <Link href="/" className="text-sm text-[#4da3ff] hover:underline">← Ballast</Link>
      <h1 className="mt-4 text-3xl font-extrabold tracking-tight">What your revenge trades cost you</h1>
      <p className="mt-3 text-[#9aa7b4]">
        Your overall win rate is an average, and averages hide the thing that&apos;s actually killing the
        account. Tag each trade by what drove it, and the damage separates out: the planned trades that
        make money, and the emotional ones that hand it back. Free, no signup.
      </p>
      <div className="mt-8">
        <InsightsTool />
      </div>
    </main>
  );
}
