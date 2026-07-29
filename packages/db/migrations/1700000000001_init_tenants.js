exports.up = (pgm) => {
  pgm.createTable("tenants", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    name: { type: "text", notNull: true },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
};
exports.down = (pgm) => pgm.dropTable("tenants");
