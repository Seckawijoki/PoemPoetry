# ChinesePoetryImport — 借鉴 chinese-poetry 的离线数据导入

一次性数据准备脚本（**不参与 Unity / C# 构建**），把开源库的结构化数据加工成本项目的出厂资产，
服务于出题算法优化（计划 234-2：#2 平水韵细分 + 声律启蒙语义充实）。需要 Python 3 + 联网（或本地缓存输入）。

## 脚本

### `build_pingshui.py` → `Assets/StreamingAssets/PoemData/pingshui_rhyme.json`
平水韵字表（`字 → 韵部id[]`），供选择题"韵脚细分"干扰项打分使用
（`RhymeService.PingshuiForChar` 标注到 `PoemLine.PingshuiRhyme`，`QuestionGenerator.Score` 顶层加分）。

- 数据源：`LingDong-/cope` 的 `data/rhymebooks.json`（key `平水韵`，已是简体）。
- 韵部 id 自带含义、由数据派生：`<P|Z><两位序号><韵目首字>`，如 `东→P01东`、`删→P15删`、`董→Z01董`；
  多音字映射多个韵部（如 `间→[P15删, Z45谏]`）。
- 当前语料韵脚覆盖率 ≈ 98.7%；表外字 `PingshuiRhyme=""`，打分自动回退到拼音韵母（新韵），优雅降级。

```
python build_pingshui.py                 # 从 CDN 抓取
python build_pingshui.py rhymebooks.json # 用本地副本
```

### `build_categories.py` → `Assets/StreamingAssets/PoemData/semantic_categories.json`
用声律启蒙词汇充实语义类别（直接提升逐字填空 `WordClozeGenerator` 的"同类同平仄"干扰层），
并新增 `天文` / `地理` 两类。

- 数据源：`chinese-poetry` 的 `蒙学/shenglvqimeng.json`（繁体）+ `cope` 的 `data/TC2SC.json`（繁→简）。
- 候选字按声律启蒙/对韵传统人工筛选，但**只保留实际出现在声律启蒙中的字**（繁→简后校验），
  未见者丢弃并打印；已属其他类别的字跳过（一字一类）。
- 新增类别后需同步 `WordClozeGenerator.CategoryPriority`（已含 `天文`、`地理`）。

```
python build_categories.py                              # 从 CDN 抓取
python build_categories.py shenglvqimeng.json TC2SC.json # 用本地副本
```

## 刷新流程

1. 运行上面两个脚本，更新 `StreamingAssets/PoemData/` 下的资产。
2. 运行 `Tools/TestHarness/build_and_test.ps1` 重跑标注/出题流水线并验证（含平水韵/体裁断言）。

## 来源与许可

- chinese-poetry: https://github.com/chinese-poetry/chinese-poetry
- LingDong-/cope（平水韵表、繁简表）: https://github.com/LingDong-/cope

本目录只提交我方派生产物与脚本；原始第三方文件由脚本按需从 CDN 拉取，未随仓库分发。
如需重新分发原始数据，请核对其各自许可证。
