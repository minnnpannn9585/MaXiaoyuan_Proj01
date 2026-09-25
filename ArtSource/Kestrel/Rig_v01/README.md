# 红隼基础骨骼绑定 v01

输入：`../Toes_Only_v2/Kestrel_Toes_Only_v2.blend`。使用已确认删除两根多余脚趾的版本。

输出：`Kestrel_Rig_v01.blend`（编辑源文件）、`Kestrel_Rig_v01.fbx`（Unity/Cascadeur 交换）、`Kestrel_Rig_v01.glb`（带蒙皮预览）。原始模型文件未覆盖。

45 根骨骼，FK 操作，无动画。身体 Root / Pelvis / Chest / Neck / Head；左右 WingUpper / WingFore / WingHand；Tail 与三组 TailFan；左右 Thigh / Shin / Foot；每脚四趾，每趾三节。左右以后视角色自身为准。

Blender 中选择 Kestrel_Rig，进入 Pose Mode，选择相应骨骼旋转。模型保存为原始展开姿势。Alt+R 清除旋转。爪尖跟随各趾末节，嘴部整体跟随 Head；未拆嘴，也未改网格。每顶点最多四个权重，采用线性蒙皮。

验证：原始顶点坐标、面连接不变；无漏绑顶点；权重归一化；静止姿势无位移；测试抬翼、下拍和脚趾弯曲。Previews 仅为测试姿势截图，不是动画片段。FBX/GLB 重导入结果见 export_validation.json。

限制：这是基础 FK 绑定，没有 IK、自动抓握或羽毛独立控制。原模型的翼羽和尾羽有连成整片的部分，大幅折翼/逐羽张合仍需后续专项调整；当前未验证完整贴身收翼。原有材质与模型细节保持不变。尚未替换 Unity 场景中的玩家，Unity 导入建议使用 Generic 骨架。
