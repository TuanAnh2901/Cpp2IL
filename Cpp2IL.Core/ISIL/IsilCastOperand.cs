using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.ISIL;

public class IsilCastOperand(Il2CppTypeEnum eTypeEnum,bool isSmart) : IsilOperandData
{

    public readonly Il2CppTypeEnum Il2CppTypeEnum = eTypeEnum;

    public readonly bool IsSmart = isSmart;
    public override string ToString()
    {
        if (Il2CppTypeEnum==Il2CppTypeEnum.IL2CPP_TYPE_END && IsSmart)
        {
            return "SmartCast";
        }
        return Il2CppTypeEnum.ToString();
    }
}
