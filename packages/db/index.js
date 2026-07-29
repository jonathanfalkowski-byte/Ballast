// Shared Postgres pool + query helper (mirrors Velocity's pg usage).
const { Pool } = require("pg");

let pool;
function getPool() {
  if (!pool) {
    pool = new Pool({ connectionString: process.env.DATABASE_URL });
  }
  return pool;
}

async function query(text, params) {
  return getPool().query(text, params);
}

module.exports = { getPool, query };
