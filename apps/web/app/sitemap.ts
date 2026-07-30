import type { MetadataRoute } from "next";

const SITE = "https://tradeballast.com";

export default function sitemap(): MetadataRoute.Sitemap {
  const pages = [
    { path: "", priority: 1.0 },
    { path: "/readiness", priority: 0.9 },
    { path: "/cushion", priority: 0.9 },
    { path: "/trailing-drawdown", priority: 0.9 },
    { path: "/insights", priority: 0.8 },
    { path: "/session", priority: 0.7 },
  ];
  // Static build timestamp is fine here; content changes ship with a deploy.
  return pages.map((p) => ({
    url: `${SITE}${p.path}`,
    changeFrequency: "weekly" as const,
    priority: p.priority,
  }));
}
