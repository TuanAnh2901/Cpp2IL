using System;
using Cpp2IL.Core.ISIL;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public class ArithmeticProcessor : IFlagsProcessor
{
    public void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode, ulong branchTarget,
        ulong addr)
    {
        switch (conditionCode)
        {
            
            case Arm64ConditionCode.EQ: // 结果等于0
            {
                // 处理等于条件
                builder.Compare(state.Address, state.Dest, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfEqual(addr , branchTarget);
                break;
            }
            case Arm64ConditionCode.NE: // 结果不等于0
            {
                // 处理不等于条件
                builder.Compare(state.Address, state.Dest, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfNotEqual(addr , branchTarget);
                break;
            }
            case Arm64ConditionCode.PL:
            {
                // 处理大于等于条件
                builder.Compare(state.Address, state.Dest, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfGreaterOrEqual(addr , branchTarget);
                break;
            }
            case Arm64ConditionCode.MI:
            {
               builder.Compare(state.Address, state.Dest, InstructionSetIndependentOperand.MakeImmediate(0));
               builder.JumpIfLess( addr , branchTarget);
                break;
            }
            default:
                    throw   new Exception($"不支持的条件码 {conditionCode} 用于地址0x{addr:X} :   state "+state);
        }
    }

    public void GenerateConditionalSelect(IsilBuilder builder, ulong addr, FlagsState state, InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue, InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode)
    {
        throw new System.NotImplementedException();
    }
}
