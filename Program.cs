VmdMotion motion = VmdReader.Read("f:\\PDAFTMM\\test_assets\\dance\\extracted\\恋は戦争モーション\\恋は戦争モーション(あにまさミク用).vmd");
PmxModel model = PmxReader.Read("f:\\PDAFTMM\\test_assets\\model\\extracted\\Tda式初音ミク・アペンドVer1.10\\Tda式初音ミク・アペンド_Ver1.10.pmx");
VmdMotion cameraMotion = VmdReader.Read("F:\\PDAFTMM\\test_assets\\camera\\extracted\\Love is War Camera\\Love is War Camera.vmd");

MmdPose pose = new MmdPose(model);
MmdCamera camera = new MmdCamera();
camera.Evaluate(cameraMotion, 1000f);
pose.Evaluate(motion, 1000f);