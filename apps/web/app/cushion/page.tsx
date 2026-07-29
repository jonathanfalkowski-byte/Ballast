import Link from "next/link";
import CushionCalculator from "@/components/CushionCalculator";

export default function CushionPage() {
  return (
    <main className="mx-auto max-w-3xl px-6 py-14">
      <Link href="/" className="text-sm text-[#4da3ff] hover:underline">
        ← Ballast
      </Link>
      <h1 className="mt-4 text-3xl font-extrabold tracking-tight">Trailing-drawdown cushion calculator</h1>
      <p className="mt-3 text-[#9aa7b4]">
        The two numbers that keep a prop account alive: how much room you have to the trailing floor,
        and the biggest size you can take without threatening it. Free, no signup.
      </p>
      <div className="mt-8">
        <CushionCalculator />
      </div>
      <footer className="mt-14 border-t border-[#2a333f] pt-6 text-sm text-[#7f8b98]">
        Confirm your firm's exact trailing rules (intraday vs end-of-day) before relying on any number
        here. Not financial advice.
      </footer>
    </main>
  );
}
