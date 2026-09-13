# Raft Piano Reborn

Raft Piano Reborn 是由 Xiaohulang 制作的《Raft》扩展钢琴 Mod。项目重写了原版钢琴的输入和发声部分，在游戏内提供完整 88 键采样钢琴、和弦演奏、七层音域、延音踏板、力度控制和可编辑键位。

当前版本：**1.0**

## 主要功能

- 完整覆盖标准钢琴 A0–C8 的 88 个音。
- 基准层加六个瞬时音域修饰层，共七层可用音域。
- 支持同时按下多个琴键演奏和弦。
- 支持延音踏板和按键松开后的自然尾音。
- 空闲回收保护：仅在全部松键、松踏板且输出连续两秒低于 −120 dBFS 后，清理未结束的极低电平声部。
- 支持 8–127 的演奏力度调节，默认力度为 96。
- 使用独立原生输入状态机和带时间戳的音频事件队列。
- 使用 Salamander Grand Piano 采样和 sfizz 音源引擎。
- 键位通过 `KeyBindings.json` 修改。

## 安装

1. 安装并能够正常使用 Raft Mod Loader。
2. 完全退出《Raft》。
3. 将真实文件 `RaftPianoReborn-1.0.1.rmod` 放进《Raft》安装目录下的 `mods` 文件夹。升级时先将旧版 `.rmod` 移出该文件夹，不要同时加载多个版本。
4. 启动游戏并通过 Raft Mod Loader 加载本 Mod。
5. 首次加载需要释放并载入 641 个钢琴采样。等待加载完成后再坐到钢琴前演奏。



## 默认演奏方式

基准音从 MIDI 48（C3）开始。字母、数字和标点键按照钢琴黑白键关系排列。以下是完整的内置默认映射；同一行出现两个按键时，两者会发出相同的音。

### 基准层琴键

| MIDI | 音名 | 默认按键 | 备用按键 |
|---:|---|---|---|
| 48 | C3 | `Z` | — |
| 49 | C♯3 | `S` | — |
| 50 | D3 | `X` | — |
| 51 | D♯3 | `D` | — |
| 52 | E3 | `C` | — |
| 53 | F3 | `V` | — |
| 54 | F♯3 | `G` | — |
| 55 | G3 | `B` | — |
| 56 | G♯3 | `H` | — |
| 57 | A3 | `N` | — |
| 58 | A♯3 | `J` | — |
| 59 | B3 | `M` | — |
| 60 | C4 | `Q` | `,`（`Comma`） |
| 61 | C♯4 | `2` | `L` |
| 62 | D4 | `W` | `.`（`Period`） |
| 63 | D♯4 | `3` | `;`（`Semicolon`） |
| 64 | E4 | `4` | `/`（`Slash`） |
| 65 | F4 | `R` | — |
| 66 | F♯4 | `5` | — |
| 67 | G4 | `T` | — |
| 68 | G♯4 | `6` | — |
| 69 | A4 | `Y` | — |
| 70 | A♯4 | `7` | — |
| 71 | B4 | `U` | — |
| 72 | C5 | `I` | — |
| 73 | C♯5 | `9` | — |
| 74 | D5 | `O` | — |
| 75 | D♯5 | `0` | — |
| 76 | E5 | `P` | — |
| 77 | F5 | `[`（`LeftBracket`） | — |
| 78 | F♯5 | `=`（`Equals`） | — |
| 79 | G5 | `]`（`RightBracket`） | — |

`KeyBindings.json` 中使用的是表格括号里的英文键名。字母键不区分大小写。

### 音域修饰键

| 按键 | 音域变化 |
|---|---:|
| `Tab` | -3 个八度 |
| `CapsLock` | -2 个八度 |
| `LeftShift` | -1 个八度 |
| 不按修饰键 | 基准层 |
| `Backslash` | +1 个八度 |
| `Enter` | +2 个八度 |
| `RightShift` | +3 个八度 |

使用时先按住一个音域修饰键，再按琴键。为了得到确定的结果，同一音符不要同时使用多个音域修饰键。已经发出的音不会因为中途松开修饰键而改变音高。

### 控制键

| 按键 | 功能 |
|---|---|
| `Space` | 延音踏板，按住为踩下，松开为抬起 |
| `Up` | 力度增加 8，最高 127 |
| `Down` | 力度降低 8，最低 8 |
| `Home` | 释放当前音符并把力度恢复到 96 |
| `E` | 使用游戏原有交互离开钢琴 |

## 修改键位

第一次成功加载后，Mod 会生成：

```text
<Raft 游戏目录>\mods\ModData\raft-piano-reborn\KeyBindings.json
```

修改方法：

1. 退出游戏。
2. 使用记事本等纯文本编辑器打开 `KeyBindings.json`。
3. 修改需要更换的按键名称。
4. 保存文件。
5. 重新加载 Mod 或完全重启游戏。

配置不会在游戏运行中自动刷新。同一个物理键不能被重复分配，`notes` 必须继续覆盖 0–31 的全部半音。配置无效时，Mod 会安全回退到内置默认键位，并在本地会话日志中记录原因，不会覆盖玩家修改过的配置。

例如，将方向键功能交换：

```json
"velocityUp": "Down",
"velocityDown": "Up"
```

重新加载后，`Down` 会增加力度，`Up` 会降低力度。

## 项目结构

```text
RaftPianoReborn-1.0.1
├─ assets
│  └─ piano
│     ├─ Data
│     ├─ Samples
│     └─ Salamander.sfz
├─ build
│  ├─ Build-All.ps1
│  ├─ Build-Native.cmd
│  └─ Build-Package.ps1
├─ prebuilt
│  └─ RaftPianoRebornCore.dll
├─ src
│  ├─ managed
│  └─ native
├─ third_party
│  ├─ salamander
│  └─ sfizz
├─ KeyBindings.example.json
├─ LICENSE.txt
├─ THIRD-PARTY-NOTICES.md
└─ MANIFEST.sha256
```

### `assets/piano`

保存运行时钢琴资源：

- `Samples`：641 个 FLAC 钢琴采样。
- `Salamander.sfz`：采样、音高、力度层、尾音和踏板之间的映射。
- `Data`：SFZ 使用的辅助映射数据。

### `src/managed`

负责与《Raft》和 Raft Mod Loader 交互的 C# 代码：

- `RaftPianoRebornMod.cs`：Mod 入口、加载、资源释放、状态和退出清理。
- `KeyboardBindings.cs`：默认键位、JSON 配置读取、合法性检查和虚拟键转换。
- `InputGate.cs`：判断玩家是否坐在钢琴前、窗口是否聚焦以及菜单状态。
- `NativeAudio.cs`：C# 与原生钢琴核心之间的调用接口。
- `AudioOutput.cs`：把原生渲染结果接入游戏的 FMOD 音频系统。
- `PlayingMotion.cs`：控制手部演奏动画开始和停止。
- `modinfo.json`：Mod 名称、作者、版本、游戏版本和许可信息。

### `src/native`

负责实时演奏的 C++ 原生核心：

- `PianoCore.cpp`
- `PianoCore.h`
- `QuietVoiceReclaimer.h`：空闲声部回收条件，不改变正常渲染样本。

它负责键盘状态、88 键音高换算、复音、和弦、音域、力度、延音、事件时间戳和 sfizz 渲染调度。

### `third_party`

保存构建和运行需要的第三方材料：

- `salamander`：Salamander Grand Piano 的原始说明和 CC BY 3.0 许可证。
- `sfizz`：sfizz 1.2.3 的 DLL、头文件、导入库和 BSD 2-Clause 许可证。

详细署名、来源和修改记录见 `THIRD-PARTY-NOTICES.md`。

### `prebuilt`

保存已经编译好的 `RaftPianoRebornCore.dll`。如果只修改 C# 代码、配置或文档，可以使用这个文件重新打包，无需再次编译 C++。

该 DLL 已经包含在正式 `.rmod` 中，普通玩家不需要单独安装。

### `build`

- `Build-Native.cmd`：编译 C++ 原生核心。
- `Build-Package.ps1`：验证资源并生成 `.rmod`。
- `Build-All.ps1`：先编译原生核心，再完成打包。

构建过程中会自动生成：

- `build/out`：中间文件和临时打包目录。
- `dist/RaftPianoReborn-1.0.1.rmod`：最终 Mod 文件。

## 重新构建

只使用已经提供的原生核心重新打包：

```powershell
powershell -ExecutionPolicy Bypass -File build\Build-Package.ps1 -UsePrebuilt
```

重新编译 C++ 核心并打包：

1. 安装带有“使用 C++ 的桌面开发”组件的 Visual Studio 2022。
2. 打开 `x64 Native Tools Command Prompt for VS 2022`。
3. 在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File build\Build-All.ps1
```

## 许可证

Xiaohulang 原创的代码和项目材料依照 **PolyForm Noncommercial License 1.0.0** 提供。允许按照该许可证进行非商业使用、研究、复制、修改和再分发；商业使用必须事先取得 Xiaohulang 的单独书面授权。

由于限制商业使用，本项目应称为“源码公开的非商业项目”，而不是 OSI 定义下的开源软件。

第三方材料保持原许可证：

- Salamander Grand Piano v3：CC BY 3.0。
- sfizz 1.2.3：BSD 2-Clause。

请阅读 `LICENSE.txt` 和 `THIRD-PARTY-NOTICES.md`。第三方许可证的完整原文保存在相应 `third_party` 目录中。

## 作者

Raft Piano Reborn 原创集成与实现：**Xiaohulang**
