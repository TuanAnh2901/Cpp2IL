using Cpp2IL.Core.ISIL;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public abstract class BaseProcessor : IFlagsProcessor
{
    
    public abstract void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode, ulong branchTarget,
        ulong addr);

    public abstract void GenerateConditionalSelect(IsilBuilder builder, ulong addr, FlagsState state, InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue, InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode);
    
    protected bool IsSignedConditionCode(Arm64ConditionCode conditionCode)
    {
        return conditionCode switch
        {
            Arm64ConditionCode.LE => true,
            Arm64ConditionCode.GE => true,
            Arm64ConditionCode.GT => true,
            Arm64ConditionCode.MI => true,
            Arm64ConditionCode.LT => true,
            Arm64ConditionCode.CC => true,
            Arm64ConditionCode.CS => true,
            _ => false
        };
    }
}
