import Link from "next/link";
import WaitlistForm from "@/components/WaitlistForm";

export default function Home() {
  return (
    <main className="mx-auto max-w-3xl px-6 py-16">
      {/* Hero */}
      <section className="rounded-2xl border border-[#2a333f] bg-gradient-to-br from-[#132a1f] to-[#12233b] p-9">
        <p className="mb-3 text-xs font-bold uppercase tracking-[0.14em] text-[#3fb950]">
          For prop futures traders
        </p>
        <h1 className="text-4xl font-extrabold leading-tight tracking-tight">
          Stop giving it all back.
        </h1>
        <p className="mt-4 max-w-xl text-[17px] text-[#9aa7b4]">
          Every journal tells you what you did after you blew up. Ballast is the layer that
          tells you to <strong className="text-white">stop before you do</strong> — it watches your
          trailing-drawdown cushion, catches the revenge trade in the moment, and grades the one
          thing that actually matters: whether you followed your own rules.
        </p>
        <div className="mt-7">
          <WaitlistForm />
        </div>
        <p className="mt-5 text-[15px]">
          <Link
            href="/addon"
            className="font-semibold text-[#4da3ff] underline underline-offset-4"
          >
            See the NinjaTrader add-on &rarr;
          </Link>
          <span className="ml-2 text-[#7f8b98]">See every screen. Not on sale yet.</span>
        </p>
        <p className="mt-3 text-[15px]">
          <Link
            href="/rules"
            className="font-semibold text-[#4da3ff] underline underline-offset-4"
          >
            Every prop firm&apos;s drawdown rules &rarr;
          </Link>
          <span className="ml-2 text-[#7f8b98]">
            The actual numbers. Free, no signup, no affiliate links.
          </span>
        </p>
        <p className="mt-4 text-sm text-[#7f8b98]">
          Built by a trader who spent ten years learning this the hard way.
        </p>
      </section>

      {/* The wedge */}
      <section className="mt-14">
        <h2 className="text-2xl font-bold tracking-tight">Three things every journal gets wrong</h2>
        <div className="mt-6 space-y-4">
          <Gap
            n="1"
            title="Trailing-drawdown cushion, live"
            body="Prop accounts live or die by how much room you had to the floor when you took the trade. Ballast puts that number front and center — and knows the difference between intraday and end-of-day trailing."
          />
          <Gap
            n="2"
            title="Revenge caught in the moment"
            body="Tag a trade revenge, FOMO, or boredom, and see your real edge by tag. Then Ballast warns you inside the tilt window — the five minutes after a loss where accounts die."
          />
          <Gap
            n="3"
            title="Graded on rules, not P&L"
            body="A green day where you broke your rules is a failing day. Ballast scores adherence and tracks your clean-day streak, because the streak is what compounds."
          />
        </div>
      </section>

      {/* Free tool */}
      <section className="mt-14 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">Free: the trailing-drawdown cushion calculator</h2>
        <p className="mt-3 text-[#9aa7b4]">
          No signup. See exactly how much room you have to your floor and the right position size for
          your risk — the single most important number in prop trading.
        </p>
        <div className="mt-5 flex flex-wrap gap-3">
          <Link
            href="/cushion"
            className="inline-block rounded-lg border border-[#3fb950] px-5 py-3 text-[15px] font-semibold text-[#3fb950] hover:bg-[#3fb950] hover:text-[#08240f]"
          >
            Open the free calculator →
          </Link>
          <Link
            href="/session"
            className="inline-block rounded-lg border border-[#4da3ff] px-5 py-3 text-[15px] font-semibold text-[#4da3ff] hover:bg-[#4da3ff] hover:text-[#04121f]"
          >
            See the engine live →
          </Link>
        </div>
      </section>

      {/* Challenge readiness */}
      <section className="mt-6 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">Free: should you even buy that evaluation?</h2>
        <p className="mt-3 text-[#9aa7b4]">
          Paste your recent trades and Ballast runs thousands of rule-aware simulations from your own numbers to estimate
          your odds of reaching a payout before you breach. If the edge isn&apos;t there yet, it says so — and saves you the fee.
          The most honest tool in prop trading.
        </p>
        <Link
          href="/readiness"
          className="mt-5 inline-block rounded-lg border border-[#3fb950] px-5 py-3 text-[15px] font-semibold text-[#3fb950] hover:bg-[#3fb950] hover:text-[#08240f]"
        >
          Check your challenge readiness →
        </Link>
      </section>

      {/* Behavioural insights */}
      <section className="mt-6 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">Free: what your revenge trades cost you</h2>
        <p className="mt-3 text-[#9aa7b4]">
          Your overall win rate is an average, and averages hide what&apos;s actually killing the account.
          Tag your trades by what drove them and the damage separates out — the planned trades that make
          money, and the emotional ones that hand it back.
        </p>
        <Link
          href="/insights"
          className="mt-5 inline-block rounded-lg border border-[#f4523b] px-5 py-3 text-[15px] font-semibold text-[#f4523b] hover:bg-[#f4523b] hover:text-[#1a0805]"
        >
          See the damage →
        </Link>
      </section>

      {/* Explainer — also the SEO entry point for people searching the mechanics */}
      <section className="mt-6 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">New to trailing drawdowns?</h2>
        <p className="mt-3 text-[#9aa7b4]">
          The rule that ends most prop accounts, explained plainly — what intraday vs end-of-day trailing
          actually means, why a floating winner that round-trips can still cost you the account, and the
          number you should be watching instead of your account size.
        </p>
        <Link
          href="/trailing-drawdown"
          className="mt-5 inline-block rounded-lg border border-[#9aa7b4] px-5 py-3 text-[15px] font-semibold text-[#c7d1db] hover:bg-[#c7d1db] hover:text-[#0e1116]"
        >
          Read the explainer →
        </Link>
      </section>

      <footer className="mt-16 border-t border-[#2a333f] pt-6 text-sm text-[#7f8b98]">
        Ballast · early access · not financial advice.
      </footer>
    </main>
  );
}

function Gap({ n, title, body }: { n: string; title: string; body: string }) {
  return (
    <div className="flex gap-4 rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
      <div className="flex h-9 w-9 flex-none items-center justify-center rounded-lg bg-[#1b2f24] font-extrabold text-[#3fb950]">
        {n}
      </div>
      <div>
        <b className="block">{title}</b>
        <span className="text-[15px] text-[#9aa7b4]">{body}</span>
      </div>
    </div>
  );
}
