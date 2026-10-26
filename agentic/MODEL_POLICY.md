# Model pool

## Cách chọn model

Nguồn sự thật: bảng `### Kết quả` bên dưới, sinh bằng `node agentic/scripts/model-eval.mjs` khi catalog đổi. Thứ tự chọn:

1. `text=OK` và `tool=OK`.
2. Context đủ cho việc.
3. Credits thấp (9router) hoặc free (opencode).
4. `ms`/TPS thấp (TPS chỉ là tiebreaker; chạy `--tps`, đo sinh 200 số, không dùng `ms` của probe 3 token).

Chọn nhanh:

| Việc | Model | Ghi chú |
|---|---|---|
| Sửa code | `opencode/deepseek-v4.1-flash-free` | đã qua bài kiểm coder; nếu 429 thì `9router/cx/gpt-6-luna[1m]` |
| Review / đo | `9router/kr/qwen3-coder-next-thinking-agentic` | 1M, tool OK, ~0,0057 credit; rẻ hơn: `9router/kr/qwen3-coder-next-thinking` |
| Context lớn | `9router/cx/gpt-6-luna[1m]` (872k) hoặc `9router/kr/qwen3-coder-next-thinking` (1M) | |
| Free opencode | `opencode/deepseek-v4.1-flash-free` | ưu tiên theo mục `## Ưu tiên khi free dùng chung hạn mức` |

Kiro: dùng `6137105a` trước (26,23 credit), rồi `9fcad678` (50); reset 01/11 07:00 VN. Bulk dùng `kr/qwen3-coder-next-thinking`; không chạy kr subagent wave.

Default phiên chính: `9router/cx/gpt-6-luna[1m]` (quota Codex/9router, không tiêu ví OpenCode Go; không phải free tuyệt đối).
`opencode-go/*` và `opencode/*` (Zen) là model trả tiền/quota; chỉ dùng khi chủ cho phép rõ. `opencode/*-free` free nhưng chia sẻ hạn mức chung.

Không đổi default sang model trả tiền khi chưa đối chiếu bảng. Mỗi account Kiro Free có 50 credit/tháng; các model `kr/*` trên cùng một account chia sẻ 50 credit đó, account khác có quota riêng. Không xoay model trên cùng account để tăng quota.

## Cost policy

- Default must not consume a paid wallet (OpenCode Go/Zen).
- Provider-quota models (Codex/Kiro) must state the quota source.
- Paid models only with explicit owner approval.
- Kiro Free is **50 credits per account per month**, not one global pool. All `kr/*` models on one account share that account's 50 credits; different accounts have separate quotas. Unused credits do not roll over; reset is `2026-11-01T00:00:00Z` (07:00 Vietnam). `overageStatus=DISABLED`: when exhausted, requests return 402 with no extra charge. Use the cheapest capable model for bulk; use an expensive model only for hard tasks.

## Kiro quota (measured 26/10)

- 90 accounts: **4,172.54 credits** remaining in total; 6 accounts exhausted (0/50); active `6137105a` 26.23; `9fcad678` 50; `8e446274` 49.97 but skipped by router selection; most active accounts 50.
- Credit per request (short tool prompt): `qwen3-coder-next-thinking` 0.0051; `qwen3-coder-next-agentic` 0.0057; `claude-sonnet-4.5` 0.0182; `glm-5-*` 0.044–0.049.
- Fallback test: after `6137105a`, the router used `9fcad678` and a tool request returned 200; `8e446274` was skipped (check testStatus/modelLock).
- Do not bulk-apply `builder-id`/profileArn to all accounts; test one account first. A wrong ARN returns 403; a missing one can return 400.

## Danh mục 9router

Đọc từ `9router` (`GET /v1/models`) ngày 26/10 — 35 model, tiền tố `9router/` khi gọi:

- `9router/agnes/agnes-2.5-flash`
- `9router/agnes/agnes-3.0-flash`
- `9router/cx/gpt-6-luna`
- `9router/cx/gpt-6-luna[1m]`
- `9router/kr/auto`
- `9router/kr/auto-thinking`
- `9router/kr/claude-sonnet-4.5`
- `9router/kr/deepseek-3.2-agentic`
- `9router/kr/deepseek-3.2-thinking`
- `9router/kr/deepseek-3.2-thinking-agentic`
- `9router/kr/glm-5-agentic`
- `9router/kr/glm-5-thinking`
- `9router/kr/glm-5-thinking-agentic`
- `9router/kr/minimax-m2.1`
- `9router/kr/minimax-m2.1-agentic`
- `9router/kr/minimax-m2.1-thinking`
- `9router/kr/minimax-m2.1-thinking-agentic`
- `9router/kr/minimax-m2.5`
- `9router/kr/minimax-m2.5-agentic`
- `9router/kr/minimax-m2.5-thinking`
- `9router/kr/minimax-m2.5-thinking-agentic`
- `9router/kr/qwen3-coder-next-agentic`
- `9router/kr/qwen3-coder-next-thinking`
- `9router/kr/qwen3-coder-next-thinking-agentic`
- `9router/mmf/mimo-auto`
- `9router/nous/reflection/beam-1.0-501b-a23b:free`
- `9router/oc/beam-free`
- `9router/oc/deepseek-v4.1-flash-free`
- `9router/oc/jev-1.13-free`
- `9router/ollama/clef`
- `9router/ollama/clef-flash`
- `9router/ollama/clef:cloud`
- `9router/ollama/nemotron-3-ultra`
- `9router/openrouter/nvidia/nemotron-3-ultra-550b-a55b:free`
- `9router/openrouter/typesafe/jev-1.13`

## Đánh giá model 9router

Khi catalog có model mới, chạy:

```bash
node agentic/scripts/model-eval.mjs
node agentic/scripts/model-eval.mjs --only=<substr> --limit=2   # thử nhanh
node agentic/scripts/model-eval.mjs --tps --only=<substr> --limit=2   # đo TPS sinh 200 số
node agentic/scripts/model-eval.mjs --output=agentic/state/model-eval-full.json   # chạy đầy đủ, ghi file riêng
node agentic/scripts/model-eval.mjs --only=<substr> --limit=2 --output=agentic/state/model-eval-smoke.json   # smoke, ghi file riêng
```

Script in bảng markdown và ghi `agentic/state/model-eval.json` mặc định (dùng `--output=<path>` để ghi đường khác). Chỉ đưa model vào pool/default sau khi có kết quả; không đổi default sang model trả tiền khi chưa so sánh.
`--tps` đo riêng một lượt sinh 200 số; TPS = completion_tokens / thời gian, chỉ ghi khi completion >= 50 token.

### Kết quả 26/10

Tất cả dòng trong bảng đều text=OK và tool=OK; lỗi nằm ở danh sách dưới. Bảng là snapshot 26/10; agentic/state/model-eval.json là lần chạy gần nhất và có thể đã lọc bằng --only.

| model | ms | credits | ctx |
|---|---:|---:|---:|
| `kr/qwen3-coder-next-thinking` | 986 | 0.005112469320066334 | 1000000 |
| `kr/qwen3-coder-next-agentic` | 928 | 0.005712568822553898 | 1000000 |
| `kr/qwen3-coder-next-thinking-agentic` | 2749 | 0.0057477097844112765 | 1000000 |
| `kr/minimax-m2.1-thinking` | 1159 | 0.012029058043117742 | 200000 |
| `kr/minimax-m2.1` | 1308 | 0.012132248756218903 | 200000 |
| `kr/auto` | 1681 | 0.014026496351575454 | 200000 |
| `kr/minimax-m2.1-thinking-agentic` | 2151 | 0.014549835820895522 | 200000 |
| `kr/minimax-m2.1-agentic` | 2056 | 0.014753099502487562 | 200000 |
| `kr/claude-sonnet-4.5` | 3612 | 0.018230349071310117 | 200000 |
| `kr/minimax-m2.5` | 1461 | 0.019989001658374795 | 204800 |
| `kr/minimax-m2.5-thinking` | 1379 | 0.020171426202321725 | 204800 |
| `kr/minimax-m2.5-agentic` | 1446 | 0.02266808623548922 | 204800 |
| `kr/minimax-m2.5-thinking-agentic` | 1442 | 0.022683731343283577 | 204800 |
| `kr/deepseek-3.2-thinking` | 1408 | 0.02328685240464345 | 128000 |
| `kr/deepseek-3.2-agentic` | 1041 | 0.02606671310116086 | 128000 |
| `kr/deepseek-3.2-thinking-agentic` | 1520 | 0.026213021558872306 | 128000 |
| `kr/glm-5-thinking` | 1270 | 0.04430975787728027 | 200000 |
| `kr/glm-5-agentic` | 1179 | 0.04926471144278607 | 200000 |
| `kr/glm-5-thinking-agentic` | 2134 | 0.0495354096185738 | 200000 |
| `agnes/agnes-3.0-flash` | 589 | — | 512000 |
| `agnes/agnes-2.5-flash` | 4621 | — | 512000 |
| `cx/gpt-6-luna` | 1340 | — | 272000 |
| `cx/gpt-6-luna[1m]` | 1381 | — | 872000 |
| `nous/reflection/beam-1.0-501b-a23b:free` | 1147 | — | 200000 |
| `openrouter/nvidia/nemotron-3-ultra-550b-a55b:free` | 2634 | — | 128000 |

- `oc/beam-free`, `oc/deepseek-v4.1-flash-free`, `oc/jev-1.13-free`: 429 FreeUsageLimitError.
- `ollama/clef`, `ollama/clef-flash`, `ollama/clef:cloud`: 400 — decision model only supports `/v1/systemone`.
- `mmf/mimo-auto`: 400.
- `openrouter/typesafe/jev-1.13`: 400 — decision model.
- `kr/auto-thinking`: text FAIL (`I can't discuss that.`), tool OK.
- `ollama/nemotron-3-ultra`: text OK, tool probe timeout (status 0).

Chọn kr: qwen3-coder-next-thinking (0,0051) cho bulk; qwen3-coder-next-thinking-agentic (0,0057) khi cần agentic; claude-sonnet-4.5 (0,0182) chỉ việc khó; tránh GLM (0,044–0,049).

### Kiro quota (đo 26/10)

- 90 account: tổng còn **4.172,54 credit**; 6 account đã 0/50; account active `6137105a` còn **26,23**; `8e446274` còn **49,97**; phần lớn active còn 50.
- Reset **01/11 07:00 giờ Việt Nam** (2026-11-01T00:00:00Z); `overageStatus=DISABLED` ⇒ hết credit thì 402, không charge thêm.
- Credit/lượt (đo, prompt tool ngắn): `qwen3-coder-next-thinking` 0,0051; `qwen3-coder-next-agentic` 0,0057; `claude-sonnet-4.5` 0,0182; `glm-5-*` 0,044–0,049.
- Dùng Qwen cho bulk; Claude/GLM chỉ cho việc khó. Muốn dùng quota account khác phải rotate account và xử lý `profileArn`; không bulk khi chưa test một account.
- Fallback test 26/10: sau `6137105a`, 9router dùng `9fcad678` (50 credit) và tool-call OK; `8e446274` bị selection bỏ qua (kiểm tra testStatus/modelLock trước khi dùng).

## Ưu tiên khi free dùng chung hạn mức

Các model `opencode/*-free` (Zen) chia sẻ một hạn mức. Thử
`opencode/deepseek-v4.1-flash-free` trước; chỉ khi model đó `limit` hoặc
`missing` mới chuyển lần lượt sang `glm-5.3-flash-free` →
`nemotron-3.5-*` → `mimo-v2.6-flash-free` → `flint-40b-free`.

Hạn mức free reset lúc **07:00 giờ Việt Nam** (00:00 UTC).

## Phân vai

Không phải model nào cũng được sửa code. Ba bậc:

- **Probe**: mọi model trong pool. Chỉ việc trivial, review, đo.
- **Coder thử việc**: đã qua bài kiểm coder, hoặc có ít nhất 1 lô
  thật được **cả 3 reviewer** PASS (không Critical). Được sửa code trong phạm vi 1
  file + test, mỗi lô vẫn qua 3 reviewer.
- **Coder chính thức**: coder thử việc hoàn thành **3 lô liên tiếp** không có
  phát hiện Critical từ 3 reviewer, không bị revert. Được nhận lô nhiều file.

Giám khảo: coordinator + 3 reviewer của lô. Thăng bậc ghi ngay vào danh sách
dưới đây; không tự suy ra từ việc "đã chạy được".

- Coder chính thức: (trống)
- Coder thử việc:
  - `opencode/deepseek-v4.1-flash-free` — qua bài kiểm coder 25/10 (6/6, `check` PASS), chưa có lô thật.
  - `9router/cx/gpt-6-luna[1m]` — có lô thật đã PASS, còn Critical trong phiên 25/10.
- Probe (đã thi nhưng trượt lô thật): `9router/kr/claude-sonnet-4.5` — qua bài kiểm median nhưng 2 lần giao lô thật đều trả hash, không sửa file.
- Probe (chưa thi coder): `9router/kr/qwen3-coder-next-thinking-agentic` — tool-call OK 26/10, 1M context, chưa qua bài kiểm coder.

Hạ bậc (tính theo lô): lô bị bất kỳ reviewer nào chấm Critical ⇒ trả về thử
việc; lô kế tiếp cũng có Critical ⇒ về Probe.

## Trạng thái

Output gần nhất: `agentic/state/model-eval.json` (không commit).