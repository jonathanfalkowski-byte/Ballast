import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import WaitlistForm from "@/components/WaitlistForm";

export const metadata: Metadata = {
  title: "The Ballast NinjaTrader add-on — see it before you buy it",
  description:
    "Ballast is a NinjaTrader 8 add-on that watches your prop account's trailing drawdown live, records every trade with a photograph of the chart, and warns you on the chart itself. Not on sale yet — this is what it does.",
  alternates: { canonical: "https://tradeballast.com/addon" },
  openGraph: {
    title: "The Ballast NinjaTrader add-on",
    description:
      "Live trailing-drawdown cushion, a journal that fills itself, and a warning on the chart before you take the trade.",
    url: "https://tradeballast.com/addon",
    type: "website",
  },
};

export default function AddonPage() {
  return (
    <main className="mx-auto max-w-3xl px-6 py-16">
      {/* Hero */}
      <section>
        <p className="mb-3 text-xs font-bold uppercase tracking-[0.14em] text-[#e3b341]">
          Not on sale yet
        </p>
        <h1 className="text-4xl font-extrabold leading-tight tracking-tight">
          It runs inside NinjaTrader. You never type a thing.
        </h1>
        <p className="mt-4 text-[17px] leading-relaxed text-[#9aa7b4]">
          Ballast reads your accounts directly. It knows what your firm&apos;s drawdown rules are,
          how much room you have left on every account right now, and what you were looking at when
          you clicked buy — because it photographs the chart. The only thing it asks you for is the
          one thing software cannot see: whether the trade was your plan.
        </p>
        <div className="mt-7">
          <WaitlistForm />
        </div>
        <p className="mt-3 text-sm text-[#7f8b98]">
          Everything below is a screenshot of the working add-on, not a mock-up.
        </p>
      </section>

      {/* Now */}
      <Shot
        eyebrow="The Now tab"
        title="One number decides whether the account survives"
        src="/shots/now.png"
        alt="The Ballast Now tab, showing an alert to step away after a loss, the tightest account's remaining room, and a per-account list with individual warnings."
        width={838}
        height={715}
      >
        <p>
          Not your balance — the distance between your balance and the level your firm closes the
          account. Ballast tracks that live on every account at once, knows the difference between
          intraday and end-of-day trailing, and knows that most firms freeze the floor once you pass
          a threshold.
        </p>
        <p>
          The headline is driven by whichever account is in the most trouble. Every other account
          still speaks for itself underneath —{" "}
          <em className="text-[#e3b341] not-italic">
            &ldquo;was up $800, handed back $600 — do not trade back your profits&rdquo;
          </em>
          . Six accounts open used to mean five of them were four numbers and a code.
        </p>
      </Shot>

      {/* Journal */}
      <Shot
        eyebrow="The Journal tab"
        title="The journal fills itself in"
        src="/shots/journal.png"
        alt="The Ballast Journal tab, showing today's plan, what the trades show so far, and a list of trades grouped by account."
        width={1080}
        height={616}
      >
        <p>
          Every round trip is recorded the moment you go flat: instrument, direction, size, entry and
          exit times, duration, P&amp;L. Nothing to look up. Nothing to retype. That mechanical
          copying is what kills journals in week two.
        </p>
        <p>
          It also records what no other journal has —{" "}
          <strong className="text-white">what Ballast was advising when you opened the trade</strong>
          , your cushion at that moment, and how many minutes it had been since your last loss. All
          machine-observed, so it works even if you never tag anything. That is how it can eventually
          tell you something like{" "}
          <em className="not-italic text-[#f4523b]">
            &ldquo;11 trades were opened after Ballast said stop, and together they cost
            $2,400.&rdquo;
          </em>
        </p>
      </Shot>

      {/* Setup */}
      <Shot
        eyebrow="Setup"
        title="It already knows your firm's rules"
        src="/shots/setup.png"
        alt="The Ballast Setup tab, showing the account watch list with each account's saved rules, firm and account type selection, and recommended settings."
        width={1270}
        height={1049}
      >
        <p>
          Tick an account and Ballast reads the firm from the account name and the size from the
          balance, then applies that firm&apos;s published drawdown, drawdown type and floor-lock
          level. Apex, Topstep, Take Profit Trader, MyFundedFutures — evaluations and funded accounts
          separately, because they do not behave the same, and legacy sizes separately, because they
          do not either.
        </p>
        <p>
          It refuses to guess. A balance that matches no standard size is left for you to fill in,
          and sim accounts are never auto-configured. The rule book updates itself from this site, so
          when a firm moves a number your cushion follows without you doing anything.
        </p>
      </Shot>

      {/* Chart warning */}
      <section className="mt-16">
        <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#8b97a5]">
          The chart indicator
        </p>
        <h2 className="text-2xl font-bold tracking-tight">
          The warning goes where your eyes already are
        </h2>
        <div className="mt-4 space-y-4 text-[#9aa7b4]">
          <p>
            A panel off to one side is exactly where nobody looks in the second before a revenge
            trade. Ballast ships a companion indicator that paints the same message across the top of
            the chart itself, in large letters, in the same colours.
          </p>
          <p>
            It only ever shows something{" "}
            <strong className="text-white">actionable</strong>. There is no permanent all-clear
            banner, because a message that is always there is one you stop seeing — and then it is
            worth nothing on the day it matters. And it draws nothing at all if the data is stale,
            rather than confidently showing you an hour-old &ldquo;you are fine&rdquo;.
          </p>
        </div>
      </section>

      {/* Charts */}
      <section className="mt-16 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">
          It photographs the chart at entry and at exit
        </h2>
        <div className="mt-4 space-y-4 text-[#9aa7b4]">
          <p>
            Everything else in a journal is a number, and numbers are easy to argue with afterwards.
            &ldquo;The setup was there.&rdquo; &ldquo;It looked different at the time.&rdquo; Memory
            of your own reasoning is not retrieved, it is rebuilt — and it gets rebuilt to fit the
            outcome.
          </p>
          <p>
            A picture of what was actually on your screen ends that argument. It is the one field
            hindsight cannot rewrite, and it costs you nothing: no screenshot key, no cropping, no
            filing. Both images sit in the journal, and any trade opens as a full page with every
            figure Ballast recorded underneath.
          </p>
        </div>
      </section>

      {/* Honesty */}
      <section className="mt-16">
        <h2 className="text-2xl font-bold tracking-tight">What it deliberately does not do</h2>
        <div className="mt-6 space-y-4">
          <Point title="It never touches your orders.">
            Ballast does not place, modify or cancel an order, and never flattens a position. It is
            advisory. Software that closes positions costs real money when it is wrong, and this has
            not earned that trust yet.
          </Point>
          <Point title="It does not tell you what to trade.">
            No signals, no setups, no entries. It watches how you behave around the trades you
            already take.
          </Point>
          <Point title="It does not guess at your numbers.">
            Where a figure is not verified against the firm&apos;s own page, Ballast assumes the
            conservative version — reporting less room than you may really have. Being cautiously
            wrong is survivable. The opposite is how accounts die.
          </Point>
        </div>
      </section>

      {/* CTA */}
      <section className="mt-16 rounded-2xl border border-[#2a333f] bg-gradient-to-br from-[#132a1f] to-[#12233b] p-9">
        <h2 className="text-2xl font-bold tracking-tight">Not for sale yet</h2>
        <p className="mt-3 text-[#9aa7b4]">
          It works, it runs live, and it is being traded on every day. It is not finished. Leave your
          email and you will hear when it is — and nothing else in the meantime.
        </p>
        <div className="mt-6">
          <WaitlistForm />
        </div>
        <p className="mt-6 text-sm text-[#7f8b98]">
          In the meantime the{" "}
          <Link href="/cushion" className="text-[#4da3ff] underline underline-offset-4">
            trailing-drawdown cushion calculator
          </Link>{" "}
          is free and needs no signup.
        </p>
      </section>
    </main>
  );
}

function Shot({
  eyebrow,
  title,
  src,
  alt,
  width,
  height,
  children,
}: {
  eyebrow: string;
  title: string;
  src: string;
  alt: string;
  width: number;
  height: number;
  children: React.ReactNode;
}) {
  return (
    <section className="mt-16">
      <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#8b97a5]">{eyebrow}</p>
      <h2 className="text-2xl font-bold tracking-tight">{title}</h2>
      <div className="mt-5 overflow-hidden rounded-xl border border-[#2a333f] bg-[#0e1116]">
        <Image
          src={src}
          alt={alt}
          width={width}
          height={height}
          className="h-auto w-full"
          unoptimized
        />
      </div>
      <div className="mt-5 space-y-4 text-[#9aa7b4]">{children}</div>
    </section>
  );
}

function Point({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
      <p className="font-semibold text-white">{title}</p>
      <p className="mt-2 text-[15px] text-[#9aa7b4]">{children}</p>
    </div>
  );
}
