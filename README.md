# Sokoban Studio / 推箱子工坊

一个面向关卡设计者的推箱子内容生产工具集：

制作 → 校验 → 分析 → 解法预览 → 一键试玩 → 迭代

<!-- AUTO:HERO_RUNTIME:START -->
![游戏实机](Docs/Screenshots/runtime-gameplay.png)
<!-- AUTO:HERO_RUNTIME:END -->

<!-- AUTO:HERO_DASHBOARD:START -->
![内容总览](Docs/Screenshots/editor-dashboard.png)
<!-- AUTO:HERO_DASHBOARD:END -->

<!-- AUTO:HERO_EDITOR:START -->
![关卡编辑器与解法预览](Docs/Screenshots/editor-solution-preview.png)
<!-- AUTO:HERO_EDITOR:END -->

本工具集包含完整的推箱子玩法，以及面向设计师的关卡制作工作流——**制作 → 校验 → 保存 → 一键试玩**——它与正式发行的游戏使用同一套运行时规则。

权威规则系统是确定性的整数网格 `Board`：`TryMove` 决定每一次移动与推箱，物理从不决定推箱的合法性。运行时代码从不依赖 `UnityEditor` 或 `AssetDatabase`，已发行关卡通过显式序列化的 `LevelCatalog` 加载而非资源发现。针对 Unity **2022.3.62f3**（精确版本）构建。

## 为什么做这个项目

- **一套规则系统，两类使用者。** 正式游戏与关卡编辑器运行同一套确定性的 `Board` 规则，因此一个在编辑器中通过校验并试玩通过的关卡，在运行时行为完全一致。
- **制作闭环得以闭合。** 制作 → 校验 → 保存 → 一键试玩，并提供可跨域重载（domain reload）安全传递的试玩交接，同时覆盖不属于已发行目录的关卡。
- **面向设计师安全性的工具，而不只是瓦片绘制器。** 阻断性错误会拦截保存/试玩；静态死角死锁警告与「有门无压力板」提示属于建议性；分析器如实报告 `SOLVABLE / UNSOLVABLE / INCONCLUSIVE`；目录审计会报告每个已发行槽位的健康状况。
- **验证纪律。** 分层的 `Fast / Gate / Release` 门禁，PlayMode 测试真正接入门禁，并且该接入以一个刻意失败的探针在正反两个方向上都证明了并非空转（证据已提交）。

## 如何游玩

1. 使用 Unity **2022.3.62f3** 打开项目。
2. 打开 `Assets/Sokoban/Scenes/MainMenu.unity`。
3. 按下 **Play**。
4. 依次进入 **Menu → Level Select → Gameplay**。

将每个箱子推到目标点上即可完成关卡。当棋盘完成时，**Next** 操作会前进到下一个已发行关卡。

**操作**

| 输入 | 动作 |
| --- | --- |
| 方向键 / WASD | 移动（并推箱） |
| Z | 撤销 |
| R | 重新开始 |

<!-- AUTO:RUNTIME_COMPLETE:START -->
![关卡完成](Docs/Screenshots/runtime-complete.png)
<!-- AUTO:RUNTIME_COMPLETE:END -->

**八个已发行关卡**

下表中的每个最短解都经分析器验证（针对真实的运行时 `Board` 规则运行）；数值来自目录审计日志（无头 `SmokeTest` 目录审计，可通过下文的 smoke 命令重新运行）。

| 关卡 | 网格 | 箱子 | 最短解 | 设计意图 |
| --- | --- | --- | --- | --- |
| **L01** 基础推动 | 7×6 | 2 | 10 moves / 4 pushes | 移动与推箱的入门。 |
| **L02** 墙角陷阱 | 8×7 | 2 | 9 moves / 5 pushes | 你不能拉箱子：需要绕到箱子的另一侧，并注意死角危险。 |
| **L03** 推箱顺序 | 7×7 | 2 | 13 moves / 3 pushes | 推箱顺序很重要。 |
| **L04** 空间规划 | 8×8 | 2 | 15 moves / 4 pushes | 围绕内部墙带进行空间规划。 |
| **L05** 压力板与门 | 9×6 | 2 | 16 moves / 4 pushes | 压力板 + 门的引入：必须用箱子压住压力板，才能打开通往第二个目标的唯一一扇门。 |
| **L06** 持续触发 | 10×6 | 2 | 23 moves / 8 pushes | 组合终章：一次压力板往返、一扇棋盘中部活门，以及一个需要双箱规划的房间。 |
| **L07** 机关组合 | 9×7 | 2 | 13 moves / 6 pushes | 箱子常驻压力板：一个箱子停在压力板上保持门开启，同时把第二个箱子推过门。 |
| **L08** 综合挑战 | 10×7 | 2 | 22 moves / 7 pushes | 终章：顺序、压力板/门与空间规划——绕行以激活压力板，穿过门，然后把两个箱子都送回目标点。 |

目录审计报告 8 个条目，8 个有效，0 个错误，0 个警告，0 个不可解，0 个不确定。

## 制作流水线

打开 **Tools → Sokoban → Level Editor**。

<!-- AUTO:EDITOR_LEVEL_EDITOR:START -->
![关卡编辑器](Docs/Screenshots/editor-level-editor.png)
<!-- AUTO:EDITOR_LEVEL_EDITOR:END -->

1. **制作（New）。** 按下 **New** 获得一份全新的工作副本，或指定一个已有的 `LevelDefinition` 资源并按下 **Load**。
2. **校验（Validate）。** 编辑器实时校验并按严重程度对问题分组。阻断性错误会禁用保存/试玩；警告为建议性。
3. **保存（Save）。** **Save** / **Save As** 将工作副本写入 `Assets/Sokoban/Levels/` 下的 `.asset` 文件。
4. **试玩（Playtest）。** 一次点击即可校验、保存，并使用与已发行玩法相同的运行时加载器，在工作关卡上进入 Play Mode。

**编辑器工具集**

- 笔刷：Wall、Floor、Goal、Player、Box、Erase、Plate、Door（在网格上点击或拖拽）。
- **Apply Resize** 用于更改网格尺寸。
- 按严重程度分组的校验（错误列表 + 警告列表），警告单元格在网格中高亮显示；点击某一行可将网格导航到对应单元格。
- **Undo / Redo**（Ctrl+Z / Ctrl+Y）作用于工作副本的编辑历史；**Ctrl+S** 保存，**Ctrl+Enter** 试玩。
- **Analyze Solvability** 按钮（见下文）。
- **Compare with saved** 展示工作副本相对已保存资源的指标。
- 当仍有阻断性错误时，**Playtest** 被禁用。

**配套窗口**

- **Content Dashboard**（`Tools → Sokoban → Content Dashboard`）：一个只读的、覆盖整个目录的健康状况表——每个关卡的校验结果，加上按需运行的有界求解判定——由与无头冒烟测试相同的审计逻辑支撑。
- **Solution Preview**（`Tools → Sokoban → Solution Preview`）：分析一个关卡，并在一个独立棋盘上回放其最短解，绝不修改资源或工作副本。

**校验规则**

阻断性**错误**（这些会拦截保存/试玩）：

- 有且仅有一个玩家；
- 至少一个箱子与至少一个目标点；
- 箱子数量必须等于目标点数量；
- 任何占据者不得位于墙上；
- 任何占据者不得位于门上（初始状态）；
- 网格格式良好（单元格与占据者数量与宽 × 高一致）。

建议性**警告**（绝不拦截保存/试玩）：

- 静态死角死锁——一个位于非目标格、且有两个正交方向的墙/边界邻居的箱子，报告时会给出单元格坐标并在网格中高亮；
- 有门却没有压力板——因为没有压力板组需要满足，所以该门保持开启。

`LevelValidator` 是作者流程/试玩流程共享的校验路径。运行时 `Board` 在加载时独立强制同样的不变量——其构造函数在非法状态下抛出异常——因此两者互相吻合，而不是其中任何一方充当「唯一真相来源」。

**破坏性开发夹具**

`Sokoban/Development/Build All Content And Scenes (Destructive)` 仅是一个受保护的开发夹具。因为它会删除并重新生成资源，所以会请求确认；它用于引导与测试——它会重新生成 Level01–04 以及目录和场景。已发行的 `LevelDefinition` 资源才是权威内容，在作者化之后未被重新生成。

**一键试玩桥**

编辑器会预置一个持久化的 `PlaytestRequest` `ScriptableObject`，并在进入 Play Mode 之前保存它，因此该请求能挺过 Play Mode 的域重载；`GameBootstrap` 在运行时消费它，并清除其内存引用。随后一个 `ExitedPlayMode` 清理器会清除磁盘上的资源，以免过期请求泄漏到之后一次正常的 Play 中。由于该请求是一个已保存的资源而非静态字段，非目录内的测试关卡也能试玩，而不会触碰已发行目录。

## 可扩展性案例研究：压力板 + 门

该机制被端到端地加入，以证明流水线能承载新规则，而不只是新瓦片。其规则契约：

- `Plate` 与 `Door` 是瓦片类型，带 A/B 组标识；编辑器调色板提供 压力板 A/门 A/压力板 B/门 B。
- 某一组的所有压力板被占压（玩家或箱子）时，打开该组的所有门；不同组互不影响。
- 没有压力板的组，其门保持开启。
- 关闭的门阻挡玩家移动与箱子落位。
- 门在组重新关闭时若被占据，则保持开启，直到占据者离开。
- 门状态是**派生**的、从不存储：每次移动/撤销/重新开后按查询重新计算；`BoardSnapshot` 不携带门状态。

最后一点是让该特性保持真实的设计决策：因为门状态是玩家与箱子格子的纯函数，撤销、重新开始以及分析器的已访问状态集都能自动保持正确。`Board` 通过与其他一切相同的 `TryMove` 来拦截移动；编辑器调色板、校验器（有门无压力板提示）、分析器（关闭的门会拒绝移动，因为它遍历真实的 `Board` 状态）以及视图（门颜色从棋盘重新读取，从不缓存）全都通过既有流水线接入了该机制。

## 分析器

`LevelAnalyzer` 是一个仅编辑器可用、有界的广度优先搜索，遍历真实的 `Board` 状态。它复用一个从不可变快照按每个节点恢复的单一 `Board`，绝不修改传给它的关卡，也绝不拦截保存或试玩。

判定语义：

| 判定 | 含义 |
| --- | --- |
| **SOLVABLE** | 在预算内找到解；报告最短移动解及其移动/推箱次数（外加探索的状态数与耗时）。 |
| **UNSOLVABLE** | 在预算内穷尽探索了可达状态空间，且不存在目标状态。只有完成穷尽搜索才可能产生此结果。 |
| **INCONCLUSIVE** | 在得出任一结论之前触发了某个预算（状态数或墙钟时间）。预算耗尽**绝不**被报告为不可解。 |

默认预算：**200,000 个状态 / 10 秒**。

搜索以玩家所在格加上排序后的箱子格来为一个状态建立键。该键是完备的，因为门状态只取决于压力板占用情况，而压力板占用情况又只取决于玩家与箱子格——所以两个拥有相同键的状态具有完全相同的合法移动。`INCONCLUSIVE` 的存在是为了让工具从不说谎：在一个超出预算的大关卡上，分析器会表示它不知道，而不是猜测「不可解」。

## 架构

```
Assets/Sokoban/
  Runtime/   权威棋盘规则、游戏流程与 OnGUI UI
             (Board、GameBootstrap/GameController、BoardView、
              MainMenu/LevelSelect 控制器、LevelDefinition/LevelCatalog、
              PlaytestRequest)
  Editor/    关卡编辑器、校验器、分析器、目录审计、试玩
             启动器 + 清理器、内容/场景工厂、无头冒烟测试
  Tests/     EditMode + PlayMode 测试套件
  Levels/    Level01–08 + LevelCatalog
  Scenes/    MainMenu、LevelSelect、Gameplay（已注册到构建设置）
```

数据流：作者化的 `LevelDefinition` 资源 → 显式的 `LevelCatalog` → 运行时（`GameBootstrap` 为每个关卡构建一个 `Board`；`GameController` 驱动输入与完成；`BoardView` 只负责渲染）。编辑器通过 `LevelValidator` 与 `LevelAnalyzer` 镜像同一套规则，因此作者化与游玩保持一致。

## 验证与证据

规范入口：

```powershell
.\Scripts\Agent\Verify.ps1 -Tier Fast
.\Scripts\Agent\Verify.ps1 -Tier Gate
.\Scripts\Agent\Verify.ps1 -Tier Release
```

| 层级 | 作用 | 当前证据 |
| --- | --- | --- |
| **Fast** | 断言项目版本为 2022.3.62f3，以批处理模式启动 Unity 以强制脚本导入/编译，并在出现编译器错误时失败。 | 编译干净。 |
| **Gate** | 运行 Fast，随后运行 EditMode 测试，再运行 PlayMode 测试（当 PlayMode 目录存在时），解析 Unity Test Framework XML。 | EditMode **190/190 passed**，PlayMode **4/4 passed**。 |
| **Release** | 运行 Gate，随后构建 Windows x64 独立版本并要求产物存在。 | 基线为绿；产物 `Build/Sokoban Studio.exe`。 |

一个无头冒烟检查直接运行相同的内容路径：

```powershell
Unity.exe -batchmode -quit -projectPath <repo> -executeMethod Sokoban.Editor.SmokeTest.Run
```

它以 0 退出并报告 `SMOKE OK ... catalog=8`。

门禁的 PlayMode 接线被证明是非空转的，而非想当然：一个刻意失败的 PlayMode 探针会把门禁翻转为 FAIL（退出码 1），移除该探针后又将其恢复为 PASS（退出码 0）。

## 已知限制

- **OnGUI 原型级呈现。** 未使用 UIElements，除视图侧的移动缓动外没有动画打磨（棋盘保持即时权威；缓动仅限视图）。
- **音频为程序化合成。** 8 类提示音（UI 点击 / 移动 / 推箱 / 撤销 / 门打开 / 门关闭 / 箱子到达目标 / 关卡完成）在内存中合成（`AudioClip.Create`），无外部素材、无版权风险，按 `M` 键静音。
- **八个已发行关卡。**
- Windows x64 构建已通过无头方式验证；手动启动并操作 `Build/Sokoban Studio.exe` 是一个人工 QA 步骤，不在门禁覆盖范围内。
- **分析器仅编辑器可用且有预算上限**——足够大的关卡可能返回 `INCONCLUSIVE`。这是刻意的：它绝不把预算耗尽报告为不可解。
- **压力板与门支持 A/B 两组。** 组内独立联动（组内压力板全部被占压时打开该组门；无压力板的组保持开门）。

## 下一步计划

- 更多关卡，在加入目录之前先经过校验与分析器检查。
- 更丰富的呈现：动画精灵与屏幕过渡。

## 过程说明

项目以测试先行的纪律迭代构建：规则变更在通过之前先由 EditMode 测试钉住，生命周期变更先由 PlayMode 测试钉住。上文每一项玩法断言都由自动化测试或已提交的日志作为支撑，而非依靠人工检查。范围被刻意控制在经过验证的小特性集上，而不是追求广度。
