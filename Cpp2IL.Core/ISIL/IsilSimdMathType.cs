namespace Cpp2IL.Core.ISIL;

public  readonly struct  IsilSimdMathType(IsilMnemonic isilMnemonic) : IsilOperandData
{
    public IsilMnemonic IsilMnemonic => isilMnemonic;
    public override string ToString()
    {
        return $"{isilMnemonic}";
    }
}
