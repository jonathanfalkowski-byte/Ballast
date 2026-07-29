// Prop / trading accounts. The trailing-drawdown fields are the heart of the product.
exports.up = (pgm) => {
  pgm.createTable("accounts", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    tenant_id: { type: "uuid", notNull: true, references: "tenants", onDelete: "cascade" },
    user_id: { type: "uuid", notNull: true, references: "users", onDelete: "cascade" },
    label: { type: "text", notNull: true },
    prop_firm: { type: "text" },
    account_size: { type: "numeric", notNull: true },
    trailing_drawdown: { type: "numeric", notNull: true },
    drawdown_type: { type: "text", notNull: true, default: "intraday" }, // intraday | end_of_day
    current_floor: { type: "numeric" },
    peak_balance: { type: "numeric" },
    is_active: { type: "boolean", notNull: true, default: true },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("accounts", ["tenant_id"]);
  pgm.createIndex("accounts", ["user_id"]);
};
exports.down = (pgm) => pgm.dropTable("accounts");
