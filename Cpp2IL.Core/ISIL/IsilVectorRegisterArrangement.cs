using Disarm;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilVectorRegisterArrangement(string reg, Arm64ArrangementSpecifier arrangementSpecifier) :IsilOperandData
{
    public string RegisterName => reg;
    public Arm64ArrangementSpecifier ArrangementSpecifier =>arrangementSpecifier;


    public override string ToString()
    {
        return $"{RegisterName}.{ArrangementSpecifier}";
    }

   
}
