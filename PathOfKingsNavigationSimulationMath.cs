using System.Globalization;
using System.Numerics;

namespace OrandOverlay;

internal readonly record struct Fraction : IComparable<Fraction>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public Fraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public static Fraction One => new(1, 1);
    public static Fraction From(NavigationProbability value) =>
        new(BigInteger.Parse(value.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(value.Denominator, CultureInfo.InvariantCulture));
    public static Fraction operator +(Fraction left, Fraction right) => new(
        left.Numerator * right.Denominator + right.Numerator * left.Denominator,
        left.Denominator * right.Denominator);
    public static Fraction operator -(Fraction left, Fraction right) => new(
        left.Numerator * right.Denominator - right.Numerator * left.Denominator,
        left.Denominator * right.Denominator);
    public static Fraction operator *(Fraction left, Fraction right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    public static Fraction operator *(Fraction left, int right) =>
        new(left.Numerator * right, left.Denominator);
    public static Fraction operator /(Fraction left, Fraction right) =>
        new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
    public static bool operator >(Fraction left, Fraction right) => left.CompareTo(right) > 0;
    public static bool operator <(Fraction left, Fraction right) => left.CompareTo(right) < 0;
    public int CompareTo(Fraction other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public static int Floor(Fraction value) => checked((int)(value.Numerator / value.Denominator));
    public Rational AsRational() => new(Numerator, Denominator);
    public NavigationProbability AsProbability() => new(
        Numerator.ToString(CultureInfo.InvariantCulture),
        Denominator.ToString(CultureInfo.InvariantCulture));
}
