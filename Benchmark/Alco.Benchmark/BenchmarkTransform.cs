using System.Numerics;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

public class BenchmarkTransform
{
    private const int count = 100000;

    private Vector3 _position3 = new Vector3(1, 2, 3);
    private Vector3 _scale3 = new Vector3(1, 1, 1);
    private Quaternion _rotation3 = math.quaternion(1, 2, 3);

    private Vector2 _position2 = new Vector2(1, 2);
    private Vector2 _scale2 = new Vector2(1, 1);
    private Rotation2D _rotation2 = new Rotation2D(90);

    [Benchmark(Description = "Build Matrix trs 3D mul")]
    public void BuildMatrixTRS3DMul()
    {
        
        _ = math.matrix4scale(_scale3) * math.matrix4rotation(_rotation3) * math.matrix4translation(_position3);
        
    }

    [Benchmark(Description = "Build Matrix trs 3D aio")]
    public void BuildMatrixTRS3DAio()
    {
        _ = math.matrix4trs(_position3, _rotation3, _scale3);
    }

    [Benchmark(Description = "Build Matrix tr 3D mul")]
    public void BuildMatrixTR3DMul()
    {
        _ = math.matrix4rotation(_rotation3) * math.matrix4translation(_position3);
    }

    [Benchmark(Description = "Build Matrix tr 3D aio")]
    public void BuildMatrixTR3DAio()
    {
        _ = math.matrix4tr(_position3, _rotation3);
    }

    [Benchmark(Description = "Build Matrix ts 3D mul")]
    public void BuildMatrixTS3DMul()
    {
        _ = math.matrix4scale(_scale3) * math.matrix4translation(_position3);
    }

    [Benchmark(Description = "Build Matrix ts 3D aio")]
    public void BuildMatrixTS3DAio()
    {
        _ = math.matrix4ts(_position3, _scale3);
    }

    [Benchmark(Description = "Build Matrix rs 3D mul")]
    public void BuildMatrixRS3DMul()
    {
        _ = math.matrix4rotation(_rotation3) * math.matrix4scale(_scale3);
    }

    [Benchmark(Description = "Build Matrix rs 3D aio")]
    public void BuildMatrixRS3DAio()
    {
        _ = math.matrix4rs(_rotation3, _scale3);
    }

    [Benchmark(Description = "Build Matrix trs 2D mul")]
    public void BuildMatrixTRS2DMul()
    {
        _ = math.matrix4scale(_scale2) * math.matrix4rotation(_rotation2) * math.matrix4translation(_position2);
    }

    [Benchmark(Description = "Build Matrix trs 2D aio")]
    public void BuildMatrixTRS2DAio()
    {
        _ = math.matrix4trs(_position2, _rotation2, _scale2);
    }

    [Benchmark(Description = "Build Matrix tr 2D mul")]
    public void BuildMatrixTR2DMul()
    {
        _ = math.matrix4rotation(_rotation2) * math.matrix4translation(_position2);
    }

    [Benchmark(Description = "Build Matrix tr 2D aio")]
    public void BuildMatrixTR2DAio()
    {
        _ = math.matrix4tr(_position2, _rotation2);
    }

    [Benchmark(Description = "Build Matrix ts 2D mul")]
    public void BuildMatrixTS2DMul()
    {
        _ = math.matrix4scale(_scale2) * math.matrix4translation(_position2);
    }

    [Benchmark(Description = "Build Matrix ts 2D aio")]
    public void BuildMatrixTS2DAio()
    {
        _ = math.matrix4ts(_position2, _scale2);
    }

    [Benchmark(Description = "Build Matrix rs 2D mul")]
    public void BuildMatrixRS2DMul()
    {
        _ = math.matrix4rotation(_rotation2) * math.matrix4scale(_scale2);
    }

    [Benchmark(Description = "Build Matrix rs 2D aio")]
    public void BuildMatrixRS2DAio()
    {
        _ = math.matrix4rs(_rotation2, _scale2);
    }
}