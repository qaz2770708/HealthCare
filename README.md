# 青禾医疗卫生管理系统

基于 C#、ASP.NET Core 10、Entity Framework Core 10 和 SQLite 的中文社区门诊管理程序。页面与 API 运行在同一 ASP.NET 服务中，数据保存在服务器数据库，可关闭后再次打开继续使用。

## 快速运行

### 使用已编译程序

1. 解压 `HealthCare-App.zip`。
2. 本机需安装 **ASP.NET Core Runtime 10 x64**，或包含此运行时的 .NET 10 SDK；[微软官方下载](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。当前开发电脑已安装。
3. 双击 `start-demo.cmd`，保持打开的终端窗口运行。
4. 浏览器访问 **http://localhost:5080**。

如果端口被占用，可在程序目录执行 `dotnet HealthCare.dll --environment Development --urls http://localhost:5082`，然后访问新端口。

### 从源码运行

安装 .NET 10 SDK 后，在本项目目录执行：

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --launch-profile HealthCare
```

也可以用支持 .NET 10 的 Visual Studio 打开 `HealthCare.csproj`。前端不依赖 Node、npm 或 CDN。

### 演示账号

| 角色 | 账号 | 密码 |
| --- | --- | --- |
| 管理员 | admin | ClinicDemo!2026 |
| 全科医生 | doctor | ClinicDemo!2026 |
| 内科医生 | doctor2 | ClinicDemo!2026 |
| 健康管理医生 | doctor3 | ClinicDemo!2026 |
| 前台 | reception | ClinicDemo!2026 |

仅在 Development 环境且 `Clinic:SeedDemo=true`、数据库无员工时创建演示数据。演示记录为虚构数据。数据日期以首次启动时的门诊当地日期生成；后续启动不会覆盖已有数据。

## 已实现功能

| 模块 | 可执行操作 |
| --- | --- |
| 工作台 | 今日预约、候诊、患者总数、实收统计、7 日趋势与候诊队列 |
| 患者档案 | 新建、搜索、分页、编辑、联系方式、过敏信息、历史就诊记录 |
| 预约挂号 | 患者检索、选择医生、查询可约时段、预约、报到、取消 |
| 医生接诊 | 开始接诊、主诉/评估/处置记录、保存草稿、确认完成 |
| 收费管理 | 自动生成诊疗账单、线下收款登记、防重复收款、打印内部凭据 |
| 随访计划 | 就诊时设置随访日期，显示未来 30 天计划 |
| 员工管理 | 管理员创建前台、医生或管理员账号 |
| 操作日志 | 登录、档案查看与业务写入记录，分页查询 |
| 账号安全 | 密码哈希、角色鉴权、CSRF 校验、登录限流、失败锁定、修改密码 |

## 操作示例

1. 前台登录，在「患者档案」新建患者。
2. 在「预约挂号」搜索患者，选医生、日期和可用时段。
3. 预约当日，前台点击「报到」，进入候诊队列。
4. 对应医生登录，在「医生接诊」点击「开始接诊」。演示数据已提供当天候诊患者，可直接体验。
5. 核对过敏信息，填写主诉、临床评估、实际处置，可先保存草稿。
6. 「确认完成就诊」锁定本次病历并生成基础诊疗费账单。
7. 前台在「收费管理」核对实际到账，登记收款方式并打印内部凭据。
8. 医生在「随访计划」查看设置的随访日期。

预约支持未来 90 天、每天 08:00–17:30，每 30 分钟一个号源。默认所有医生采用相同营业时间，暂不包含排班、节假日或临时停诊管理。服务器同时检查医生时段冲突和患者同一时段重复预约。

## 权限

| 操作 | 管理员 | 医生 | 前台 |
| --- | --- | --- | --- |
| 查看患者基本档案 | 是 | 是 | 是 |
| 新建、编辑患者 | 是 | 否 | 是 |
| 查看患者已完成的历史病历 | 是 | 是（院内连续照护） | 否 |
| 预约、报到、取消 | 是 | 否 | 是 |
| 接诊与修改病历草稿 | 是 | 仅自己的预约 | 否 |
| 收费与收款凭据 | 是 | 否 | 是 |
| 随访计划 | 全部 | 仅本人计划 | 否 |
| 员工管理与操作日志 | 是 | 否 | 否 |

菜单隐藏之外，每个业务 API 也执行服务端授权。已完成病历不可直接修改；本版本没有医疗文书更正/签章流程。管理员拥有临床数据权限，正式应用应按机构岗位制度进一步细分。

## 技术结构

```text
HealthCare/
  Controllers/AuthController.cs     登录、退出、修改密码
  Controllers/ClinicController.cs   患者、预约、病历、收费、统计 API
  Models/Entities.cs                EF Core 实体、关系、唯一索引与并发版本
  Models/Requests.cs                请求参数及验证规则
  Services/ClinicClock.cs           门诊时区
  Services/Seeder.cs                数据库初始化与演示数据
  wwwroot/                         中文界面、样式、浏览器交互
  tests/integration.py              独立数据库黑盒集成测试
  tools/backup.py                   SQLite 在线一致性备份
  Program.cs                       服务注册、安全中间件与启动
  App_Data/                        启动后生成的数据库及会话密钥
```

患者 → 预约 → 病历 / 账单；员工与预约关联；操作日志记录动作和实体编号，不复制整份病历。预约与付款使用版本字段拒绝过期更新。完成病历与创建账单在同一次数据库保存事务中提交，唯一索引避免重复账单。金额使用整数「分」。

预约时间采用 `Clinic:TimeZone` 指定的门诊墙上时间；审计和收款时间以 UTC 保存，并在界面按门诊时区显示。

API 路由与请求结构见 `docs/API.md`。

## 测试与发布

```powershell
dotnet build
python tests/integration.py
dotnet publish -c Release -o publish
```

测试使用临时目录与 5081 端口，不修改正常数据库。Python 3 标准库即可运行。已通过 22 项集成检查，包括权限、CSRF、预约冲突、病历版本、重复收款、密码变更会话失效、登录锁定/限流和重启持久化。浏览器验收详情见 `docs/VERIFICATION.md`。

## 正式部署配置

这是可运行的门诊业务基础版，适合流程演示、二次开发和受控试用。正式接入患者数据前，还需完成机构的安全、权限、备份恢复和业务验收。当前不包含医保结算、电子处方、药房库存、检验影像、支付网关、法定收费票据、数字签章或对接医院 HIS；系统不自动诊断或推荐治疗。

1. 使用独立数据库目录，切换到 `Production`；不要复用演示数据库。
2. 首次启动设置环境变量 `Bootstrap__AdminPassword`（至少 12 位）。Production 不创建公开演示账号，只创建 admin；初始化后移除此环境变量。
3. 配置有效 HTTPS 证书和 Kestrel HTTPS 监听。Production 使用 Secure Cookie、HSTS 与 HTTPS 重定向，不能以纯 HTTP 方式登录。
4. 设置 `AllowedHosts` 为实际域名，并让应用服务账号仅能访问自己的数据目录。
5. 将 `Clinic__DataDirectory` 指向持久存储；SQLite 文件和 Data Protection 密钥需保护访问权限，部署到加密磁盘并纳入备份。当前应用没有内置数据库文件加密或外部密钥托管。
6. 生产启动采用最新维护版本的 .NET 10 运行时，并根据机构部署规范维护补丁。

示例环境变量（密码与证书密码通过部署系统注入，不写入源码）：

```text
ASPNETCORE_ENVIRONMENT=Production
Clinic__DataDirectory=D:\ClinicData
Clinic__SeedDemo=false
Clinic__Name=实际机构名称
Clinic__TimeZone=Asia/Shanghai
Clinic__ConsultationFeeCents=3000
AllowedHosts=clinic.example.org
ASPNETCORE_URLS=https://0.0.0.0:5443
Kestrel__Certificates__Default__Path=D:\Certificates\clinic.pfx
```

另需注入 `Kestrel__Certificates__Default__Password` 和首次启动的 `Bootstrap__AdminPassword`。若采用反向代理，需按实际可信代理配置 HTTPS 转发；本项目没有预置对任意代理的信任。

目前使用 `EnsureCreated` 初始化数据库，适用于 v1.0 新部署；更新数据库结构前必须编写并验证迁移方案，不可直接覆盖或删除已有数据库。SQLite 适合单实例、小规模门诊；多实例或较大并发需迁移到 SQL Server/PostgreSQL 并进行容量验证。

## 备份与恢复

在项目目录运行：

```powershell
python tools/backup.py App_Data backups
```

脚本通过 SQLite Backup API 创建一致性快照，不直接复制正在使用的 WAL 数据文件。备份含患者信息，应与生产数据库采取相同保护。恢复时停止应用，将备份的 `clinic.db` 与 `keys` 放入独立恢复目录，修改 `Clinic__DataDirectory` 指向该目录后启动并验证。恢复演练应先在隔离环境完成。

## 参考文档

- [.NET 10 下载与运行时](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [ASP.NET Core 防伪令牌](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

本项目未向外部服务上传患者数据，前端不加载外部字体、脚本或图片。
