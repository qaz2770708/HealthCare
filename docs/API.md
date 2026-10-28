# API 说明

所有业务 API 位于 `/api`。JSON 使用 camelCase。请求异常返回 `{ "message": "中文原因" }`，表单错误另含 `errors`。

浏览器先 GET `/api/auth/token` 获取 `token`，写操作在 `X-CSRF-TOKEN` 请求头传入，并保留 Cookie。登录后用户身份改变，需再次获取 token。未登录为 401，无权限为 403，记录冲突为 409，参数错误为 400。

| 方法 | 路由 | 作用 |
| --- | --- | --- |
| GET | /auth/config | 机构名、日期、时区、演示标识、基础费用 |
| GET | /auth/token | 获取 CSRF token |
| POST | /auth/login | `{username,password}` |
| GET | /auth/me | 当前员工 |
| POST | /auth/logout | 退出 |
| POST | /auth/password | `{currentPassword,newPassword}` |
| GET | /dashboard | 业务统计；医生返回本人接诊统计且不返回收入 |
| GET | /patients?q=&page=1 | 患者查询，每页 10 条 |
| GET | /patients/{id} | 基本档案；医生/管理员另返回已完成历史病历 |
| POST | /patients | 新建患者 |
| PUT | /patients/{id} | 更新患者，携带 version |
| GET | /doctors | 可预约医生 |
| GET | /slots?doctorId=1&date=2026-10-27 | 可约时段与 available 标记 |
| GET | /appointments?date=2026-10-27&status=&page=1 | 预约查询，每页 12 条 |
| POST | /appointments | `{patientId,doctorId,slot,reason}` |
| POST | /appointments/{id}/checkin | 报到，`{version}` |
| POST | /appointments/{id}/cancel | 取消，`{version}` |
| POST | /appointments/{id}/start | 开始接诊，`{version}` |
| GET | /appointments/{id}/encounter | 获取单次病历及患者信息 |
| PUT | /appointments/{id}/encounter?finalize=false | 保存病历；finalize=true 时完成并生成账单 |
| GET | /invoices?status=&page=1 | 账单查询，每页 10 条 |
| POST | /invoices/{id}/pay | `{method,version}`，method 为现金/银行卡/微信/支付宝 |
| GET | /followups | 未来 30 天随访，最多 100 条 |
| GET / POST | /staff | 员工列表/新增员工 |
| GET | /audit?page=1 | 操作日志，每页 15 条 |

患者写入：`{name,gender,birthDate,phone,address,allergies,version}`。gender 为「男」「女」「未知」，日期用 `yyyy-MM-dd`。

病历写入：`{complaint,assessment,plan,followUpDate,version}`。主诉、评估、处置均必填；followUpDate 可为空。草稿与最终完成均校验版本；最终确认后不可直接编辑。

员工创建：`{username,password,name,role,department}`。role 为 `Admin`、`Doctor`、`Reception`。

预约状态：`Scheduled → CheckedIn → InProgress → Completed`；`Scheduled` 或 `CheckedIn` 可转 `Cancelled`。

账单状态：`Unpaid → Paid`。目前不提供退款/撤销 API。所有收款为线下到账登记，无真实扣款操作。
