namespace Cpp2IL.Core.ISIL;

public  readonly struct  IsilSimdMathType(IsilMnemonic isilMnemonic) : IsilOperandData
{
    public override string ToString()
    {
        return $"{isilMnemonic}";
    }
}
