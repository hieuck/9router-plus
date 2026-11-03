# Luật repo — RouterPlus (9router-plus)

## 1. Skill check bắt buộc (trước mọi hành động)

Trước khi làm bất cứ điều gì — kể cả hỏi clarifying question, đọc code, chạy tool — xác nhận có skill liên quan không:

- **Có skill liên quan** → invoke skill đó (`Skill` tool), trích dẫn 1 câu xác nhận đã đọc, rồi mới làm.
- **Không có skill liên quan** → nói rõ "không dùng skill", rồi làm.
- Việc quên bước này là vi phạm luật, không phải tùy chọn.

Skills liên quan thường gặp: `superpowers:brainstorming` (thiết kế/tính năng mới), `superpowers:test-driven-development` (code mới/sửa bug/refactor), `superpowers:systematic-debugging` (debug), `superpowers:writing-skills` (viết luật/skill), `ecc:dev-team` (review 4 góc nhìn), `codegraph` (đọc code).

## 2. TDD — review độc lập test TRƯỚC, 3 reviewer SAU

**Iron Law: không code production nào mà không có failing test trước.** (Áp dụng Claude Code Best Practices — Boris Cherny, Anthropic: "Write tests, commit; code, iterate, commit" và "Have one Claude write code; use another Claude to verify".)

- **Khám + Plan trước code**: đọc code (codegraph/rg), nêu plan 1 đoạn ngắn, ghi giả định. Đi lệch → course-correct sớm (dừng, sửa hướng, không patch mãi).
- **Trước khi triển khai**: viết test cho behavior mới/sửa đổi → **1 reviewer độc lập** (subagent riêng, context cô lập, không thấy code production) review test: test có đúng behavior không, có đủ edge case không, có assert real behavior không. Test chưa qua review → không viết code.
- **Triển khai** theo red-green-refactor: watch test fail (đúng reason), viết minimal code để pass, refactor. Workflow phức tạp → dùng checklist/scratchpad, đánh dấu xong từng mục.
- **Sau khi triển khai**: **3 reviewer độc lập** (subagent riêng, context cô lập lẫn nhau, không thấy reasoning của nhau — verify bằng session khác người viết) review diff. Vòng sau chỉ review phần diff mới. Bất kỳ reviewer nào chấm **Critical** → block, phải sửa và re-review. Reviewer không đồng thuận → coordinator nêu conflict, không dung hòa qua đường.
- "Watch test fail" và "3 reviewer độc lập" là bắt buộc, không bỏ qua với lý do "việc nhỏ".
- **3 vòng review fail → rethink**: nếu cùng một batch lọt qua 3 vòng review mà reviewer vẫn không pass (đặc biệt cùng loại Critical lặp lại), dừng patch. Quay lại thiết kế/cách làm — viết lại brief, đổi hướng tiếp cận, hoặc chia nhỏ bài toán. Patch mãi trên cùng hướng là vi phạm luật. (Tinh thần "rule of three": lặp lại lần 3 nghĩa là vấn đề nằm ở thiết kế, không nằm ở chi tiết.)

## 3. Agent tự vận hành — không hỏi lại user

- Sau khi dev-team/skill/luật đã thống nhất phương án, agent **tự chốt và thực hiện**, không hỏi user xác nhận lại.
- Chỉ hỏi user khi: destructive/không đảo ngược được, cần credential/secret, thêm dependency mới, hoặc conflict scope sản phẩm.
- Trước khi code: nêu giả định, nếu có nhiều cách thì chọn 1 cách và làm, ghi lý do 1 dòng. **Không trình nhiều phương án để user chọn.**
- Skill check (§1) trước mọi hành động — kể cả khi định hỏi.

## 4. Sửa đổi tối thiểu

- Chỉ chạm code liên quan trực tiếp đến task. Không refactor, không "improve" code kề bên, không dọn dead code ngoài phạm vi.
- Match style hiện có. Mỗi dòng đổi phải trace được đến yêu cầu.

## 5. Guardrails repo này (không bỏ qua được)

- **Không log secret / API key / DPAPI vault content** vào code, log, hay output.
- Build phải xanh **0 warning** (`scripts/build.ps1`; `TreatWarningsAsErrors` đang bật).
- Test liên quan phải xanh; test suite đủ 5 project phải xanh trước khi báo done.
- UI automation / provider flow: không claim "working" nếu chưa có live verification evidence (screenshot/log) lưu dưới `tools/live-verify/`.
- **Không commit khi chưa được user yêu cầu. Không push.**

## 6. Model / router

- Model pool, quota, credit **không ghi trong luật**. Chọn model theo cost/quality tại thời điểm dùng. Không hardcode số liệu quota vào luật.

## 7. Quyền lực agent

- Agent được phép: đọc mọi file, viết code, chạy build/test, invoke subagent, invoke skill.
- Agent không được: push, commit khi chưa được yêu cầu, làm ngược §1–§5.
