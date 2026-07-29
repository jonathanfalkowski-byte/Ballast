const path = require("path");
require("dotenv").config({ path: path.resolve(__dirname, "../.env") });

const { runner } = require("node-pg-migrate");

async function main() {
  const databaseUrl = process.env.DATABASE_URL;
  if (!databaseUrl) {
    console.error("DATABASE_URL is missing in .env");
    process.exit(1);
  }
  const migrationsDir = path.resolve(__dirname, "../packages/db/migrations");
  await runner({
    direction: "up",
    databaseUrl,
    dir: migrationsDir,
    migrationsTable: "pgmigrations",
    verbose: true,
    checkOrder: false,
  });
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
