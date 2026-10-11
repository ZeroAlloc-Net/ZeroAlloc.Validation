namespace ZeroAlloc.Validation.Internal;

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class DecimalValidator
{
    public static bool ExceedsPrecisionScale(decimal value, int precision, int scale)
    {
        // decimal.Scale reads the scale straight from the value. decimal.GetBits returned a new
        // int[4] on every call, valid values included, issue #316.
        if (value.Scale > scale) return true;

        // Count integer digits
        var abs = decimal.Truncate(decimal.Abs(value));
        int intDigits = abs == 0m ? 0 : (int)System.Math.Floor(System.Math.Log10((double)abs)) + 1;
        return intDigits + scale > precision;
    }
}
