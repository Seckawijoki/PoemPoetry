# 唐诗宋词测试 — 上手指南（SETUP）

本工程在 **Unity 2022.3.62f3 LTS** 上开发，纯代码驱动 UI，运行时无需手动拖拽预制体。
按下面 4 步即可跑起来；其中只有「中文字体」是必须的手动步骤。

---

## V2 新增（本轮）

- **主界面**改为两大入口：**答题** / **滑动找诗**，各自进配置页；下方仍有 历史记录/收藏夹/错题本/设置。
- **答题配置页**：题数 + **朝代多选** + **难度多选**（难度0=当前题库）。答题**限时**（每题倒计时，按字数/难度算），顶部「**退出**」中途退出不记录；出题与答完**只显示被考句+相邻一句**，不展示整首。
- **滑动找诗配置页**：方向难度 **L1 横竖直线 / L2 横竖斜 / L3 横竖蛇形 / L4 全向蛇形** + **网格边长 8/9/10/12** + **允许重叠字**。L3/L4 需拖动**沿弯路描画**整句。
- **编辑器菜单**：
  - **唐诗宋词 ▸ 难度配置**：逐首设难度档（0=当前库），「按名气推断 0/1/2」一键填，保存写回 `poems_seed.json`，再点「内容工具 ▸ ① 生成题库」。
  - **唐诗宋词 ▸ 词性统计**：jieba.NET 词性统计 + 回填 posPattern（**默认未启用**，见下「词性工具」）。
- **题库扩充到约 83 首 / 290 题**：唐56·宋18·清3·汉2·明2·先秦1·元1；诗68·词14·曲1；按难度 0/1/2/4 分布（96/142/25/27），含 99 道**词**题与少量**四言**题。难度档定义：**0=家喻户晓(小学)、1=含名句、2=不含名句、4=生僻**。
- 题库已用离线流水线重新生成（StreamingAssets 内 poems/questions 已是最新）。**改完只需在 Unity 重新编译并 Play。**
- 想要**完整三百首×2**：用 chinese-poetry 公共语料走现有流水线导入（需补更大的拼音字典 / 提供语料文件）。

### V2.1 增量
- 答题配置页：选朝代/难度时**实时显示「当前可出题 N 道」**；页内「历史记录」。
- 诗词详情页：`0/1/2/4` **难度单选**，点选即改，存本地 `difficulty_overrides.json`（即时影响出题池）。
- 滑动找诗：每局**存记录**+页内「历史记录」；结算弹窗「继续查看」可关掉看终局网格；**不可逆向匹配**。

### V2.2 增量
- **题库扩到约 140 首 / 639 题**（含 99+ 道词题、毛泽东诗词、先唐四言、各难度档）。
- **答题干扰项改为开局实时计算**：干扰项取自所选朝代 + 难度 ≤ 所选最高档的诗库（不再用预生成固定干扰项）。
- 答题配置新增 **体裁（诗/词/曲）多选**；**记住上次**的题数/朝代/难度/体裁。
- **返回各自主页**：结算「返回」回到答题配置页（而非主菜单）。
- 诗词详情：**上一首/下一首**（从收藏/记录列表进入时可翻阅）；难度/收藏按钮已调小；**词无题时预览显示词牌**。
- 编辑器「难度配置」：每行 `+` 可**展开全诗**，难度用 `< >` 设置。
- 滑动找诗：设置新增**朝代/难度**；**找全即结束**（不等倒计时）；记录**存网格快照**，记录详情可「查看本局网格」**回看/练习（不计入记录）**；结算/回看可「**显示未滑出**」高亮未找出的诗句。

### 词性工具（jieba.NET，可选）
默认关闭，不影响构建。启用：① NuGet 取 `jieba.NET`(MIT)，把 `JiebaNet.*.dll`+词典放 `Assets/Editor/Jieba/`；② 在 `PoemPoetry.Editor.asmdef` 的 `precompiledReferences` 加这些 DLL；③ Player ▸ Scripting Define Symbols 加 `POEM_JIEBA`；④ 用 JiebaNet ConfigManager 指向词典目录。文言准确率约 80-85%，仅作统计/对仗排序参考。

---

## 1. 打开工程，等待包解析

用 Unity 2022.3.x 打开 `f:\UnityProjects\PoemPoetry`。首次打开会自动解析新增的
`com.unity.nuget.newtonsoft-json`（已写入 `Packages/manifest.json`）。等控制台不再编译即可。

代码分为若干程序集（asmdef）：`PoemPoetry.Data / Services / Core`（纯 C#，无引擎依赖）、
`PoemPoetry.UI / App`（Unity）、`PoemPoetry.Editor`（编辑器工具）、`PoemPoetry.Tests.EditMode`（测试）。

## 2. 设置中文 TMP 字体（**必须**，否则中文显示为方块）

1. 准备一个 CJK 字体（推荐 **思源宋体 SC / Source Han Serif SC** 或 **Noto Serif CJK SC**，均为 SIL OFL 可商用）。
   把 `.otf/.ttf` 拖进 `Assets/Fonts/`。
2. 菜单 **Window ▸ TextMeshPro ▸ Font Asset Creator**：
   - Source Font File = 你的中文字体
   - **Atlas Population Mode = Dynamic**（关键：CJK 两万+字形，必须动态生成，切勿烤静态图集）
   - Render Mode = SDFAA，Atlas 1024×1024
   - 点 Generate，Save 为 `Assets/Fonts/PoemFont SDF`
3. 菜单 **Window ▸ TextMeshPro ▸ Project Settings**（或 `Assets/TextMesh Pro/Resources/TMP Settings`），
   把 **Default Font Asset** 设为 `PoemFont SDF`。
   - 首次导入 TMP 若提示导入 Essentials，先点 **Import TMP Essentials**。

> 代码里所有文字都用 TMP 默认字体，设好默认字体后整个 App 自动显示中文。

## 3. 生成启动场景并运行

菜单 **唐诗宋词 ▸ 创建启动场景** → 自动创建 `Assets/Scenes/Main.unity`（含 `AppBootstrapper`），
并设为唯一构建场景。按 **Play** 即可：主菜单 → 选题数 → 答题 → 结算 → 记录/收藏/错题本/拼诗句。

（也可手动：新建空场景，建一个空物体挂 `PoemPoetry.App.AppBootstrapper` 即可。）

## 4. （可选）扩充题库

菜单 **唐诗宋词 ▸ 内容工具**：
- **① 生成题库**：读取 `Tools/SampleContent/poems_seed.json` → 自动算字数/韵脚 → 生成
  「同字数+同韵」的干扰项 → 写入 `Assets/StreamingAssets/PoemData/{poems,questions,rhyme_groups}.json`。
- **② 校验题库**：检查字数/韵组/重复等硬约束，列出 ERROR/WARNING。

**加诗的方法**：编辑 `Tools/SampleContent/poems_seed.json`（每首给 `lines[].text` 和 `isRhymeLine`），
并确保每句**末字**的拼音在 `Assets/StreamingAssets/PoemData/char_pinyin.json` 里（多音字按韵脚正确读音排在首位），
然后点①重新生成。某首诗若「同字数同韵」候选不足 3 个会自动跳过，不出题——题库越大越少见。

> 接公共语料（chinese-poetry）时：把语料文本转成同样的 `poems_seed` 结构、补齐末字拼音字典即可走同一条流水线。
> 自动出题的已知风险是「另一个选项其实也通」与「明显跑题给答案」——可在 `questions.json` 里人工删除坏题，
> 或在内容工具里扩展黑名单（已预留）。

---

## 运行单元测试

- **引擎外（最快，已验证 64/64）**：`Tools/TestHarness/build_and_test.ps1`
  用本机 Roslyn 编译 `Data+Services+Core` 与测试程序，跑通会顺带重新生成 StreamingAssets 题库。
- **Unity 内**：菜单 **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**（`Assets/Tests/EditMode`）。

## 目录结构

```
Assets/
  Scripts/
    Data/      模型 + 仓储接口 + JSON 实现 + 内容源（纯 C#，可单测）
    Services/  韵脚引擎 PinyinRhyme/RhymeService、出题 QuestionGenerator、
               ContentService/QuizService/RecordService/FavoriteService/WrongBookService/SettingsService
    Core/      AppServices（组装根）+ App 门面
    UI/        UiKit + UIScreen + ScreenNavigator + 各 *Screen（代码驱动 UI）
    App/       AppBootstrapper + StreamingAssetsTextLoader（Android 用 UnityWebRequest）
    Editor/    内容工具窗口 + 启动场景生成器
  Tests/EditMode/   NUnit 烟雾测试
  StreamingAssets/PoemData/   出厂题库（由内容工具生成）
  Fonts/   放中文字体与生成的 TMP SDF
Tools/
  SampleContent/poems_seed.json   题库种子（手工编辑这里）
  TestHarness/                    引擎外测试与流水线（开发用，不进构建）
```

## 架构要点（便于后续接后端）

- 用户数据走 `IRecordRepository / IFavoriteRepository / IWrongBookRepository / ISettingsStore`
  四个「仓储接口」，目前是 `persistentDataPath` 下的 JSON 实现；将来换 SQLite 或 REST 只需新增实现类，
  改 `AppServices.CreateAsync` 一处即可，UI/Services 不动。接口已是 `Task` 异步签名，换网络后端不需改调用链。
- 出厂内容走 `IContentSource`（StreamingAssets JSON），同理可换 Addressables/远程包。

## 平台 / 构建

- 默认手机优先（竖屏 1080×1920 参考分辨率，2×2 可点选）。旧输入系统 + uGUI 同时支持触摸与鼠标。
- **Android**：StreamingAssets 在 APK 内，已用 `UnityWebRequest` 异步读取；请在真机验证中文动态字体与读取。
- 安卓返回键 / Esc：`AppBootstrapper.Update` 调 `ScreenNavigator.HandleBack()`。

## 排错

- **中文全是方块** → 没设 TMP 默认字体（见第 2 步），或字体不是 Dynamic。
- **运行报「内容加载失败」** → 先跑一次「内容工具 ▸ ①生成题库」或 `build_and_test.ps1` 生成 StreamingAssets。
- **Newtonsoft 找不到** → 等 Unity 解析包；必要时 Window ▸ Package Manager 确认 `Newtonsoft Json` 已安装。
```
