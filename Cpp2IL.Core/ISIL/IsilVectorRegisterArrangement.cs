using Disarm;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilVectorRegisterArrangement(string reg, Arm64ArrangementSpecifier arrangementSpecifier) :IsilOperandData
{
    public readonly string RegisterName = reg;
    public readonly Arm64ArrangementSpecifier ArrangementSpecifier = arrangementSpecifier;


    public override string ToString()
    {
        return $"{RegisterName}.{ArrangementSpecifier}";
    }

   
}
