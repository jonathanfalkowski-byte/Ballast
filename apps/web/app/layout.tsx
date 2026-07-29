import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Ballast — stop giving it all back",
  description:
    "The discipline layer for prop futures traders. Not another journal — an engine that tells you to stop before you blow up, and learns why you don't.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen antialiased">{children}</body>
    </html>
  );
}
