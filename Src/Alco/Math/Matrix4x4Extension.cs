

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
        return GetEulerRadians(matrix) * TO_DEGREES;
    }

    public unsafe static Quaternion GetRotation(this Matrix4x4 matrix)
    {
        float trace = matrix.M11 + matrix.M22 + matrix.M33;

        Quaternion q;

        if (trace > 0.0f)
        {
            float invS = rsqrt(trace + 1.0f);
            q.W = 0.5f * invS;
            invS *= 0.5f;

            q.X = (matrix.M23 - matrix.M32) * invS;
            q.Y = (matrix.M31 - matrix.M13) * invS;
            q.Z = (matrix.M12 - matrix.M21) * invS;

        }
        else
        {
            int i = 0;

            if (matrix.M11 < matrix.M22)
                i = 1;

            if (matrix.M33 < matrix[i, i])
                i = 2;

            int* nxt = stackalloc int[3] { 1, 2, 0 };
            int j = nxt[i];
            int k = nxt[j];

            float s = matrix[i, i] - matrix[j, j] - matrix[k, k] + 1.0f;
            float invS = rsqrt(s);

            float* qt = stackalloc float[4];
            qt[i] = 0.5f * (1.0f / invS);
            invS *= 0.5f;

            qt[3] = (matrix[j, k] - matrix[k, j]) * invS;
            qt[j] = (matrix[i, j] + matrix[j, i]) * invS;
            qt[k] = (matrix[i, k] + matrix[k, i]) * invS;

            q.X = qt[0];
            q.Y = qt[1];
            q.Z = qt[2];
            q.W = qt[3];
        }
        return q;
    }
}


