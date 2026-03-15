using System;
using System.Diagnostics;

class Program
{
    static float[] MultiplyMatrixOld(float[] a, float[] b)
    {
        float[] result = new float[16];
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                result[c * 4 + r] =
                    a[r] * b[c * 4] +
                    a[r + 4] * b[c * 4 + 1] +
                    a[r + 8] * b[c * 4 + 2] +
                    a[r + 12] * b[c * 4 + 3];
            }
        }
        return result;
    }

    static void MultiplyMatrixNew(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> result)
    {
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                result[c * 4 + r] =
                    a[r] * b[c * 4] +
                    a[r + 4] * b[c * 4 + 1] +
                    a[r + 8] * b[c * 4 + 2] +
                    a[r + 12] * b[c * 4 + 3];
            }
        }
    }

    static void LoopNew(float[] a, float[] b, int iters)
    {
        Span<float> res = stackalloc float[16];
        for (int i = 0; i < iters; i++)
        {
            MultiplyMatrixNew(a, b, res);
        }
    }

    static void Main()
    {
        float[] m = new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
        int iters = 10000000;

        // Warmup
        MultiplyMatrixOld(m, m);
        Span<float> resNew = stackalloc float[16];
        MultiplyMatrixNew(m, m, resNew);

        var sw = Stopwatch.StartNew();
        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iters; i++)
        {
            var res = MultiplyMatrixOld(m, m);
        }
        long memAfter = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();
        Console.WriteLine($"Old: {sw.ElapsedMilliseconds}ms, Allocations: {(memAfter - memBefore) / 1024 / 1024} MB");

        sw.Restart();
        memBefore = GC.GetAllocatedBytesForCurrentThread();
        LoopNew(m, m, iters);
        memAfter = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();
        Console.WriteLine($"New: {sw.ElapsedMilliseconds}ms, Allocations: {(memAfter - memBefore) / 1024 / 1024} MB");
    }
}
