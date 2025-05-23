using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.ISIL;

public class IsilCastOperand(Il2CppTypeEnum eTypeEnum) : IsilOperandData
{

    public readonly Il2CppTypeEnum Il2CppTypeEnum = eTypeEnum;

    public override string ToString()
    {
        return Il2CppTypeEnum.ToString();
    }
}
