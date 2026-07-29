import Link from "next/link";
import SessionConsole from "@/components/SessionConsole";

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
