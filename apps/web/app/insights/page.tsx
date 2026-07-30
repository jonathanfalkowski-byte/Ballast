import Link from "next/link";
import InsightsTool from "@/components/InsightsTool";

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
