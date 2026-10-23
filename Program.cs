using Mmd;
using Diva;
using MMDiva;

var options = Options.Parse(args);

var pv = options.Require("pv").ToUpperInvariant();
var outputPath = options.Get("output", "./");

VmdMotion danceMotion = VmdReader.Read(options.Require("dance-vmd"));
VmdMotion cameraMotion = VmdReader.Read(options.Require("camera-vmd"));
PmxModel model = PmxReader.Read(options.Require("pmx"));

MmdPose mmdPose = new MmdPose(model);
MegamixDivaFiles megamixDivaFiles = new MegamixDivaFiles(options.Require("megamix"));

RobPose robPose = new RobPose(megamixDivaFiles);

Retargeter retargeter = new Retargeter(mmdPose, robPose, new Retargeter.Options());

MotionBaker baker = new MotionBaker(retargeter);
var danceFrames = baker.Bake(danceMotion);

var motionBuilder = new MotionBuilder(robPose.Skeleton, new MotionBuilder.Options(), megamixDivaFiles);
var builtMotion = motionBuilder.Build(danceFrames);

var motionFarcBuilder = new MotionFarcBuilder(builtMotion, pv, megamixDivaFiles);
motionFarcBuilder.CreateFarcFile(motionBuilder.Set, outputPath);

CameraBaker cameraBaker = new CameraBaker();
var (cameraFrames, cameraCuts) = cameraBaker.Bake(cameraMotion);
var cameraA3da = Auth3d.BuildA3daCamera(pv, cameraFrames, cameraCuts);

var cameraFarcBuilder = new CameraFarcBuilder(pv, cameraA3da, megamixDivaFiles);
cameraFarcBuilder.CreateFarcFile(outputPath);
