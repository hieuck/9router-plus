#!/usr/bin/env node
// Standalone 9router model evaluation probe.
// Usage: node agentic/scripts/model-eval.mjs [--only=<substr>] [--limit=<n>] [--timeout=<ms>] [--concurrency=<n>] [--tps] [--output=<path>] [--json] [--api-key=<key>] [--help]

import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

const USAGE =
  "node agentic/scripts/model-eval.mjs [--only=<substr>] [--limit=<n>] [--timeout=<ms>] [--concurrency=<n>] [--tps] [--output=<path>] [--json] [--api-key=<key>] [--help]";

const TEXT_PROMPT = "Reply with exactly: MODEL_EVAL_OK. Nothing else.";
const TOOL_PROMPT =
  "Use the shell tool to run exactly: echo MODEL_EVAL_TOOL_OK. Then reply with only the stdout.";
const TPS_PROMPT = "Write the numbers from 1 to 200 separated by spaces. No other text.";

const SHELL_TOOL = [
  {
    type: "function",
    function: {
      name: "shell",
      description: "Run a shell command",
      parameters: {
        type: "object",
        properties: { command: { type: "string" } },
        required: ["command"],
        additionalProperties: false,
      },
    },
  },
];

function parseArgs(argv) {
  const opts = {
    help: false,
    json: false,
    tps: false,
    only: null,
    limit: null,
    timeout: 15000,
    concurrency: 3,
    output: "agentic/state/model-eval.json",
    apiKey: null,
  };

  const needValue = (flag) => {
    // returns true if flag expects a value
    return (
      flag === "--only" ||
      flag === "--limit" ||
      flag === "--timeout" ||
      flag === "--concurrency" ||
      flag === "--output" ||
      flag === "--api-key"
    );
  };

  const assign = (flag, raw) => {
    switch (flag) {
      case "--only":
        opts.only = raw;
        break;
      case "--limit":
        opts.limit = Number(raw);
        break;
      case "--timeout": {
        const n = Number(raw);
        if (!Number.isInteger(n)) throw new Error("--timeout must be an integer (milliseconds)");
        opts.timeout = n;
        break;
      }
      case "--concurrency":
        opts.concurrency = Number(raw);
        break;
      case "--output":
        opts.output = raw;
        break;
      case "--api-key":
        opts.apiKey = raw;
        break;
      default:
        throw new Error(`Unknown flag: ${flag}`);
    }
  };

  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--help" || a === "-h") {
      opts.help = true;
      continue;
    }
    if (a === "--json") {
      opts.json = true;
      continue;
    }
    if (a === "--tps") {
      opts.tps = true;
      continue;
    }
    const eq = a.indexOf("=");
    if (eq > 0) {
      const flag = a.slice(0, eq);
      const val = a.slice(eq + 1);
      if (!needValue(flag)) throw new Error(`Unknown flag: ${flag}`);
      assign(flag, val);
    } else {
      if (!needValue(a)) throw new Error(`Unknown flag: ${a}`);
      if (i + 1 >= argv.length) throw new Error(`Missing value for ${a}`);
      assign(a, argv[++i]);
    }
  }
  return opts;
}

function fail(msg) {
  console.error(msg);
  process.exitCode = 1;
}

function responseParts(body) {
  const events = [];
  for (const line of body.split(/\r?\n/)) {
    const match = /^data:\s*(\{.*\})\s*$/.exec(line);
    if (match) {
      try {
        events.push(JSON.parse(match[1]));
      } catch {
        // ignore malformed chunk
      }
    }
  }
  if (!events.length) {
    const json = body.match(/\{[\s\S]*\}\s*data:\s*\[DONE\]/);
    if (json) {
      try {
        events.push(JSON.parse(json[0].replace(/\s*data:\s*\[DONE\]\s*$/, "")));
      } catch {
        // ignore
      }
    }
    if (!events.length) {
      try {
        events.push(JSON.parse(body));
      } catch {
        // ignore
      }
    }
  }
  let content = "";
  let reasoning = "";
  const usage = {};
  let finishReason = null;
  const calls = new Map();
  for (const event of events) {
    if (event && event.usage) Object.assign(usage, event.usage);
    for (const choice of event?.choices ?? []) {
      finishReason = choice.finish_reason ?? finishReason;
      const msg = choice.message ?? choice.delta ?? {};
      if (typeof msg.content === "string") content += msg.content;
      if (typeof msg.reasoning_content === "string") reasoning += msg.reasoning_content;
      for (const call of msg.tool_calls ?? []) {
        const index = call.index ?? calls.size;
        const prev = calls.get(index) ?? {
          id: "",
          type: "function",
          function: { name: "", arguments: "" },
        };
        prev.id += call.id ?? "";
        prev.type = call.type ?? prev.type;
        prev.function.name += call.function?.name ?? "";
        prev.function.arguments += call.function?.arguments ?? "";
        calls.set(index, prev);
      }
    }
  }
  return { content, reasoning, usage, finishReason, calls: [...calls.values()] };
}

async function request(url, key, model, prompt, maxTokens, timeout, tools) {
  const started = Date.now();
  try {
    const res = await fetch(`${url}/v1/chat/completions`, {
      method: "POST",
      signal: AbortSignal.timeout(timeout),
      headers: { authorization: `Bearer ${key}`, "content-type": "application/json" },
      body: JSON.stringify({
        model,
        messages: [{ role: "user", content: prompt }],
        max_tokens: maxTokens,
        stream: true,
        ...(tools ? { tools } : {}),
      }),
    });
    const body = await res.text();
    return { status: res.status, ms: Date.now() - started, ...responseParts(body), error: res.ok ? "" : body.slice(0, 100) };
  } catch (err) {
    return {
      status: 0,
      ms: Date.now() - started,
      content: "",
      reasoning: "",
      usage: {},
      finishReason: null,
      calls: [],
      error: String(err?.message ?? err).slice(0, 100),
    };
  }
}

async function main() {
  let opts;
  try {
    opts = parseArgs(process.argv.slice(2));
  } catch (err) {
    console.error(err.message);
    console.error(`Usage: ${USAGE}`);
    process.exitCode = 1;
    return;
  }

  if (opts.help) {
    console.log(`Usage: ${USAGE}`);
    console.log("");
    console.log("Probe text + shell tool on the 9router model catalog.");
    console.log("  --only=<substr>      filter models whose id contains substr");
    console.log("  --limit=<n>          limit number of models");
    console.log("  --timeout=<ms>       per-request timeout (default 15000, cap 20000)");
    console.log("  --concurrency=<n>    parallel workers (default 3)");
    console.log("  --tps                measure tokens/sec on a third prompt");
    console.log("  --output=<path>      output JSON path (default agentic/state/model-eval.json)");
    console.log("  --json               print full JSON to stdout");
    console.log("  --api-key=<key>      API key (or set NINE_ROUTER_KEY)");
    return;
  }

  const timeout = Math.min(Number.isFinite(opts.timeout) ? opts.timeout : 15000, 20000);
  const concurrency = opts.concurrency;
  if (!Number.isFinite(timeout) || timeout <= 0) {
    fail("timeout must be a positive number");
    return;
  }
  if (!Number.isFinite(concurrency) || concurrency <= 0) {
    fail("concurrency must be a positive number");
    return;
  }
  if (opts.limit !== null && (!Number.isFinite(opts.limit) || opts.limit <= 0)) {
    fail("limit must be a positive number");
    return;
  }

  const baseUrl = (process.env.NINE_ROUTER_URL || "http://127.0.0.1:20128").replace(/\/$/, "");

  const key = opts.apiKey || process.env.NINE_ROUTER_KEY;
  if (!key) {
    fail("Missing API key: set NINE_ROUTER_KEY or pass --api-key=<key>.");
    return;
  }

  let catalog;
  try {
    const res = await fetch(`${baseUrl}/v1/models`, {
      headers: { authorization: `Bearer ${key}` },
      signal: AbortSignal.timeout(timeout),
    });
    if (!res.ok) throw new Error(`GET /v1/models HTTP ${res.status}`);
    catalog = await res.json();
  } catch (err) {
    fail(`Could not fetch model list: ${err.message}`);
    return;
  }

  let models = (catalog.data ?? catalog.models ?? [])
    .map((m) => (typeof m === "string" ? { id: m } : m))
    .filter((m) => m?.id && (!opts.only || String(m.id).includes(opts.only)))
    .sort((a, b) => String(a.id).localeCompare(String(b.id)));
  if (opts.limit !== null) models = models.slice(0, opts.limit);
  if (!models.length) {
    fail("No models match the filter.");
    return;
  }

  const results = new Array(models.length);
  let next = 0;
  async function worker() {
    while (true) {
      const i = next++;
      if (i >= models.length) return;
      const model = models[i];
      const text = await request(baseUrl, key, model.id, TEXT_PROMPT, 80, timeout);
      const tool =
        text.status === 200
          ? await request(baseUrl, key, model.id, TOOL_PROMPT, 120, timeout, SHELL_TOOL)
          : null;
      let tps = null;
      if (opts.tps && text.status === 200) {
        const probe = await request(baseUrl, key, model.id, TPS_PROMPT, 300, timeout);
        const tokens = probe?.usage?.completion_tokens;
        if (probe?.status === 200 && typeof tokens === "number" && tokens >= 50 && probe.ms > 0) {
          tps = Math.round((tokens / (probe.ms / 1000)) * 10) / 10;
        }
      }
      const toolCalls = tool?.calls ?? [];
      const shellCalls = toolCalls.filter((c) => c.function?.name === "shell");
      const toolRequested = shellCalls.length > 0;
      let requestedCommand = null;
      for (const c of shellCalls) {
        try {
          const args = JSON.parse(c.function?.arguments ?? "{}");
          if (args?.command === "echo MODEL_EVAL_TOOL_OK") requestedCommand = args.command;
        } catch {
          // malformed arguments: not an exact match
        }
      }
      let executedOk = false;
      if (requestedCommand !== null) {
        try {
          const out =
            process.platform === "win32"
              ? execFileSync(process.env.ComSpec || "cmd.exe", ["/c", requestedCommand], {
                  encoding: "utf8",
                  timeout: 5000,
                })
              : execFileSync("sh", ["-c", requestedCommand], {
                  encoding: "utf8",
                  timeout: 5000,
                });
          executedOk = String(out).includes("MODEL_EVAL_TOOL_OK");
        } catch {
          executedOk = false;
        }
      }
      const toolOk = requestedCommand !== null && executedOk;
      let note = text.error || text.content;
      if (tool?.error && !note) note = tool.error;
      results[i] = {
        model: model.id,
        context: model.context_length ?? model.context ?? null,
        status: text.status,
        ms: text.ms,
        textOk: String(text.content ?? "").includes("MODEL_EVAL_OK"),
        toolStatus: tool?.status ?? null,
        toolRequested,
        toolOk,
        promptTokens: text.usage?.prompt_tokens ?? null,
        completionTokens: text.usage?.completion_tokens ?? null,
        credits: text.usage?.kiro_credits ?? null,
        tps,
        note: String(note ?? "").replace(/\s+/g, " ").slice(0, 100),
      };
    }
  }
  await Promise.all(
    Array.from({ length: Math.min(concurrency, models.length) }, () => worker())
  );

  const redactedBaseUrl = baseUrl.replace(/\/\/[^@/]+@/, "//***@");
  const output = { generatedAt: new Date().toISOString(), baseUrl: redactedBaseUrl, results };

  const outPath = path.resolve(opts.output);
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, `${JSON.stringify(output, null, 2)}\n`, "utf8");

  if (opts.json) {
    console.log(JSON.stringify(output, null, 2));
  } else {
    const header = `| model | status | ms | text | tool | prompt | completion | credits | ctx |${opts.tps ? " tps |" : ""} note |`;
    const sep = `|---|---:|---:|:---:|:---:|---:|---:|---:|---:|${opts.tps ? "---:|" : ""}---|`;
    console.log(header);
    console.log(sep);
    for (const r of results) {
      const tpsCol = opts.tps ? ` ${r.tps ?? "—"} |` : "";
      console.log(
        `| ${r.model} | ${r.status} | ${r.ms} | ${r.textOk ? "OK" : "FAIL"} | ${r.toolOk ? "OK" : r.toolStatus ?? "SKIP"} | ${r.promptTokens ?? ""} | ${r.completionTokens ?? ""} | ${r.credits ?? ""} | ${r.context ?? ""} |${tpsCol} ${String(r.note).replaceAll("|", "\\|")} |`
      );
    }
  }
}

main().catch((err) => {
  console.error(err?.message ?? err);
  process.exitCode = 1;
});
