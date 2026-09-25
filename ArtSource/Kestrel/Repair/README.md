# 红隼网格局部修复

输入：`../falcon in flight 3d model.glb`，原文件未修改，SHA-256 见 repair_validation.json。
输出：`../falcon in flight 3d model_repaired.glb`；可编辑源文件：`Kestrel_Repaired.blend`。

处理了退化面、145 条连接超过两个面的异常边以及不一致的面朝向，并填充了 65 个不超过 20 条边的小边界环。没有移动原始顶点，没有整体重建或减面，也没有把独立羽片合并成一个实体。

修复后为 37,960 个三角面。GLB 重新导入后确认：零面积面 0、过度连接边 0、孤立边 0、流形边面朝向冲突 0。预览检查未见明显轮廓变化。

仍保留 3,746 条开放边，包含薄片边界和较大的开放区域。本次是保守的局部修复，未宣称所有开放区域都是正常羽片，也未将模型转为全封闭实体；未做完整自相交检测。后续若有具体可见的大破洞，应根据位置局部修补，避免整体封孔改变羽毛层次。

诊断：`diagnosis.json`；导出复检：`repair_validation.json`；灰模预览：`Previews/`。
复现脚本位于项目 Temp：repair_kestrel_mesh.py、fix_kestrel_arrays.py、export_repaired_kestrel.py。
