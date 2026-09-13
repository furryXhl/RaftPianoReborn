# Raft Piano Reborn 第三方材料声明

Raft Piano Reborn 1.0.1 同时包含原创代码和第三方材料。本文件用于明确第三方材料的作者、来源、许可证、在项目中的位置以及我们所做的修改。

项目自身的非商业许可不会替代或限制下列第三方许可证。使用者也不能仅根据 Raft Piano Reborn 的项目许可推断自己获得了第三方材料之外的权利。

## 1. Salamander Grand Piano v3

- 钢琴录音作者：Alexander Holm
- 乐器：Yamaha C5 三角钢琴
- 录音规格：48 kHz / 24-bit，16 个力度层
- SFZ 重映射：kinwie
- Retuned 版本：Markus Fiedler
- 上游项目：https://github.com/sfzinstruments/SalamanderGrandPiano
- 原始录音来源：https://archive.org/details/SalamanderGrandPianoV3
- 许可证：Creative Commons Attribution 3.0 Unported（CC BY 3.0）
- 许可证网址：https://creativecommons.org/licenses/by/3.0/

在本项目中的位置：

- `assets/piano/`
- `.rmod` 内的 `Data`、`Samples` 和 `Salamander.sfz` 资源
- `third_party/salamander/`


## 2. sfizz 1.2.3

- 项目：sfizz
- 版权所有者：sfizz contributors
- 上游项目：https://github.com/sfztools/sfizz/tree/1.2.3
- 许可证：BSD 2-Clause License

在本项目中的位置：

- `third_party/sfizz/bin/sfizz.dll`
- `third_party/sfizz/include/`
- `third_party/sfizz/lib/sfizz.lib`
- `.rmod` 内封装的 `sfizz.dll`


BSD 2-Clause 的完整许可证保存在：

`third_party/sfizz/LICENSE.txt`


## 3. Raft Piano Reborn 原创部分

以下部分属于 Raft Piano Reborn 的原创实现，并依照项目根目录 `LICENSE.txt` 中指定的 PolyForm Noncommercial License 1.0.0 提供：

- RML Mod 入口和生命周期管理
- 资源释放与完整性检查
- 可编辑键位配置读取器
- 原生键盘状态机和 88 键映射
- 带时间戳的事件队列
- 和弦、音域、力度和延音控制
- DSP 音频桥接和播放状态管理
- 手部演奏动画控制
