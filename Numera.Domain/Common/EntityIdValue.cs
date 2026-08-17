using System.Buffers.Binary;
using System.Globalization;

namespace Numera.Domain.Common;

public readonly struct EntityIdValue : IEquatable<EntityIdValue>, IComparable<EntityIdValue>
{
    public const int ByteLength = 16;
    public const int TextLength = 32;

    private readonly UInt128 bits;

    private EntityIdValue(UInt128 bits) => this.bits = bits;

    public static EntityIdValue Empty => default;

    public bool IsEmpty => bits == UInt128.Zero;

    public static EntityIdValue FromBits(UInt128 bits) => new(bits);

    public static EntityIdValue FromBytes(ReadOnlySpan<byte> source)
    {
        if (source.Length != ByteLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.EntityIdLengthInvalid);
        }

        return new EntityIdValue(BinaryPrimitives.ReadUInt128BigEndian(source));
    }

    public static bool TryParse(ReadOnlySpan<char> source, out EntityIdValue result)
    {
        result = default;
        if (source.Length != TextLength)
        {
            return false;
        }

        UInt128 accumulated = UInt128.Zero;
        foreach (char character in source)
        {
            int digit = HexDigit(character);
            if (digit < 0)
            {
                return false;
            }

            accumulated = (accumulated << 4) | (UInt128)(uint)digit;
        }

        result = new EntityIdValue(accumulated);
        return true;
    }

    public static EntityIdValue Parse(ReadOnlySpan<char> source) =>
        TryParse(source, out EntityIdValue result)
            ? result
            : throw InvariantViolationException.Create(InvariantViolationCode.EntityIdTextInvalid);

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length != ByteLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.EntityIdLengthInvalid);
        }

        BinaryPrimitives.WriteUInt128BigEndian(destination, bits);
    }

    public byte[] ToByteArray()
    {
        byte[] buffer = new byte[ByteLength];
        WriteBytes(buffer);
        return buffer;
    }

    public bool Equals(EntityIdValue other) => bits == other.bits;

    public override bool Equals(object? obj) => obj is EntityIdValue other && Equals(other);

    public override int GetHashCode() => bits.GetHashCode();

    public int CompareTo(EntityIdValue other) => bits.CompareTo(other.bits);

    public override string ToString() => bits.ToString("x32", CultureInfo.InvariantCulture);

    public static bool operator ==(EntityIdValue left, EntityIdValue right) => left.Equals(right);

    public static bool operator !=(EntityIdValue left, EntityIdValue right) => !left.Equals(right);

    public static bool operator <(EntityIdValue left, EntityIdValue right) => left.CompareTo(right) < 0;

    public static bool operator <=(EntityIdValue left, EntityIdValue right) => left.CompareTo(right) <= 0;

    public static bool operator >(EntityIdValue left, EntityIdValue right) => left.CompareTo(right) > 0;

    public static bool operator >=(EntityIdValue left, EntityIdValue right) => left.CompareTo(right) >= 0;

    private static int HexDigit(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1,
    };
}
