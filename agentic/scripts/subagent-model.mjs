#!/usr/bin/env node
// Chỉ đọc cơ sở dữ liệu phiên OpenCode; không thay đổi dữ liệu.
import os from "node:os";
import path from "node:path";
import { DatabaseSync } from "node:sqlite";

const dbPath = process.env.OPENCODE_DB || path.join(os.homedir(), ".local", "share", "opencode", "opencode.db");
const args = process.argv.slice(2);
const limitArg = args.find((arg) => arg.startsWith("--limit="));
const sinceArg = args.find((arg) => arg.startsWith("--since="));
const limit = limitArg ? Number(limitArg.slice(8)) : 15;
const sinceHours = sinceArg ? Number(sinceArg.slice(8)) : 24;
const children = args.includes("--children");

if (!Number.isInteger(limit) || limit < 1 || !Number.isFinite(sinceHours) || sinceHours < 0) {
  console.error("Tham số không hợp lệ: --limit phải là số nguyên dương, --since là số giờ không âm.");
  process.exit(1);
}

let db;
try {
  db = new DatabaseSync(dbPath, { readOnly: true });
} catch (error) {
  console.error(`Không mở được DB OpenCode tại ${dbPath}: ${error.message}`);
  process.exit(1);
}

try {
  const table = db.prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='session_v2'").get();
  if (!table) {
    console.error(`Không tìm thấy bảng session_v2 trong ${dbPath}.`);
    process.exitCode = 1;
  } else {
    const columns = db.prepare("PRAGMA table_info(session_v2)").all().map((column) => column.name);
    const required = ["id", "time_created", "parent_id", "agent", "model", "cost", "title"];
    const missing = required.filter((column) => !columns.includes(column));
    if (missing.length) {
      console.error(`session_v2 thiếu cột bắt buộc: ${missing.join(", ")}`);
      process.exitCode = 1;
    } else {
      const conditions = ["time_created >= ?"];
      const params = [Date.now() - sinceHours * 60 * 60 * 1000];
      if (children) conditions.push("parent_id IS NOT NULL");
      const rows = db.prepare(`SELECT time_created, parent_id, agent, model, cost, title FROM session_v2 WHERE ${conditions.join(" AND ")} ORDER BY time_created DESC LIMIT ?`).all(...params, limit);
      console.log("created (local) | type | agent | model | cost | title");
      for (const row of rows) {
        let model;
        try {
          const parsed = JSON.parse(row.model);
          model = `${parsed.providerID}/${parsed.id}${parsed.variant ? ` · ${parsed.variant}` : ""}`;
        } catch {
          model = String(row.model ?? "");
        }
        const date = new Date(row.time_created).toLocaleString();
        const title = String(row.title ?? "").slice(0, 50);
        console.log(`${date} | ${row.parent_id == null ? "root" : "child"} | ${row.agent ?? ""} | ${model} | ${Number(row.cost ?? 0).toFixed(4)} | ${title}`);
      }
    }
  }
} catch (error) {
  console.error(`Không đọc được session_v2: ${error.message}`);
  process.exitCode = 1;
} finally {
  db.close();
}
