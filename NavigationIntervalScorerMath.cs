using System.Numerics;

namespace OrandOverlay;

public static class NavigationIntervalScorerMath
{
    public static int StateUtilityBp(int buildBp, int coreBp, int combatBp)
    {
        ValidateBasisPoints(buildBp);
        ValidateBasisPoints(coreBp);
        ValidateBasisPoints(combatBp);
        return checked((60 * buildBp + 25 * coreBp + 15 * combatBp + 50) / 100);
    }

    public static NavigationSignedRational FromInteger(int value) => new(value, 1);

    public static NavigationSignedRational FromRational(Rational value) =>
        new(value.Numerator, value.Denominator);

    public static NavigationSignedRational Add(
        NavigationSignedRational left,
        NavigationSignedRational right)
    {
        var gcd = BigInteger.GreatestCommonDivisor(left.Denominator, right.Denominator);
        var leftScale = right.Denominator / gcd;
        var rightScale = left.Denominator / gcd;
        return new NavigationSignedRational(
            left.Numerator * leftScale + right.Numerator * rightScale,
            left.Denominator * leftScale);
    }

    public static NavigationSignedRational Subtract(
        NavigationSignedRational left,
        NavigationSignedRational right) =>
        Add(left, new NavigationSignedRational(-right.Numerator, right.Denominator));

    public static NavigationSignedRational Multiply(
        NavigationSignedRational left,
        NavigationSignedRational right)
    {
        var leftNumerator = left.Numerator;
        var rightNumerator = right.Numerator;
        var gcdLeft = BigInteger.GreatestCommonDivisor(
            BigInteger.Abs(leftNumerator), right.Denominator);
        var gcdRight = BigInteger.GreatestCommonDivisor(
            BigInteger.Abs(rightNumerator), left.Denominator);
        return new NavigationSignedRational(
            leftNumerator / gcdLeft * (rightNumerator / gcdRight),
            left.Denominator / gcdRight * (right.Denominator / gcdLeft));
    }

    public static NavigationSignedRational Multiply(
        NavigationSignedRational value,
        Rational probability) =>
        Multiply(value, FromRational(probability));

    public static NavigationSignedRational Divide(
        NavigationSignedRational value,
        int divisor)
    {
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor));
        return Multiply(value, new NavigationSignedRational(1, divisor));
    }

    public static Rational Square(NavigationSignedRational value) =>
        new(BigInteger.Abs(value.Numerator) * BigInteger.Abs(value.Numerator),
            value.Denominator * value.Denominator);

    public static Rational Add(Rational left, Rational right)
    {
        var gcd = BigInteger.GreatestCommonDivisor(left.Denominator, right.Denominator);
        var leftScale = right.Denominator / gcd;
        var rightScale = left.Denominator / gcd;
        return new Rational(
            left.Numerator * leftScale + right.Numerator * rightScale,
            left.Denominator * leftScale);
    }

    public static Rational Subtract(Rational left, Rational right)
    {
        var gcd = BigInteger.GreatestCommonDivisor(left.Denominator, right.Denominator);
        var leftScale = right.Denominator / gcd;
        var rightScale = left.Denominator / gcd;
        var numerator = left.Numerator * leftScale - right.Numerator * rightScale;
        if (numerator < 0)
            throw new ArgumentOutOfRangeException(nameof(right));
        return new Rational(numerator, left.Denominator * leftScale);
    }

    public static Rational Multiply(Rational left, Rational right)
    {
        var gcdLeft = BigInteger.GreatestCommonDivisor(left.Numerator, right.Denominator);
        var gcdRight = BigInteger.GreatestCommonDivisor(right.Numerator, left.Denominator);
        return new Rational(
            left.Numerator / gcdLeft * (right.Numerator / gcdRight),
            left.Denominator / gcdRight * (right.Denominator / gcdLeft));
    }

    public static int RoundAwayFromZero(NavigationSignedRational value)
    {
        var absolute = BigInteger.Abs(value.Numerator);
        var quotient = BigInteger.DivRem(absolute, value.Denominator, out var remainder);
        if (remainder * 2 >= value.Denominator)
            quotient++;
        if (value.Numerator < 0)
            quotient = -quotient;
        return checked((int)quotient);
    }

    public static int FloorSquareRoot(Rational value)
    {
        var integerPart = value.Numerator / value.Denominator;
        if (integerPart.IsZero)
            return 0;
        var low = BigInteger.Zero;
        var high = BigInteger.One << checked((int)((integerPart.GetBitLength() + 1) / 2));
        while (low < high)
        {
            var middle = (low + high + 1) >> 1;
            if (middle * middle <= integerPart)
                low = middle;
            else
                high = middle - 1;
        }
        return checked((int)low);
    }

    public static int Compare(NavigationSignedRational left,
        NavigationSignedRational right) =>
        (left.Numerator * right.Denominator)
            .CompareTo(right.Numerator * left.Denominator);

    public static int Compare(Rational left, Rational right) =>
        (left.Numerator * right.Denominator)
            .CompareTo(right.Numerator * left.Denominator);

    public static int ClampBasisPoints(int value) => Math.Clamp(value, 0, 10_000);

    public static int MaxBitLength(NavigationSignedRational value) =>
        checked((int)Math.Max(BigInteger.Abs(value.Numerator).GetBitLength(),
            value.Denominator.GetBitLength()));

    public static int MaxBitLength(Rational value) =>
        checked((int)Math.Max(value.Numerator.GetBitLength(), value.Denominator.GetBitLength()));

    public static void ValidateBasisPoints(int value)
    {
        if (value is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
