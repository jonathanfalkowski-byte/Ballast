// One row per user: the circuit-breaker config the disciplineEngine reads.
exports.up = (pgm) => {
  pgm.createTable("rules", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    tenant_id: { type: "uuid", notNull: true, references: "tenants", onDelete: "cascade" },
    user_id: { type: "uuid", notNull: true, references: "users", onDelete: "cascade" },
    max_losses_before_stop: { type: "integer", notNull: true, default: 2 },
    daily_loss_limit: { type: "numeric", notNull: true, default: 500 },
    daily_target: { type: "numeric", notNull: true, default: 500 },
    max_trades: { type: "integer", notNull: true, default: 4 },
    max_contracts: { type: "integer", notNull: true, default: 1 },
    session_start_minute: { type: "integer", notNull: true, default: 570 }, // 9:30 ET = 570
    session_end_minute: { type: "integer", notNull: true, default: 690 },   // 11:30 ET = 690
    cooldown_minutes: { type: "integer", notNull: true, default: 5 },
    created_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
    updated_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("rules", ["user_id"], { unique: true });
};
exports.down = (pgm) => pgm.dropTable("rules");
