using System.Numerics;

namespace FVMeatbag.Scene;

/// <summary>Local translation, rotation and scale of a scene component.</summary>
public struct Transform : IEquatable<Transform>
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static Transform Identity => new() { Rotation = Quaternion.Identity, Scale = Vector3.One };

    public readonly Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public readonly bool Equals(Transform other) =>
        Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;

    public override readonly bool Equals(object? obj) => obj is Transform other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(Position, Rotation, Scale);
    public static bool operator ==(Transform left, Transform right) => left.Equals(right);
    public static bool operator !=(Transform left, Transform right) => !left.Equals(right);
}
