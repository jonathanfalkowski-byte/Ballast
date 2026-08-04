import type { Metadata } from "next";
import Link from "next/link";
import { RULES_TEXT, RULES_VERSION, RULES_VERIFIED, RULE_SOURCES } from "@/lib/propFirmRules";

export const metadata: Metadata = {
  title: "Prop firm drawdown rules — the actual numbers",
  description:
    "Every prop firm account type, with its real trailing drawdown, whether it trails intraday or end-of-day, the balance where the threshold stops following you, and the firm's own contract cap. Evaluation and funded listed separately, because they do not behave the same. No affiliate links.",
  alternates: { canonical: "https://tradeballast.com/rules" },
  openGraph: {
    title: "Prop firm drawdown rules — the actual numbers",
    description:
      "The trailing drawdown, drawdown type, floor-lock level and contract cap for every account type. Evaluation and funded separately. No affiliate links.",
    url: "https://tradeballast.com/rules",
    type: "website",
  },
};

type Rule = {
  firm: string;
  plan: string;
  size: number;
  drawdown: number;
  type: string;
  dailyLoss: number;
  target: number;
  note: string;
  lockAt: number;
  maxContracts: number;
};

/**
 * Parsed from the very same text the add-on downloads.
 *
 * That is the point of this page. There is no second copy of these numbers
 * maintained for marketing purposes — if a figure here is wrong, the software
 * is wrong in exactly the same way, which is the only arrangement under which a
 * reference like this stays honest.
 */
function parseRules(): Rule[] {
  const out: Rule[] = [];

  for (const raw of RULES_TEXT.split("\n")) {
    const line = raw.trim();
    if (!line || line.startsWith("#")) continue;
    if (line.startsWith("VERSION|") || line.startsWith("VERIFIED|")) continue;

    const f = line.split("|");
    if (f.length < 8) continue;

    const num = (v: string | undefined) => {
      const n = Number.parseFloat(v ?? "");
      return Number.isFinite(n) ? n : 0;
    };

    out.push({
      firm: f[0],
      plan: f[1],
      size: num(f[2]),
      drawdown: num(f[3]),
      type: (f[4] || "").toUpperCase() === "EOD" ? "End-of-day" : "Intraday",
      dailyLoss: num(f[5]),
      target: num(f[6]),
      note: f[7] ?? "",
      lockAt: num(f[8]),
      maxContracts: Math.round(num(f[9])),
    });
  }

  return out;
}

const money = (n: number) =>
  n === 0 ? "—" : "$" + Math.round(n).toLocaleString("en-US");

const size = (n: number) => (n >= 1000 ? Math.round(n / 1000) + "K" : String(n));

export default function RulesPage() {
  const rules = parseRules();

  const firms: string[] = [];
  for (const r of rules) if (!firms.includes(r.firm)) firms.push(r.firm);

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "Dataset",
    name: "Prop firm drawdown rules",
    description:
      "Trailing drawdown, drawdown type, floor-lock level, profit target and contract cap for prop trading firm account types, split by evaluation and funded.",
    url: "https://tradeballast.com/rules",
    dateModified: RULES_VERIFIED,
    creator: { "@type": "Organization", name: "Ballast" },
    distribution: [
      {
        "@type": "DataDownload",
        encodingFormat: "text/plain",
        contentUrl: "https://tradeballast.com/api/rules",
      },
    ],
  };

  return (
    <main className="mx-auto max-w-5xl px-6 py-16">
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
      />

      <section>
        <p className="mb-3 text-xs font-bold uppercase tracking-[0.14em] text-[#e3b341]">
          Free reference · no signup
        </p>
        <h1 className="text-4xl font-extrabold leading-tight tracking-tight">
          Prop firm drawdown rules, with the actual numbers
        </h1>
        <p className="mt-4 text-[17px] leading-relaxed text-[#9aa7b4]">
          Every comparison site will tell you a firm&apos;s star rating and its discount code. Very
          few will tell you what its trailing drawdown actually is, whether it follows you intraday
          or only at the close, or the balance at which it stops following you at all. Those three
          numbers decide whether your account survives. This is them.
        </p>
        <p className="mt-4 text-[15px] leading-relaxed text-[#9aa7b4]">
          Evaluation and funded accounts are listed separately, because they do not behave the same.
          Legacy account generations are listed separately, because they do not either.
        </p>

        <div className="mt-7 rounded-xl border border-[#2a333f] bg-[#161b22] p-5 text-sm text-[#9aa7b4]">
          <p>
            <strong className="text-white">We take no money from any firm on this page.</strong> No
            affiliate links, no referral codes, no paid placement, no rankings. Ballast is paid for
            by software, so this table has nothing to sell you and no reason to flatter anyone.
          </p>
          <p className="mt-3">
            Version {RULES_VERSION}, last checked against the firms&apos; own pages on{" "}
            {RULES_VERIFIED}. The same file the{" "}
            <Link href="/addon" className="text-[#4da3ff] underline underline-offset-4">
              Ballast add-on
            </Link>{" "}
            reads is served at{" "}
            <a
              href="/api/rules"
              className="text-[#4da3ff] underline underline-offset-4"
              rel="nofollow"
            >
              /api/rules
            </a>{" "}
            — plain text, free, no key. Use it for whatever you like.
          </p>
        </div>
      </section>

      {/* The finding worth leading with */}
      <section className="mt-14 rounded-2xl border border-[#f4523b] bg-[#1a0f0e] p-7">
        <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-[#f4523b]">
          The one nobody publishes
        </p>
        <h2 className="text-2xl font-bold tracking-tight">
          On Apex, your data feed changes your drawdown
        </h2>
        <div className="mt-4 space-y-4 text-[#c9b6b4]">
          <p>
            Apex&apos;s intraday trailing threshold has{" "}
            <strong className="text-white">three different behaviours</strong>, and which one
            applies to you depends on how you connect. From their own help centre:
          </p>
          <ul className="ml-5 list-disc space-y-2">
            <li>
              <strong className="text-white">Performance (funded) accounts:</strong>{" "}
              &ldquo;Once the
              Intraday Threshold reaches Starting Balance + $100, it stops increasing.&rdquo; Note
              what that means in balance terms: the threshold is your peak less the drawdown, so it
              only gets there once your{" "}
              <strong className="text-white">highest balance</strong> reaches Starting Balance + Max
              Drawdown + $100. Apex&apos;s own example is a 50K performance account with a $2,000
              drawdown: the threshold fixes at $50,100, reached when the balance touches $52,100 —
              not at $50,100. On a 100K performance account with a $3,000 drawdown it is $103,100
              you have to reach before the floor stops following you.
            </li>
            <li>
              <strong className="text-white">Evaluations on Rithmic and WealthCharts:</strong>{" "}
              the
              threshold stops trailing and becomes fixed at the Target Profit balance. Apex&apos;s
              own worked example, on a 50K with a $3,000 target: &ldquo;Profit Target Balance =
              $53,000. Threshold locks when balance reaches = $55,000. Final Threshold Stop Level =
              $53,000.&rdquo; Read the prose on that page carefully — it says trailing stops once
              the threshold reaches &ldquo;Profit Target Balance + $2,000&rdquo;, but its own
              example shows $2,000 is the drawdown, so it is the BALANCE that has to reach $55,000
              and the threshold rests at $53,000. This applies to end-of-day evaluations on Rithmic
              too, which do lock.
            </li>
            <li>
              <strong className="text-white">Evaluations on Tradovate:</strong>{" "}
              the drawdown
              &ldquo;trails indefinitely with the peak account balance&rdquo; and never stops.
              Intraday and end-of-day alike.
            </li>
          </ul>
          <p>
            Same firm, same account size, same evaluation — a materially different floor. On a 250K
            legacy evaluation over Rithmic the threshold fixes at $265,000 once your peak reaches
            $271,500, and every dollar above that is real cushion. On Tradovate it never fixes, and
            you have $6,500 of room at any balance, forever.
          </p>
          <p className="text-[#9aa7b4]">
            NinjaTrader normally reaches Apex over Rithmic. If you have been assuming your threshold
            keeps trailing, it probably stops — and if you have been assuming it stops, on Tradovate
            it does not.
          </p>
          <p className="text-[#9aa7b4]">
            The platform split is an <strong className="text-white">evaluation</strong> phenomenon.
            Apex states the funded-account rule once, with no platform attached, in both its
            intraday and end-of-day performance-account articles, and its legacy documentation scopes
            the Tradovate never-stops behaviour explicitly to evaluations. So on a funded account the
            threshold stops at Starting Balance + $100 whichever feed you use. Apex does not say this
            in so many words, so we are reading it off the structure of their own rules rather than
            quoting it — check your dashboard.
          </p>
        </div>
      </section>

      {/* The table */}
      {firms.map((firm) => (
        <section key={firm} className="mt-14">
          <h2 className="text-2xl font-bold tracking-tight">{firm}</h2>

          <div className="mt-5 overflow-x-auto rounded-xl border border-[#2a333f]">
            <table className="w-full border-collapse text-left text-sm">
              <thead className="bg-[#11151b] text-xs uppercase tracking-wide text-[#8b97a5]">
                <tr>
                  <th className="px-4 py-3 font-semibold">Account type</th>
                  <th className="px-4 py-3 font-semibold">Size</th>
                  <th className="px-4 py-3 font-semibold">Drawdown</th>
                  <th className="px-4 py-3 font-semibold">Trails</th>
                  <th className="px-4 py-3 font-semibold">Stops trailing at</th>
                  <th className="px-4 py-3 font-semibold">To pass</th>
                  <th className="px-4 py-3 font-semibold">Firm cap</th>
                </tr>
              </thead>
              <tbody>
                {rules
                  .filter((r) => r.firm === firm)
                  .map((r, i) => (
                    <tr
                      key={firm + r.plan + r.size + i}
                      className="border-t border-[#20272f] align-top"
                    >
                      <td className="px-4 py-3">
                        <span className="text-white">{r.plan}</span>
                        {r.note ? (
                          <span className="mt-1 block text-xs text-[#6f7a87]">{r.note}</span>
                        ) : null}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-[#9aa7b4]">{size(r.size)}</td>
                      <td className="whitespace-nowrap px-4 py-3 font-semibold text-white">
                        {money(r.drawdown)}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-[#9aa7b4]">{r.type}</td>
                      <td className="whitespace-nowrap px-4 py-3 text-[#9aa7b4]">
                        {r.lockAt > 0 ? (
                          money(r.lockAt)
                        ) : (
                          <span className="text-[#e3b341]">never</span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-[#9aa7b4]">
                        {money(r.target)}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-[#9aa7b4]">
                        {r.maxContracts > 0 ? r.maxContracts : "—"}
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}

      {/* How to read it */}
      <section className="mt-16">
        <h2 className="text-2xl font-bold tracking-tight">How to read this</h2>
        <div className="mt-5 space-y-5 text-[#9aa7b4]">
          <p>
            <strong className="text-white">Drawdown is your real account size.</strong> A 100K
            account carrying a $3,000 trailing drawdown is a $3,000 account with a large number
            printed on it. Risking &ldquo;1% of 100K&rdquo; is a third of everything keeping it
            alive. Size your positions against the drawdown, never against the balance —{" "}
            <Link
              href="/cushion"
              className="text-[#4da3ff] underline underline-offset-4"
            >
              the cushion calculator
            </Link>{" "}
            does that arithmetic for you.
          </p>
          <p>
            <strong className="text-white">Intraday trailing includes unrealised profit.</strong>{" "}
            Your floor follows your highest equity during the session, not your closing balance. A
            winner that goes $2,000 green and round-trips back to flat has moved your floor up
            $2,000 permanently and you have nothing to show for it. End-of-day trailing only
            recalculates at the close, which is materially more forgiving.{" "}
            <Link
              href="/trailing-drawdown"
              className="text-[#4da3ff] underline underline-offset-4"
            >
              The long explanation is here.
            </Link>
          </p>
          <p>
            <strong className="text-white">&ldquo;Stops trailing at&rdquo; is the number people
            miss.</strong> Until your floor reaches that balance, profit buys you nothing — the
            floor follows you up and your room stays exactly the same. After it, every dollar is
            genuine cushion. Where this column says <em className="not-italic text-[#e3b341]">never</em>,
            the floor follows you forever and your room never grows, however well you trade.
          </p>
          <p>
            <strong className="text-white">Where we are not certain, we assume the tighter
            rule.</strong> A figure we could not verify against a firm&apos;s own page is recorded
            as the conservative version, which reports less room than you may really have. Being
            cautiously wrong is survivable. The opposite is how accounts die.
          </p>
        </div>
      </section>

      {/* Sources and honesty */}
      <section className="mt-16 rounded-2xl border border-[#2a333f] bg-[#161b22] p-8">
        <h2 className="text-2xl font-bold tracking-tight">Where these came from</h2>
        <p className="mt-4 text-[#9aa7b4]">
          Every figure is taken from the firm&apos;s own published rules, not from another
          comparison site. Firms change these often and not always loudly, so check yours before you
          trust a number here — including ours. If you find one that is wrong, tell us and it gets
          corrected for everyone on the next deploy, including inside the add-on.
        </p>
        <ul className="mt-5 space-y-2 text-sm">
          {RULE_SOURCES.map((s) => (
            <li key={s.url}>
              <span className="text-[#6f7a87]">{s.firm}</span>{" "}
              <a
                href={s.url}
                target="_blank"
                rel="noopener noreferrer nofollow"
                className="text-[#4da3ff] underline underline-offset-4"
              >
                {s.url.replace(/^https?:\/\//, "")}
              </a>
            </li>
          ))}
        </ul>
      </section>

      <section className="mt-16 rounded-2xl border border-[#2a333f] bg-gradient-to-br from-[#132a1f] to-[#12233b] p-9">
        <h2 className="text-2xl font-bold tracking-tight">
          These numbers are also a piece of software
        </h2>
        <p className="mt-3 text-[#9aa7b4]">
          Ballast is a NinjaTrader add-on that applies this table to your live accounts — working
          out how much room each one has left right now, reading your connection to know which Apex
          rule applies to you, and telling you before you take the trade rather than after.
        </p>
        <p className="mt-5">
          <Link
            href="/addon"
            className="text-[#4da3ff] underline underline-offset-4"
          >
            See what it does →
          </Link>
        </p>
      </section>
    </main>
  );
}
