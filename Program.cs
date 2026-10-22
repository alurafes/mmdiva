VmdMotion motion = VmdReader.Read("f:\\PDAFTMM\\test_assets\\dance\\extracted\\恋は戦争モーション\\恋は戦争モーション(あにまさミク用).vmd");
PmxModel model = PmxReader.Read("f:\\PDAFTMM\\test_assets\\model\\extracted\\Tda式初音ミク・アペンドVer1.10\\Tda式初音ミク・アペンド_Ver1.10.pmx");

MmdPose pose = new MmdPose(model);
pose.Evaluate(motion, 1000f);