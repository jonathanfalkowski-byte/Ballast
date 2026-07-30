import type { Metadata } from "next";
import "./globals.css";

const SITE = "https://tradeballast.com";

export const metadata: Metadata = {
  metadataBase: new URL(SITE),
  title: {
    default: "Ballast — free tools for prop futures traders",
    template: "%s | Ballast",
  },
  description:
    "Free trailing-drawdown calculator, challenge-readiness check and behavioural trade analysis for prop futures traders. No signup.",
  keywords: [
    "trailing drawdown calculator",
    "prop firm drawdown",
    "intraday vs end of day trailing drawdown",
    "prop firm challenge odds",
    "futures position size calculator",
    "prop trading discipline",
  ],
  alternates: { canonical: SITE },
  openGraph: {
    type: "website",
    url: SITE,
    siteName: "Ballast",
    title: "Ballast — free tools for prop futures traders",
    description:
      "Work out your real drawdown cushion, your safe position size, and whether your trades justify buying an evaluation. Free, no signup.",
  },
  twitter: {
    card: "summary_large_image",
    title: "Ballast — free tools for prop futures traders",
    description:
      "Trailing-drawdown cushion, safe position size, and an honest challenge-readiness check. Free, no signup.",
  },
  robots: { index: true, follow: true },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen antialiased">{children}</body>
    </html>
  );
}
