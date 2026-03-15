namespace ZValidationInternal;

internal static class DecimalValidator
{
    internal static bool ExceedsPrecisionScale(decimal value, int precision, int scale)
    {
        // Extract actual scale (decimal places) from decimal bits — zero allocation
        var bits = decimal.GetBits(value);
        int actualScale = (bits[3] >> 16) & 0x1F;
        if (actualScale > scale) return true;

        // Count integer digits
        var abs = decimal.Truncate(decimal.Abs(value));
        int intDigits = abs == 0m ? 0 : (int)System.Math.Floor(System.Math.Log10((double)abs)) + 1;
        return intDigits + scale > precision;
    }
}
