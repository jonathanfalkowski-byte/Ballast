// Mirrors Velocity's learning loop: did the trader obey the discipline nudge?
exports.up = (pgm) => {
  pgm.createTable("recommendation_feedback_events", {
    id: { type: "uuid", primaryKey: true, default: pgm.func("gen_random_uuid()") },
    tenant_id: { type: "uuid", notNull: true, references: "tenants", onDelete: "cascade" },
    user_id: { type: "uuid", references: "users", onDelete: "set null" },
    decision_key: { type: "text", notNull: true },
    feedback_type: { type: "text", notNull: true },        // followed | overridden | dismissed
    recommendation_action: { type: "text", notNull: true }, // stop_for_day | size_down | cooldown ...
    recommendation_label: { type: "text", notNull: true },
    context_snapshot: { type: "jsonb", notNull: true, default: pgm.func("'{}'::jsonb") },
    note: { type: "text" },
    submitted_at: { type: "timestamptz", notNull: true, default: pgm.func("now()") },
  });
  pgm.createIndex("recommendation_feedback_events", ["tenant_id"]);
  pgm.createIndex("recommendation_feedback_events", ["user_id"]);
};
exports.down = (pgm) => pgm.dropTable("recommendation_feedback_events");
