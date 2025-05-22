using Cpp2IL.Core.ISIL;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public class CompareProcessor : IFlagsProcessor
{
    public void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode,
        ulong branchTarget)
    {
        throw new System.NotImplementedException();
    }

    public void GenerateConditionalSelect(IsilBuilder builder, FlagsState state, InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue, InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode)
    {
        throw new System.NotImplementedException();
    }
}
