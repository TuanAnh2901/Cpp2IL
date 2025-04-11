namespace Cpp2IL.Core.ISIL;

public readonly struct IsilRegisterOperand(string registerName) : IsilOperandData
{
    public readonly string RegisterName = registerName;

    public bool IsZeroAlias => RegisterName == "X31" || RegisterName == "W31";

    public string GetZeroRegName()
    {
        if (RegisterName == "X31")
            return "XZR";
        if (RegisterName == "W31")
            return "WZR";
        return RegisterName;
    }
    public override string ToString() => RegisterName;
}
