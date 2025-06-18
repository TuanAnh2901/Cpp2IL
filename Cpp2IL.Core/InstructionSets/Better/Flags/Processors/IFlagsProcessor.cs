using Cpp2IL.Core.ISIL;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public interface IFlagsProcessor
{
    /// <summary>
    /// 为特定条件码生成比较和跳转逻辑
    /// </summary>
    void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode, ulong branchTarget,
        ulong addr);
    //CINC 条件递增指令
    void GenerateConditionalIncrement(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest, InstructionSetIndependentOperand source, Arm64ConditionCode conditionCode);
    void GenerateConditionalIncrement2Args(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest, InstructionSetIndependentOperand arg1,InstructionSetIndependentOperand arg2, Arm64ConditionCode conditionCode);
    
    void GenerateConditionalNegate(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest, InstructionSetIndependentOperand source, Arm64ConditionCode conditionCode);
    /// <summary>
    /// 为条件选择指令生成逻辑
    /// </summary>
    void GenerateConditionalSelect(IsilBuilder builder,ulong addr, FlagsState state, 
        InstructionSetIndependentOperand dest, 
        InstructionSetIndependentOperand trueValue, 
        InstructionSetIndependentOperand falseValue, 
        Arm64ConditionCode conditionCode);
    
    
   
}
