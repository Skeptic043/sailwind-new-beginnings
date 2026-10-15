// Measured native service bounds, modeled without Unity collision.
using UnityEngine;

static class NativeYardMeasuredBox
{
    internal static bool Contains(int port, Vector3 point)
    {
        float[, ] m;
        Vector3 c, h;
        switch (port)
        {
            case 0:
                m = new float[, ]
                {
                    {
                        -0.282922094f,
                        0f,
                        -0.412256152f,
                        820.80082f
                    },
                    {
                        0f,
                        0.5f,
                        0f,
                        6.17999983f
                    },
                    {
                        0.412256152f,
                        0f,
                        -0.282922094f,
                        -199.519535f
                    }
                };
                c = new Vector3(12.132247f, 0f, 0.727416456f);
                h = new Vector3(35.0120888f, 27.5001965f, 18.4584446f);
                break;
            case 9:
                m = new float[, ]
                {
                    {
                        0.411135866f,
                        0f,
                        -0.284548263f,
                        -141.367188f
                    },
                    {
                        0f,
                        0.49999997f,
                        0f,
                        5.52109098f
                    },
                    {
                        0.284548263f,
                        0f,
                        0.411135866f,
                        -530.007812f
                    }
                };
                c = new Vector3(-1.76108432f, 0f, 3.71647859f);
                h = new Vector3(15.7360725f, 27.5001659f, 31.2164555f);
                break;
            case 22:
                m = new float[, ]
                {
                    {
                        -0.473045337f,
                        0f,
                        -0.161952804f,
                        -9.33203125f
                    },
                    {
                        0f,
                        0.5f,
                        0f,
                        4.26999998f
                    },
                    {
                        0.161952804f,
                        0f,
                        -0.473045337f,
                        119.570312f
                    }
                };
                c = new Vector3(13.9248276f, 0f, 0.727416813f);
                h = new Vector3(45.9625359f, 27.5001965f, 18.4584446f);
                break;
            default:
                throw new System.Exception("unknown measured yard");
        }

        var d = point - new Vector3(m[0, 3], m[1, 3], m[2, 3]);
        var local = new Vector3();
        for (int i = 0; i < 3; i++)
        {
            var axis = new Vector3(m[0, i], m[1, i], m[2, i]);
            var p = Vector3.Dot(d, axis) / axis.sqrMagnitude;
            if (i == 0)
                local.x = p;
            else if (i == 1)
                local.y = p;
            else
                local.z = p;
        }

        return System.MathF.Abs(local.x - c.x) <= h.x && System.MathF.Abs(local.y - c.y) <= h.y && System.MathF.Abs(local.z - c.z) <= h.z;
    }
}
