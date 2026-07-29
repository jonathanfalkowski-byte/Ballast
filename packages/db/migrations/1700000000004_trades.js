exports.up = (pgm) => {
  pgm.createTable("trades", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    tenant_id: { type: "uuid", notNull: true, references: "tenants", onDelete: "cascade" },
    user_id: { type: "uuid", notNull: true, references: "users", onDelete: "cascade" },
    account_id: { type: "uuid", references: "accounts", onDelete: "set null" },
    symbol: { type: "text", notNull: true },
    direction: { type: "text", notNull: true }, // long | short
    contracts: { type: "integer", notNull: true, default: 1 },
    entry_price: { type: "numeric" },
    exit_price: { type: "numeric" },
    pnl: { type: "numeric", notNull: true, default: 0 },
    r_multiple: { type: "numeric" },
    tag: { type: "text" }, // a_plus | plan | revenge | fomo | boredom
    notes: { type: "text" },
    opened_at: { type: "timestamptz" },
    closed_at: { type: "timestamptz" },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("trades", ["tenant_id"]);
  pgm.createIndex("trades", ["user_id"]);
  pgm.createIndex("trades", ["account_id"]);
  pgm.createIndex("trades", ["tag"]);
  pgm.createIndex("trades", ["closed_at"]);
};
exports.down = (pgm) => pgm.dropTable("trades");
