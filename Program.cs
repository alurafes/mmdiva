using Mmd;
using Diva;
using MMDiva;

VmdMotion motion = VmdReader.Read("f:\\PDAFTMM\\test_assets\\dance\\extracted\\恋は戦争モーション\\恋は戦争モーション(あにまさミク用).vmd");
PmxModel model = PmxReader.Read("f:\\PDAFTMM\\test_assets\\model\\extracted\\Tda式初音ミク・アペンドVer1.10\\Tda式初音ミク・アペンド_Ver1.10.pmx");
VmdMotion cameraMotion = VmdReader.Read("F:\\PDAFTMM\\test_assets\\camera\\extracted\\Love is War Camera\\Love is War Camera.vmd");

MmdPose mmdPose = new MmdPose(model);
MmdCamera camera = new MmdCamera();
mmdPose.Evaluate(null, 0); // setting to rest pose
camera.Evaluate(cameraMotion, 1000f);


MegamixDivaFiles megamixDivaFiles = new MegamixDivaFiles("F:\\SteamLibrary\\steamapps\\common\\Hatsune Miku Project DIVA Mega Mix Plus");

RobSkeleton skeleton = new RobSkeleton(megamixDivaFiles);
RobPose robPose = new RobPose(skeleton);
var restPose = RobRestPoseLoader.Load(megamixDivaFiles);
robPose.Apply(restPose);

Retargeter retargeter = new Retargeter(mmdPose, robPose, new Retargeter.Options());

retargeter.Solve(motion, 1000f);

MMDiva.Debug.StickRenderer.Render(mmdPose.StickRendererSegments(0.06f), "MMD.png", 2.2f, 300);
MMDiva.Debug.StickRenderer.Render(robPose.StickRendererSegments(), "DIVA.png", 2.2f, 300);
