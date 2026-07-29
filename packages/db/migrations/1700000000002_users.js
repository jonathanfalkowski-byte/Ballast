exports.up = (pgm) => {
  pgm.createTable("users", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    tenant_id: { type: "uuid", notNull: true, references: "tenants", onDelete: "cascade" },
    email: { type: "text", notNull: true },
    name: { type: "text" },
    role: { type: "text", notNull: true, default: "trader" },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("users", ["tenant_id"]);
  pgm.createIndex("users", ["tenant_id", "email"], { unique: true });
};
exports.down = (pgm) => pgm.dropTable("users");
