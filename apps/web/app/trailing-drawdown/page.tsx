import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Intraday vs end-of-day trailing drawdown, explained",
  description:
    "The trailing drawdown is the rule that ends most prop accounts, and the difference between intraday and end-of-day trailing decides how. A plain-English explanation with worked examples, plus a free calculator.",
  alternates: { canonical: "https://tradeballast.com/trailing-drawdown" },
  openGraph: {
    title: "Intraday vs end-of-day trailing drawdown, explained",
    description:
      "Why a floating winner that round-trips can still end your account — and how to work out your real cushion.",
    url: "https://tradeballast.com/trailing-drawdown",
  },
};

const FAQ = [
  {
    q: "What is a trailing drawdown?",
    a: "A moving loss limit that sits a fixed distance below the highest point your account has reached. As your balance makes new highs the limit follows it up, but it never moves back down. Touch it and the account is done.",
  },
  {
    q: "What is the difference between intraday and end-of-day trailing drawdown?",
    a: "Intraday trailing follows your highest equity during the session, including unrealised profit on an open trade. End-of-day trailing only recalculates once, at the daily close, based on your realised balance. Intraday is stricter, because a trade that goes green and then round-trips can still ratchet your floor up permanently.",
  },
  {
    q: "Does the trailing drawdown stop moving?",
    a: "On most firms it locks once you have banked enough profit, commonly around the starting balance plus a small buffer. After that the floor stays put. The exact lock point varies by firm and by account type, so confirm it with your own firm.",
  },
  {
    q: "Why does a $1,000 loss matter more than 1% on a $100,000 account?",
    a: "Because the advertised account size is not the money at risk. If that account carries a $3,000 trailing drawdown, a $1,000 loss consumes about a third of the entire buffer keeping the account alive. The buffer is the number that matters, not the headline size.",
  },
];

export default function TrailingDrawdownPage() {
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: FAQ.map((f) => ({
      "@type": "Question",
      name: f.q,
      acceptedAnswer: { "@type": "Answer", text: f.a },
    })),
  };

  return (
    <main className="mx-auto max-w-3xl px-6 py-14">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }} />

      <Link href="/" className="text-sm text-[#4da3ff] hover:underline">← Ballast</Link>

      <h1 className="mt-4 text-3xl font-extrabold tracking-tight">
        Intraday vs end-of-day trailing drawdown, explained
      </h1>
      <p className="mt-3 text-[17px] text-[#9aa7b4]">
        The trailing drawdown is the rule that ends most prop accounts, and most traders only really
        understand it after it has cost them one. Here is what it actually does, and why the intraday
        version is stricter than people expect.
      </p>

      <Section title="What a trailing drawdown actually is">
        <p>
          It is a loss limit that moves. It sits a fixed distance below the highest point your account
          has reached, and it follows you up as you make new highs — but it never comes back down.
        </p>
        <p>
          The important consequence: <strong className="text-white">a trailing drawdown does not punish
          losing. It punishes giving back.</strong> You can lose steadily and survive a surprisingly long
          time. Make a big gain and hand it back, and you can fail while still showing a profit for the day.
        </p>
      </Section>

      <Section title="A worked example">
        <p>
          Take a $100,000 account with a $3,000 trailing drawdown. The floor starts at $97,000.
        </p>
        <ul className="ml-5 list-disc space-y-1">
          <li>You run the balance up to $104,000. The floor ratchets to $101,000.</li>
          <li>You give back down to $100,500. You are still up $500 on the account — and you have failed,
            because you are below the floor.</li>
        </ul>
        <p>
          Nothing about that is a losing streak. It is one good run followed by a give-back, which is the
          single most common way funded accounts die.
        </p>
      </Section>

      <Section title="The difference between the two models">
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-[15px]">
            <thead>
              <tr className="border-b border-[#2a333f] text-left text-[13px] uppercase tracking-wide text-[#9aa7b4]">
                <th className="py-2 pr-4">Model</th>
                <th className="py-2 pr-4">What moves the floor</th>
                <th className="py-2">What it means for you</th>
              </tr>
            </thead>
            <tbody className="align-top">
              <tr className="border-b border-[#2a333f]">
                <td className="py-3 pr-4 font-semibold">Intraday trailing</td>
                <td className="py-3 pr-4 text-[#c7d1db]">
                  Your highest equity during the session, including unrealised profit on an open trade.
                </td>
                <td className="py-3 text-[#c7d1db]">
                  Stricter. A trade that goes $1,500 green and round-trips to flat still moves your floor
                  up $1,500, permanently. Bank profit rather than admiring it.
                </td>
              </tr>
              <tr>
                <td className="py-3 pr-4 font-semibold">End-of-day trailing</td>
                <td className="py-3 pr-4 text-[#c7d1db]">
                  Your realised balance, checked once at the daily close.
                </td>
                <td className="py-3 text-[#c7d1db]">
                  More forgiving intraday — floating swings during the session do not count. Give-back still
                  shows up as a red close.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p className="text-[14px] text-[#9aa7b4]">
          Firms differ, and they change their rules. Apex-style accounts have historically used intraday
          trailing while Topstep has used end-of-day, but account types vary even within one firm.
          <strong className="text-white"> Confirm which model your specific account uses before you rely on
          any of this</strong> — it changes how you should manage an open winner.
        </p>
      </Section>

      <Section title="The number you should actually watch">
        <p>
          Prop marketing trains you to think in account sizes — a &ldquo;$100,000 account.&rdquo; But the
          money that decides whether you survive is the drawdown allowance, not the headline number.
        </p>
        <p>
          On that $100,000 account with a $3,000 trailing drawdown, a $1,000 stop is not
          &ldquo;1% risk.&rdquo; It is <strong className="text-white">a third of everything standing between
          you and a blown account.</strong> Three of those in a row and you are finished, on an account
          that sounded like it had six figures behind it.
        </p>
        <p>
          So the number worth putting in front of you on every trade is: <em>what percentage of my remaining
          failure buffer am I risking here?</em>
        </p>
        <div className="mt-4 flex flex-wrap gap-3">
          <Link
            href="/cushion"
            className="inline-block rounded-lg border border-[#3fb950] px-5 py-3 text-[15px] font-semibold text-[#3fb950] hover:bg-[#3fb950] hover:text-[#08240f]"
          >
            Work out your cushion →
          </Link>
          <Link
            href="/readiness"
            className="inline-block rounded-lg border border-[#4da3ff] px-5 py-3 text-[15px] font-semibold text-[#4da3ff] hover:bg-[#4da3ff] hover:text-[#04121f]"
          >
            Check your challenge odds →
          </Link>
        </div>
      </Section>

      <Section title="Common questions">
        <div className="space-y-5">
          {FAQ.map((f) => (
            <div key={f.q}>
              <h3 className="text-[16px] font-semibold text-[#e8edf3]">{f.q}</h3>
              <p className="mt-1">{f.a}</p>
            </div>
          ))}
        </div>
      </Section>

      <footer className="mt-14 border-t border-[#2a333f] pt-6 text-sm text-[#7f8b98]">
        Educational information, not financial advice. Prop firm rules change frequently and differ by
        account type — always verify the current rules with your own firm.
      </footer>
    </main>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mt-10">
      <h2 className="text-2xl font-bold tracking-tight">{title}</h2>
      <div className="mt-3 space-y-3 text-[#c7d1db]">{children}</div>
    </section>
  );
}
