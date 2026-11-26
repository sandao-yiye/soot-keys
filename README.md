# 墨爪 · Soot Keys

墨爪是一只在奶油色小键盘后面蹲班的黑猫。它用前爪打字，左后脚踩空格，右后脚够回车。敲快了，四只白袜子一起上。

![墨爪收爪、踩空格、伸手够鼠标，再抬头看向四周的循环动画](preview/soot-keys.gif)

[下载 Windows 便携包](https://github.com/sandao-yiye/soot-keys/releases/latest/download/SootKeys-Windows.zip) · [查看版本记录](https://github.com/sandao-yiye/soot-keys/releases)

## 在 Codex 里选择墨爪

1. 下载并解压便携包，双击 `Install-CodexPet.cmd`。
2. 打开 Codex 的 **设置 → 智能伙伴 → 选择智能伙伴**，点 **墨爪**。部分界面的入口叫「迷你界面和智能伙伴」。如果已经打开过列表、暂时没看到新猫，完全退出后重新打开 Codex。
3. 打开桌面伙伴，即可看到 Codex 驱动的待机、工作等动作。支持 v2 图集的版本还可以让它朝指针方向看。

手动安装时，把 `pet` 里的 `pet.json` 和 `spritesheet.webp` 放在 `~/.codex/pets/soot-keys/`。设置过 `CODEX_HOME` 时，使用该目录下的 `pets/soot-keys/`。

## 让它跟着你敲键盘

在 Windows 10/11 上，双击解压出来的 **SootKeys.exe**。它会单独显示一只可拖动的透明桌宠。如果只想留一只，可以收起 Codex 自带的桌面伙伴。选择 Codex 伙伴不会自动启动这个程序。

| 你的动作 | 墨爪的动作 |
| --- | --- |
| 敲键盘左区或右区 | 伸出对应的前爪拍键，另一只爪子收起来 |
| 按空格 | 抬起前爪，用左后脚斜着踩空格 |
| 按回车 | 右后脚够到回车，顺便挤一下眼睛 |
| 快速连打 | 四爪抢着敲，回车键帽被弹飞，歪扣在两只耳朵中间，再翻回键盘 |
| 移动指针 | 金色眼睛一路追视，偶尔来个双连眨 |
| 点鼠标左键 | 向左侧探身，用白爪去够它自己的小鼠标 |

拖动猫可以摆放位置。右击猫或系统托盘图标，选 **整只猫跟随鼠标** 就能带着它走，也可以暂停互动或退出。程序使用 Windows Raw Input 将敲击归为左右键区、空格或回车，只在内存里驱动动作。互动程序可离线使用，不保存输入文本。

## 从源码构建

仓库包含可编辑的角色绘图、动作编排、逐帧素材、Codex 图集及 Windows 程序源码。已有帧可直接构建，Windows 端使用 .NET Framework 4.8 和 Windows PowerShell。

```powershell
# 编译便携程序到 desktop/dist/SootKeys.exe
.\desktop\build.ps1

# 自检并生成 dist/SootKeys-Windows.zip
.\Package-Windows.ps1
```

要改角色外观，编辑 `art/illustration.py`。要改姿势和动作幅度，编辑 `art/motion.py`，然后重新生成素材，再构建程序。

```sh
python -m pip install Pillow aggdraw
python art/build.py
```

代码和项目原创美术采用 [MIT 许可证](LICENSE)。
