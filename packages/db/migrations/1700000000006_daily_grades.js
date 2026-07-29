// Process-over-P&L: one graded day per user. A green day with broken rules is a FAIL.
exports.up = (pgm) => {
  pgm.createTable("daily_grades", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    tenant_id: { type: "uuid", notNull: true, references: "tenants", onDelete: "cascade" },
    user_id: { type: "uuid", notNull: true, references: "users", onDelete: "cascade" },
    trade_date: { type: "date", notNull: true },
    rules_followed: { type: "boolean", notNull: true, default: false },
    grade: { type: "text" }, // A | B | C | D | F
    adherence: { type: "jsonb", notNull: true, default: pgm.func("'{}'::jsonb") },
    pnl: { type: "numeric", notNull: true, default: 0 },
    notes: { type: "text" },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("daily_grades", ["user_id", "trade_date"], { unique: true });
};
exports.down = (pgm) => pgm.dropTable("daily_grades");
