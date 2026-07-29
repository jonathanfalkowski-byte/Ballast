// Phase 0: capture interest before the app exists.
exports.up = (pgm) => {
  pgm.createTable("waitlist_signups", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    email: { type: "text", notNull: true },
    source: { type: "text" },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("waitlist_signups", ["email"], { unique: true });
};
exports.down = (pgm) => pgm.dropTable("waitlist_signups");
