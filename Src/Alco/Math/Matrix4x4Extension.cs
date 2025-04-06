

using System.Numerics;

using static Alco.math;

namespace Alco;

public static class Matrix4x4Extension
{
    public static Vector3 GetTranslation(this Matrix4x4 matrix)
    {
        return new Vector3(matrix.M41, matrix.M42, matrix.M43);
    }

    public static Vector3 GetEulerRadians(this Matrix4x4 matrix)
    {
        Vector3 axisX = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        Vector3 axisY = new Vector3(matrix.M21, matrix.M22, matrix.M23);
        Vector3 axisZ = new Vector3(matrix.M31, matrix.M32, matrix.M33);

        float pitch = atan2(axisX.Z, sqrt(axisX.X * axisX.X + axisX.Y * axisX.Y));
        float yaw = atan2(axisX.Y, axisX.X);
        float roll = atan2(axisZ.Y, axisY.Y);

        return new Vector3(roll, pitch, yaw);
    }

    public static Vector3 GetEulerDegrees(this Matrix4x4 matrix)
    {
        return GetEulerRadians(matrix) * RadToDeg;
    }

}


