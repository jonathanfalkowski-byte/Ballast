import { NextResponse } from "next/server";
import { RULES_TEXT, RULES_VERSION, RULES_VERIFIED } from "@/lib/propFirmRules";

// Served to the Ballast NinjaTrader add-on so traders never maintain rules by hand.
// Plain text on purpose: the add-on parses it in C# with no JSON dependency.
//
// ?check=1 returns just the version line, so a client can cheaply decide whether
// it needs the full body.

export const dynamic = "force-static";

export async function GET(req: Request) {
  const url = new URL(req.url);

  if (url.searchParams.get("check") === "1") {
    return new NextResponse(`VERSION|${RULES_VERSION}\nVERIFIED|${RULES_VERIFIED}\n`, {
      status: 200,
      headers: {
        "Content-Type": "text/plain; charset=utf-8",
        "Cache-Control": "public, max-age=1800",
      },
    });
  }

  return new NextResponse(RULES_TEXT, {
    status: 200,
    headers: {
      "Content-Type": "text/plain; charset=utf-8",
      "Cache-Control": "public, max-age=1800",
      "X-Ballast-Rules-Version": String(RULES_VERSION),
      "X-Ballast-Rules-Verified": RULES_VERIFIED,
    },
  });
}
