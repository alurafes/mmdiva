using Mmd;
using Diva;
using System.Numerics;

namespace MMDiva;

public sealed class CameraBaker
{
    public CameraBaker()
    {
        
    }

    private static float RollFor(Vector3 eye, Vector3 interest, Vector3 up)
    {
        var dir = eye - interest;
        float yaw = MathF.Atan2(dir.X, dir.Z);
        float pitch = MathF.Atan2(-dir.Y, MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z));
        var basis = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw);
        Matrix4x4.Invert(basis, out var inv);
        var local = Vector3.TransformNormal(up, inv);
        return MathF.Atan2(-local.X, local.Y);
    }

    public (List<Auth3d.CameraFrame>, bool[]) Bake(VmdMotion motion)
    {
        int count = motion.MaxFrame * 2 + 1;

        MmdCamera mmdCamera = new MmdCamera(motion);

        List<Auth3d.CameraFrame> frames = new List<Auth3d.CameraFrame>();
        var cutAfterFrame = new bool[count];

        for (int frame = 0; frame < count; frame++)
        {
            float mmdFrame = frame / 2.0f;
            mmdCamera.Evaluate(mmdFrame);
            
            frames.Add(new Auth3d.CameraFrame
            {
                Eye = mmdCamera.State.Eye,
                Interest = mmdCamera.State.Interest,
                Roll = RollFor(mmdCamera.State.Eye, mmdCamera.State.Interest, mmdCamera.State.Up),
                FovX = 2 * MathF.Atan(MathF.Tan(mmdCamera.State.FovRad / 2) * (16f / 9f)),
            });

            cutAfterFrame[frame] = mmdCamera.HasCutBetweenFrames(frame, frame + 1);
        }

        return (frames, cutAfterFrame);
    }
}