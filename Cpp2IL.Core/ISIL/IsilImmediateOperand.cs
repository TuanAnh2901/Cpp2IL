using System;
using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilImmediateOperand(IConvertible value) : IsilOperandData
{
    public readonly IConvertible Value = value;
    
    
    public float CastToFloat()
    {
        try
        {
            return Convert.ToSingle(Value);
        }
        catch (Exception e)
        {
            throw new InvalidCastException($"Failed to cast immediate operand value '{Value}' of type {Value.GetType().Name} to float.", e);
        }
    }
    public long CastToLong()
    {
        try
        {
            return Convert.ToInt64(Value);
        }
        catch (Exception e)
        {
            throw new InvalidCastException($"Failed to cast immediate operand value '{Value}' of type {Value.GetType().Name} to long.", e);
        }
    }
    public long AsLong() => Convert.ToInt64(Value);
    public override string ToString()
    {
        if (Value is string)
        {
            return "\"" + Value + "\"";
        }

        try
        {
            //Quick sanity to reduce the possibility of throwing exceptions here, because that's slow
            var isUlongAndTooLarge = Value is ulong and >= long.MaxValue;

            if (!isUlongAndTooLarge && Convert.ToInt64(Value) > 0x1000)
                return $"0x{Value:X}";
        }
        catch
        {
            //Ignore
        }

        return Value.ToString(CultureInfo.InvariantCulture);
    }
}
