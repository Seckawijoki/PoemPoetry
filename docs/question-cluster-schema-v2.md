# 选择题干扰项「共享簇」重构方案（schema v2）

状态：**已实现**（对仗/对偶维度按约定暂缓）
作者：本次会话整理
适用文件：`Assets/StreamingAssets/PoemData/questions.json`（+ 加载/出题/校验链路）

## 实现结果（落地实测）

- `questions.json`：**8.47 MB → 1.57 MB**（5.4×），1820 题不丢，273 个簇（65 韵组桶 × ~4 平仄型），最大簇 91 句。
- 干扰项 **95% 同平仄型**（簇内优先），不足 3 个时才扩到同韵组桶（~5%）；平水韵同韵部 50%。
- 关键实现取舍（比原草案更省改动）：
  - `Question.Distractors` 改为 `[JsonIgnore]` 的**运行时字段**，不进 JSON；`ContentService` 在建会话时从簇填充。
    因此 **`QuizService.Prepare` / `QuizScreen` 零改动**（仍是"从 q.Distractors 随机抽 3"）。
  - **记录/错题本无需迁移**：`QuestionResult` 本就不存选项，错题本按 id 回查 → `GetQuestionsByIds` 即时填充干扰项。
  - 出题**只用簇**：`BuildRuntimeQuestions` 不再现场构造 `QuestionGenerator`，改为筛选已加载的轻量题 + `PopulateDistractors`。
  - 簇的"viability"用**松桶 (字数,韵组)** 判定（≥3），保证题量与 v1 一致；平仄型只影响**采样偏好**，不减题。
  - `IContentSource.LoadQuestionsAsync` → `LoadQuestionBankAsync()`（返回含 clusters 的 `QuestionFile`）。
- 涉及文件：`Question.cs`/`ContentFiles.cs`（模型）、`ToneService.cs`（`ToneType`）、`QuestionGenerator.cs`（`BuildBank`/`NearDuplicate`）、
  `JsonContentSource.cs`/`IContentSource.cs`、`ContentService.cs`、`ContentToolWindow.cs`、`Tools/TestHarness/Program.cs`。
- 校验：TestHarness **147 passed / 0 failed**（含簇一致性、运行时 3..20 干扰项、95% 同平仄型、采样随机性、JSON 不再内嵌 distractors）。

---

> 以下为原始设计草案，保留作背景。

---

## 1. 背景与动机

### 现状（schema v1）
每道题把自己的干扰项**整句内嵌**进 `distractors` 数组（当前为 3~20 句的池，出题时随机抽 3 个）。

实测冗余（1820 题）：

| 指标 | 数值 |
|---|---|
| `questions.json` 体积 | **8.47 MB** |
| 内嵌干扰项对象总数（含重复） | **33,279** |
| 去重后不同诗句数 | **2,380** |
| 冗余倍数 | **≈14×** |

同一句诗（如某个常见 7 言平韵句）会被复制进几十道题里，每份都带全套 `charCount/lastChar/rhymeFinal/rhymeGroup/pingshui/sourcePoemId` 字段。

### 目标
1. **去冗余**：同一句只存一份。
2. **干扰项池可超过 20**，不再为每题穷举、内嵌候选。
3. 干扰项质量从"逐题计算"上移到"逐簇定义"：**相同韵脚 + 相近平仄**的诗句归为一组，组内互为合格干扰项。

---

## 2. 关键设计判断

### 2.1 「相同韵脚」已经天然成组
按 `(字数, 韵组)` 给所有诗句分桶 = 现有 `QuestionGenerator.Key(charCount, rhymeGroup)`。实测 **65 个桶**，分布：

| 桶大小 | 桶数 |
|---|---|
| <5 | 1 |
| 5–20 | 31 |
| 21–50 | 15 |
| 51–100 | 15 |
| 100+ | 3（最大 182） |

### 2.2 「相近平仄」需新增标注
逐行 `tone`（平仄串）字段当前 **0/2453 全空** —— 从未标注。`ToneService`（逐词填空已在用）能算逐字平仄，可补。

聚类用**粗粒度律句型**而非精确平仄串：
- 近体诗格律只看 **2/4/6 字位 + 韵脚**的平仄。
- 把一句压成 `toneType`，例如四类：`平起平收 / 仄起平收 / 平起仄收 / 仄起仄收`（"起"= 第 2 字平仄，"收"= 韵脚平仄）。
- **理由**：若按精确平仄串分簇，桶会碎成大量 1~2 句的小簇，反而凑不齐 3 个干扰项。粗粒度保证簇足够大。

### 2.3 ⚠️「对偶/对仗」不能进簇键
- 韵脚、平仄型是**等价关系** → 能分等价类（簇）。
- 对仗是**成对关系且不传递**：A 对 B、B 对 C ⇏ A 对 C。
- 而且填空题"对仗工整"是相对**被挖那句的对句**而言的——是**每题各自**的属性，不是可全局共享的簇属性。
- **结论**：对仗作为**出题时的采样排序信号**（从簇里挑 3 个时，优先与本题对句平仄/词性更相称者），不进簇身份。

### 2.4 选定的簇粒度（已确认）
**簇键 = (字数, 韵组, 平仄型)**。

---

## 3. 目标数据结构（schema v2）

`questions.json`：

```jsonc
{
  "schemaVersion": 2,
  "clusters": [
    {
      "id": 0,
      "charCount": 7,
      "rhymeGroup": "10",
      "toneType": "仄起平收",
      "lines": [
        { "text": "…", "lastChar": "…", "rhymeFinal": "uang", "pingshui": "P22阳",
          "tone": "平平仄仄平平仄", "sourcePoemId": "…", "difficulty": 2 }
        // 可 >20 句；每句只在此存一份
      ]
    }
    // …每个 (字数,韵组,平仄型) 一个簇
  ],
  "questions": [
    {
      "id": "q-…", "poemId": "…", "blankLineIndex": 3,
      "clusterId": 0,            // 指向所属簇
      "correctText": "…",        // 正确句（也是 cluster.lines 的成员）
      "difficulty": 2, "explanation": "", "sourceMode": "corpus"
    }
    // 题目不再内嵌 distractors
  ]
}
```

要点：
- 正确句本身是其簇的成员；`correctText` 用于运行时从簇里排除它。
- `lines[].difficulty` 保留，使运行时仍能"按难度档过滤干扰项"（见 §5 取舍）。
- `tone` 整串保留（供采样时算"与对句平仄贴合度"），`toneType` 是其粗粒度聚类键。

---

## 4. 运行时出题（QuizService.Prepare 改造）

```
输入：一道 v2 题
1. 由 clusterId 取出簇
2. 候选 = 簇.lines.Where(text != correctText)
         [可选] .Where(difficulty <= 所选最高档)
3. 排序信号（强→弱）：
     a. 与本题对句（被挖句的 couplet partner，若有）平仄/词性更相称
     b. 同平水韵韵部（pingshui 相同）
     c. 与正确句字面重合度低（避免太像）
4. 取前 K 个（如 K=8）做候选窗 → shuffle → 抽 3
5. 正确句 + 3 干扰项 → shuffle → 记录 CorrectIndex
```

每次 `Prepare` 仍是"从池里随机抽 3"，与当前行为一致，只是池来自簇而非内嵌数组。

---

## 5. 影响范围与改动清单

| 模块 | 改动 |
|---|---|
| `Data/Models/Question.cs` | 题目去掉 `Distractors`，加 `ClusterId` + `CorrectText`（或保留 `Correct` 仅存文本+元数据）。新增 `LineCluster` / `ClusterLine` 模型。 |
| `Data/Models`（新增） | `QuestionClusterFile`（clusters + questions 根对象）。 |
| `Services/Content/QuestionGenerator.cs` | 产出改为：先按 (字数,韵组,平仄型) 建簇 → 再为每个可挖句生成"指向簇"的轻量题；丢弃逐题 `SelectDistractors` 的内嵌输出（评分逻辑迁去采样阶段）。 |
| `Services/Rhyme/`（标注） | 新增逐行 `tone` + `toneType` 标注（用 `ToneService`）。`PoemLine` 增 `ToneType` 字段。 |
| `Services/Quiz/QuizService.cs` | `Prepare` 从簇采样 3 个（§4）。需要能拿到簇 → 注入 `ContentService` 或在 `Question` 上挂簇引用。 |
| `Services/Content/ContentService.cs` | 加载 clusters，建 `clusterId → cluster` 索引；`BuildRuntimeQuestions` 适配；难度过滤改在簇内行级别。 |
| `Data`（加载） | `JsonContentSource` 解析 v2；保留对 v1 的兼容读取或一次性迁移。 |
| `Editor/ContentToolWindow.cs` | 校验改为：每簇 ≥? 句、每题 clusterId 有效、correctText ∈ 簇、簇内同字数同韵组同平仄型。 |
| `Tools/TestHarness/Program.cs` | 生成簇 + 题；断言：簇内一致性、每题候选（簇−正确句）≥3、采样输出 4 选项且仅 1 正确、运行时难度过滤仍生效。 |
| `Records / WrongBook`（错题本/记录） | **重要**：现在记录里若内嵌了整题快照，要改存 `clusterId + correctText`，回放时重建。需核对 `QuestionResult`/记录仓储。 |

---

## 6. 取舍与风险

1. **难度分层干扰项**（现有 `tier-0` 测试："干扰项只来自 ≤ 所选难度档的诗"）：簇模型要在 `lines[].difficulty` 上做行级过滤才能保留此行为。已纳入 schema。
2. **簇过小**：某些 (字数,韵组,平仄型) 组合可能 <4 句 → 凑不齐 3 干扰项。对策：平仄型用粗粒度（§2.2）；仍不足时**回退到同 (字数,韵组) 的相邻平仄型**采样（簇可声明"回退伙伴簇"，或运行时按 (字数,韵组) 二级桶兜底）。
3. **错题本回放兼容**：旧记录若内嵌 v1 题，需迁移或双读。改动前务必盘点记录仓储里存了什么。
4. **对仗信号要等平仄/词性标注**：`posPattern` 当前也全空。§4 的排序信号 a 需要逐行词性或至少平仄对立判断；首版可只用 b/c，对仗作为后续增强。
5. **确定性**：生成与采样都走种子化 `IRandomSource`，保持现有可复现性。

---

## 7. 收益预估

- 干扰项存储：33,279 份 → **2,380 句各存一份**（约 14× → 1×）。
- `questions.json` 体积预计从 8.47 MB 降到 **~1–1.5 MB** 量级（题目变轻 + 簇去重）。
- 干扰项池天然可 **>20**（整簇），无需每题穷举。
- 干扰项质量集中在"建簇"一处定义，易调参、易审阅。

---

## 8. 建议实施顺序

1. 加逐行 `tone` + `toneType` 标注，先不改 schema，跑 TestHarness 看簇大小分布（验证粗粒度平仄型下没有大量过小簇）。
2. 定稿 v2 模型类 + 生成器产出簇/题。
3. 改 `QuizService.Prepare` 采样 + `ContentService` 簇索引。
4. 迁移校验（ContentToolWindow）+ TestHarness 断言。
5. 盘点并迁移错题本/记录回放。
6. 全量重生成 `questions.json`，对比体积与"每题候选≥3"覆盖率。
