import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import WaitlistForm from "@/components/WaitlistForm";

export const metadata: Metadata = {
  title: "The Ballast NinjaTrader add-on — see it before you buy it",
  description:
    "Ballast is a NinjaTrader 8 add-on that watches your prop account's trailing drawdown live, records every trade with a photograph of the chart, warns you on the chart itself, and puts a wall on the screen when you start trading to get even. Not on sale yet — this is what it does.",
  alternates: { canonical: "https://tradeballast.com/addon" },
  openGraph: {
    title: "The Ballast NinjaTrader add-on",
    description:
      "Live trailing-drawdown cushion, a journal that fills itself, a warning on the chart before you take the trade, and a wall in front of the revenge trade.",
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
          Every image below is captioned. They are illustrations rendered from the add-on&apos;s own
          source, so the layout, the colours and every word in them are the ones it really puts on
          screen. Nothing here is a concept. Setup has been rebuilt since the last photograph was
          taken, so rather than show you the old one there is no image of it below &mdash; a current
          one goes up when it is taken.
        </p>
      </section>

      {/* Now */}
      <Shot
        eyebrow="The Now tab"
        title="One number decides whether the account survives"
        src="/shots/now.png"
        alt="The Ballast Now tab, showing an alert to step away after a loss, the tightest account's remaining room, a trade waiting to be tagged with four verdict buttons, and a per-account list with individual warnings."
        width={1800}
        height={1500}
        kind="illustration"
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


      {/* The wall */}
      <section className="mt-16">
        <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#f4523b]">
          When you are tilting
        </p>
        <h2 className="text-2xl font-bold tracking-tight">
          It takes the screen away. It never touches your orders.
        </h2>
        <Frame
          src="/shots/tilt.png"
          alt="The Ballast lockout: a full-window red screen reading 'You are done for the day', the account and the exact amount it is down, the trader's own record of previous overrides, a large 'I'm done for the day' button, and below it a text box where the release sentence must be typed out."
          width={1580}
          height={1456}
          kind="illustration"
        />
        <div className="mt-5 space-y-4 text-[#9aa7b4]">
          <p>
            Every trader knows the feeling. You are down, and the next trade stops being a trade
            and becomes an attempt to get the money back. Nothing on a dashboard has ever stopped
            that, because in that state you are not reading dashboards.
          </p>
          <p>
            So when an account goes past its floor, past your daily loss limit, or past the number
            of losses you set as your line, Ballast covers its own window &mdash; every tab, all of
            it &mdash; and says the thing in your own numbers.{" "}
            <em className="not-italic text-white">
              &ldquo;Nothing from here is a setup. It is a bet to get even, and it is being placed
              by the part of you that just lost $1,240.&rdquo;
            </em>
          </p>
          <p>
            <strong className="text-white">
              &ldquo;I&apos;m done for the day&rdquo; is one click.
            </strong>{" "}
            Carrying on means typing out{" "}
            <em className="not-italic text-[#e3b341]">
              &ldquo;I am trading outside my plan and I accept I may lose this account.&rdquo;
            </em>{" "}
            That is about ten seconds, and you cannot paste it. Ten seconds is roughly how long it
            takes for an impulse to stop being automatic and start being a decision &mdash; which is
            the entire mechanism. The right choice is easy on purpose and the wrong one is slow on
            purpose.
          </p>
          <p>
            Then it is written down. Every override is logged with your P&amp;L at that moment and
            what the rest of the day actually did, so the next time the wall appears it stops
            arguing and shows you your own record:{" "}
            <em className="not-italic text-[#f4523b]">
              &ldquo;You went past this 4 times in the last 30 days. 3 of those sessions went on to
              lose a further $2,300; 1 recovered $500.&rdquo;
            </em>{" "}
            It counts the times carrying on worked out, because a record that only ever reported
            losses would be worthless the first time you checked it. It counts the times you
            stopped, too.
          </p>
        </div>
      </section>

      {/* Journal */}
      <Shot
        eyebrow="The Journal tab"
        title="The journal fills itself in"
        src="/shots/journal.png"
        alt="The Ballast Journal tab, showing today's plan, what the trades show so far including the cost of overriding the lockout, and trades grouped under collapsible account headings."
        width={2000}
        height={1284}
        kind="illustration"
      >
        <p>
          Every round trip is recorded the moment you go flat: instrument, direction, size, entry and
          exit times, duration, P&amp;L. Nothing to look up. Nothing to retype. That mechanical
          copying is what kills journals in week two.
        </p>
        <p>
          You name your own setups once — call them A, B and C, or name them properly — and every
          trade in the journal carries a picker for which one you took. That is what turns a pile of
          trades into an answer about your entries rather than a diary.
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

      {/* The one thing it has to ask */}
      <section className="mt-16 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">
          It asks one question a machine cannot answer
        </h2>
        <div className="mt-4 space-y-4 text-[#9aa7b4]">
          <p>
            After every trade: <strong className="text-white">did you move your stop or your
            target?</strong> One tap — held both, moved my stop, moved my target, moved both — and
            if you moved something, the note box changes to ask why, while you still remember.
          </p>
          <p>
            Ballast watches your position, not your working orders, so this is genuinely invisible
            to it. It is also the break that costs the most: a stop moved away turns a planned loss
            into an unplanned one, and a target pulled in turns a winner into a scratch. Answering
            it turns the most expensive habit in trading into something with a number attached —{" "}
            <em className="not-italic text-[#f4523b]">
              &ldquo;the 4 trades where you moved your stop or target cost you $1,200; the 6 you
              left alone made $1,080.&rdquo;
            </em>
          </p>
          <p>
            Trades you do not answer count as neither. A journal that assumed you held would
            flatter you, and that is the one thing it must never do.
          </p>
        </div>
      </section>

      {/* Setup */}
      <section className="mt-16">
        <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#8b97a5]">Setup</p>
        <h2 className="text-2xl font-bold tracking-tight">
          Every account gets its own rules, on its own page
        </h2>
        <div className="mt-5 space-y-4 text-[#9aa7b4]">
          <p>
            Tick an account and Ballast reads the firm from the account name and the size from the
            balance, then applies that firm&apos;s published drawdown, drawdown type and floor-lock
            level. Apex, Topstep, Take Profit Trader, MyFundedFutures — evaluations and funded
            accounts separately, because they do not behave the same, and legacy sizes separately,
            because they do not either.
          </p>
          <p>
            Setup is two pages now, not one. A list of your accounts, and behind each one{" "}
            <strong className="text-white">its own page of rules</strong> — daily stop, trade count,
            losses in a row, target, size, trading window. Click &ldquo;set its rules&rdquo; and that
            account is in front of you, named at the top, with every field on the page belonging to
            it and nothing else. A trader running six accounts across two firms does not have one
            set of limits, and the old single page pretended he did.
          </p>
          <p>
            One thing came out rather than in. There used to be a button that matched every account
            at once by balance, and a setting above it asking which generation of account you hold.
            Both are gone.{" "}
            <em className="not-italic text-[#e3b341]">
              A single answer to &ldquo;which generation do you hold&rdquo; cannot be true of a
              person, only of one account at a time
            </em>{" "}
            — a legacy 50K trails $2,500 against a 4.0 50K&apos;s $2,000, a balance cannot tell them
            apart, and you can hold both at once alongside accounts at other firms entirely. Each
            account states its generation when you pick its type, and there is a per-account override
            if a label ever lies.
          </p>
          <p>
            Each account also says what it is <em className="not-italic text-white">for</em> —
            practice, evaluation, or funded. That is not the same question as whether the platform
            calls it a simulator: plenty of traders run a sim deliberately as though it were funded,
            to test a strategy under something like real conditions. Only you know which, and it
            decides what the comparison further down this page is actually measuring.
          </p>
          <p>
            It refuses to guess. A balance that matches no standard size is left for you to fill in,
            and an account whose purpose you have not stated is left out of the analysis rather than
            guessed at. The rule book updates itself from this site, so when a firm moves a number
            your cushion follows without you doing anything.
          </p>
        </div>
      </section>

      {/* What the journal gives back */}
      <section className="mt-16">
        <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#8b97a5]">
          What the journal gives back
        </p>
        <h2 className="text-2xl font-bold tracking-tight">
          A journal nobody reads is just tagging
        </h2>
        <div className="mt-5 space-y-4 text-[#9aa7b4]">
          <p>
            This is the failure mode every trading journal dies of, and admitting it is the only way
            past it. You answer a question on every trade for a week, and you never open the page
            where the answers add up. The tagging is a cost you pay daily for a benefit you never
            collect.
          </p>
          <p>
            So Ballast does not wait to be opened. When your trading day ends — your window closes,
            or you press{" "}
            <strong className="text-white">&ldquo;I&apos;m done for the day&rdquo;</strong> — it puts
            the day&apos;s strongest finding in front of you, and it does the same before your first
            trade of the next session, when it can still change a decision. Not{" "}
            <em className="not-italic">&ldquo;would you like to review your journal&rdquo;</em>,
            which is a question with an easy no at the end of a losing day. The finding itself:{" "}
            <em className="not-italic text-[#e3b341]">
              &ldquo;The 3 you took off your plan cost $650. The 4 you took by the book made $500.
              That is the whole day.&rdquo;
            </em>
          </p>
          <p>
            It picks what to say by how <strong className="text-white">actionable</strong> it is
            rather than how big the number is. Execution first, because &ldquo;stop chasing&rdquo; is
            an instruction you can follow tomorrow morning. Then which setup is carrying which. Then
            the feeling that only ever loses. And if none of those can be said honestly, it reports
            the day and no more — a sentence that overclaims on a three-trade sample is how a journal
            starts lying to the one trader who finally began reading it.
          </p>
        </div>

        <div className="mt-8 grid gap-4 sm:grid-cols-2">
          <Card title="Do your entries work?">
            Your own setups, worst money first, net of commission, over today, this week, this month
            or the year. This is the question you cannot answer from memory: the setup that{" "}
            <em className="not-italic">feels</em> like it works is the one whose wins are memorable,
            and memorability has nothing to do with money. Where the sample is too thin to mean
            anything it says so instead of pretending.
          </Card>
          <Card title="What changes when the money is real">
            The only controlled experiment you ever run on yourself — same person, same setups, same
            market, same hours, one variable moved. Ballast compares how you trade a practice account
            against how you trade a funded one: rule-breaking, how long you hold winners against
            losers, how fast you re-enter after a loss, trades a day, size.
          </Card>
          <Card title="Behaviour, never money">
            A simulator&apos;s fills flatter you — no slippage, no queue, limits that fill when they
            would not have. So a P&amp;L gap between practice and funded is part psychology and part
            generosity, and nobody can separate the two. No fill engine decides whether you chased,
            or whether you held a winner to target. That is the person, and it is the only honest
            ground for the comparison.
          </Card>
          <Card title="It changes the tool, not you">
            Ballast is not qualified to tell you about your own head and does not try. What it does
            is point at a number in your own settings that would have changed the outcome:{" "}
            <em className="not-italic text-[#e3b341]">
              &ldquo;Across 10 days your first 3 trades made $4,500. Everything after that gave back
              $5,500. Your limit is 12.&rdquo;
            </em>{" "}
            One button applies it. Nothing changes unless you press it.
          </Card>
        </div>
      </section>

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
            trade. Ballast ships a companion indicator that puts the same message on the chart
            itself — in a strip of its own, beneath the price panel, the way volume sits.
          </p>
          <p>
            The strip matters more than it sounds. A real trading chart is the most contested space
            on the screen: your platform lists every indicator&apos;s name across the top, the
            instrument watermark sits in a corner, and a chart running eight studies has a wall of
            coloured text before Ballast draws a character. Making the warning bigger or redder does
            not win that fight, it just adds to the pile. So it stops competing.{" "}
            <strong className="text-white">Nothing else can draw there</strong>, so it can never be
            buried, and you always know exactly where to look.
          </p>
          <p>
            On a quiet day it is a quiet line — trades taken against your limit, losses in a row,
            what is left of today&apos;s budget, the room to your floor. When an account goes past a
            hard line the strip fills red. It reads whichever account the chart&apos;s own order
            entry is set to, so switching a chart between accounts moves Ballast with it and there
            is nothing to configure. And it draws nothing at all if the data is stale, rather than
            confidently showing you an hour-old &ldquo;you are fine&rdquo;.
          </p>
          <p>
            A hard breaker is the one thing it will not stay quiet about. Turning the lockout off,
            or typing past it, buys you silence inside Ballast &mdash; it does not buy a
            clean-looking chart.
          </p>
        </div>
        <Frame
          src="/shots/chart.png"
          alt="A chart with the Ballast indicator painting STOP - YOU ARE DONE FOR THE DAY in large red letters across the top."
          width={2400}
          height={780}
          kind="illustration"
        />
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
            Ballast does not place, modify or cancel an order, and never flattens a position
            &mdash; not even behind the lockout. The wall covers Ballast&apos;s own window; your
            platform is one Alt-Tab away and always will be. Software that closes positions costs
            real money when it is wrong, and this has not earned that trust yet.
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
  kind,
  children,
}: {
  eyebrow: string;
  title: string;
  src: string;
  alt: string;
  width: number;
  height: number;
  kind: "photo" | "illustration";
  children: React.ReactNode;
}) {
  return (
    <section className="mt-16">
      <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#8b97a5]">{eyebrow}</p>
      <h2 className="text-2xl font-bold tracking-tight">{title}</h2>
      <Frame src={src} alt={alt} width={width} height={height} kind={kind} />
      <div className="mt-5 space-y-4 text-[#9aa7b4]">{children}</div>
    </section>
  );
}

function Frame({
  src,
  alt,
  width,
  height,
  kind,
}: {
  src: string;
  alt: string;
  width: number;
  height: number;
  kind: "photo" | "illustration";
}) {
  return (
    <figure className="mt-5">
      <div className="overflow-hidden rounded-xl border border-[#2a333f] bg-[#0e1116]">
        <Image
          src={src}
          alt={alt}
          width={width}
          height={height}
          className="h-auto w-full"
          unoptimized
        />
      </div>
      <figcaption className="mt-2 text-xs text-[#6f7a87]">
        {kind === "photo"
          ? "Screenshot of the running add-on."
          : "Illustration \u2014 rendered from the add-on's source, not a photograph. Same layout, same wording."}
      </figcaption>
    </figure>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-[#2a333f] bg-[#161b22] p-5">
      <p className="font-semibold text-white">{title}</p>
      <p className="mt-2 text-[15px] leading-relaxed text-[#9aa7b4]">{children}</p>
    </div>
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
