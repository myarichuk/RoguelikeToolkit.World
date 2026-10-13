// Small BCL APIs that netstandard2.1 (Unity) lacks. Each shim is behavior-identical for the way Core uses it;
// on .NET the real API is used (the shims compile out).
#if !NET7_0_OR_GREATER
namespace RoguelikeToolkit.World.Core
{
    using System.IO;

    internal static class StreamCompat
    {
        /// <summary>Reads exactly <c>buffer.Length</c> bytes or throws <see cref="EndOfStreamException"/> (.NET 7 <c>Stream.ReadExactly</c>).</summary>
        public static void ReadExactly(this Stream stream, Span<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int read = stream.Read(buffer);
                if (read <= 0) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                buffer = buffer.Slice(read);
            }
        }

        /// <summary>Array overload of <see cref="ReadExactly(Stream, Span{byte})"/>.</summary>
        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
            => stream.ReadExactly(buffer.AsSpan(offset, count));
    }
}
#endif

#if !NET6_0_OR_GREATER
namespace RoguelikeToolkit.World.Core
{
    using System.Runtime.CompilerServices;

    internal static class ArgumentNullExceptionCompat
    {
        // C# 14 static extension: call sites keep the .NET 6+ spelling `ArgumentNullException.ThrowIfNull(x)`.
        extension(ArgumentNullException)
        {
            /// <summary>.NET 6 <c>ArgumentNullException.ThrowIfNull</c>.</summary>
            public static void ThrowIfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
            {
                if (argument is null) throw new ArgumentNullException(paramName);
            }
        }
    }

    internal static class EnumCompat
    {
        extension(Enum)
        {
            /// <summary>.NET 5 <c>Enum.GetValues&lt;T&gt;()</c>.</summary>
            public static TEnum[] GetValues<TEnum>() where TEnum : struct, Enum => (TEnum[])Enum.GetValues(typeof(TEnum));
        }
    }

    internal static class ArrayCompat
    {
        extension(Array)
        {
            /// <summary>.NET 6 <c>Array.Clear(Array)</c>.</summary>
            public static void Clear(Array array) => Array.Clear(array, 0, array.Length);

            /// <summary>.NET 6 <c>Array.MaxLength</c> (CoreCLR's limit; also a safe bound on Mono/IL2CPP).</summary>
            public static int MaxLength => 0x7FFFFFC7;
        }
    }

    internal static class MathCompat
    {
        // 2^1023, 2^-1022, 2^53 (exact)
        private static readonly double TwoPow1023 = BitConverter.Int64BitsToDouble(0x7FE0000000000000);
        private static readonly double TwoPowMinus1022 = BitConverter.Int64BitsToDouble(0x0010000000000000);
        private const double TwoPow53 = 9007199254740992.0;

        extension(Math)
        {
            /// <summary>
            /// .NET Core 3 <c>Math.ScaleB</c>: x * 2^n computed exactly. Same algorithm as the BCL (musl <c>scalbn</c>),
            /// so DetMath stays bit-identical across runtimes instead of depending on the host libm.
            /// </summary>
            public static double ScaleB(double x, int n)
            {
                double y = x;
                if (n > 1023)
                {
                    y *= TwoPow1023;
                    n -= 1023;
                    if (n > 1023)
                    {
                        y *= TwoPow1023;
                        n -= 1023;
                        if (n > 1023) n = 1023;
                    }
                }
                else if (n < -1022)
                {
                    y *= TwoPowMinus1022 * TwoPow53;
                    n += 1022 - 53;
                    if (n < -1022)
                    {
                        y *= TwoPowMinus1022 * TwoPow53;
                        n += 1022 - 53;
                        if (n < -1022) n = -1022;
                    }
                }

                return y * BitConverter.Int64BitsToDouble((long)(0x3ff + n) << 52);
            }
        }
    }

    internal static class HashCompat
    {
        extension(Convert)
        {
            /// <summary>.NET 5 <c>Convert.ToHexString</c> (uppercase).</summary>
            public static string ToHexString(byte[] bytes)
            {
                const string Digits = "0123456789ABCDEF";
                var chars = new char[bytes.Length * 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    chars[2 * i] = Digits[bytes[i] >> 4];
                    chars[2 * i + 1] = Digits[bytes[i] & 0xF];
                }
                return new string(chars);
            }
        }

        extension(System.Security.Cryptography.SHA256)
        {
            /// <summary>.NET 5 <c>SHA256.HashData</c>.</summary>
            public static byte[] HashData(byte[] source)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                return sha.ComputeHash(source);
            }
        }
    }

    internal static class SortCompat
    {
        /// <summary>
        /// .NET 5 <c>MemoryExtensions.Sort(keys, items)</c> for small stack spans. Only the search pruning order
        /// depends on it (the nearest-cell search is exact), so tie order between equal keys does not affect results.
        /// </summary>
        public static void Sort(Span<double> keys, Span<int> items)
        {
            var k = keys.ToArray();
            var v = items.ToArray();
            Array.Sort(k, v);
            k.CopyTo(keys);
            v.CopyTo(items);
        }
    }

    internal static class RandomCompat
    {
        /// <summary>Non-deterministic 64-bit value (stand-in for <c>Random.Shared.NextInt64()</c>); thread-safe.</summary>
        public static long NextInt64() => BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
    }
}

namespace System.Collections.Generic
{
    using System.Runtime.CompilerServices;

    /// <summary>.NET 5 <c>ReferenceEqualityComparer</c>.</summary>
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>
    {
        private ReferenceEqualityComparer() { }

        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj!);
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) => ParameterName = parameterName;
        public string ParameterName { get; }
    }
}
#endif
