using Mmd;
using Diva;
using MMDiva;
using System.Text.RegularExpressions;

var options = Options.Parse(args);

var pv = options.Require("pv").ToUpperInvariant();
if (!Regex.IsMatch(pv, @"^[A-Za-z]{2}\d+$")) throw new ArgumentException("invalid PV format (should be PVxxxx)");

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

var motionFarcWriter = new MotionFarcWriter(builtMotion, pv, megamixDivaFiles);
motionFarcWriter.CreateFarcFile(motionBuilder.Set, outputPath);

CameraBaker cameraBaker = new CameraBaker(retargeter);
var (cameraFrames, cameraCuts) = cameraBaker.Bake(cameraMotion);
var cameraA3da = Auth3d.BuildA3daCamera(pv, cameraFrames, cameraCuts);

var cameraFarcWriter = new CameraFarcWriter(pv, cameraA3da, megamixDivaFiles);
cameraFarcWriter.CreateFarcFile(outputPath);

var length = double.Parse(options.Require("length-seconds"), System.Globalization.CultureInfo.InvariantCulture);
var bpm = int.Parse(options.Get("bpm", "120"), System.Globalization.CultureInfo.InvariantCulture);
var dsc = new DscBuilder(length, bpm).BuildEmptyChart();

var dscWriter = new DscWriter(pv, dsc);
dscWriter.CreateDscFile(outputPath);

var cameraDbUid = int.Parse(options.Require("camera-db-uid"), System.Globalization.CultureInfo.InvariantCulture);
var cameraDbWriter = new CameraDbWriter(pv, cameraDbUid, cameraFrames.Count);
cameraDbWriter.CreateDbFile(outputPath);

var motionSetId = uint.Parse(options.Require("motion-set-id"), System.Globalization.CultureInfo.InvariantCulture);
var motionDbId = uint.Parse(options.Require("motion-db-id"), System.Globalization.CultureInfo.InvariantCulture);
var motionDbWriter = new MotionDbWriter(motionBuilder, pv, motionSetId, motionDbId);
motionDbWriter.CreateDbFile(outputPath);
