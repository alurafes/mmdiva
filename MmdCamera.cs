using System.Net.Cache;
using System.Numerics;

public sealed class MmdCamera
{
    public sealed class CameraState
    {
        public Vector3 Eye { get; set; } = Vector3.Zero;
        public Vector3 Interest { get; set; } = Vector3.Zero;
        public Vector3 Up { get; set; } = Vector3.Zero;
        public float FovRad = 0.0f;
    }

    public CameraState State { get; private set; } = new();

    public void Evaluate(VmdMotion motion, float frame)
    {
        var (f0, f1) = motion.SampleCamera(frame);

        Vector3 interest = f0.InterestPoint, rot = f0.Rotation;
        float dist = f0.Distance, fov = f0.Fov;

        // 1 frame gap is a cut
        if (f1 != null && f1.Frame - f0.Frame > 1)
        {
            float t = (frame - f0.Frame) / (f1.Frame - f0.Frame);

            interest = new Vector3(
                Lerp(f0.InterestPoint.X, f1.InterestPoint.X, f1.Bezier(0, t)),
                Lerp(f0.InterestPoint.Y, f1.InterestPoint.Y, f1.Bezier(1, t)),
                Lerp(f0.InterestPoint.Z, f1.InterestPoint.Z, f1.Bezier(2, t))
            );
            rot = Vector3.Lerp(f0.Rotation, f1.Rotation, f1.Bezier(3, t));
            dist = Lerp(f0.Distance, f1.Distance, f1.Bezier(4, t));
            fov = Lerp(f0.Fov, f1.Fov, f1.Bezier(5, t));
        }

        var rotation = Matrix4x4.CreateRotationX(rot.X) * Matrix4x4.CreateRotationZ(-rot.Z) * Matrix4x4.CreateRotationY(rot.Y);
        var i = new Vector3(interest.X, interest.Y, -interest.Z);
        var eye = i + Vector3.TransformNormal(new Vector3(0, 0, -dist), rotation);
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, rotation);

        State.Interest = dist < -1e-3f ? i : eye + forward * 10;
        State.Eye = eye;
        State.Up = Vector3.TransformNormal(Vector3.UnitY, rotation);
        State.FovRad = fov * MathF.PI / 180;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}