using Mmd;
using Diva;
using MMDiva;

var options = Options.Parse(args);

VmdMotion motion = VmdReader.Read(options.Require("vmd"));
PmxModel model = PmxReader.Read(options.Require("pmx"));

MmdPose mmdPose = new MmdPose(model);
MegamixDivaFiles megamixDivaFiles = new MegamixDivaFiles(options.Require("megamix"));

RobPose robPose = new RobPose(megamixDivaFiles);

Retargeter retargeter = new Retargeter(mmdPose, robPose, new Retargeter.Options());

Baker baker = new Baker(retargeter);
var frames = baker.Bake(motion);

var motionBuilder = new MotionBuilder(robPose.Skeleton, new MotionBuilder.Options(), megamixDivaFiles);
var builtMotion = motionBuilder.Build(frames);

var motionFarcBuilder = new MotionFarcBuilder(builtMotion, options.Require("pv"), megamixDivaFiles);
motionFarcBuilder.CreateFarcFile(motionBuilder.Set, options.Get("output", "./"));