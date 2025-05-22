using System;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public class CompareProcessor : IFlagsProcessor
{
    private bool IsZeroImmValue(InstructionSetIndependentOperand operand)
    {
        if (operand.Type == InstructionSetIndependentOperand.OperandType.Immediate)
        {
            var d = operand.Data is IsilImmediateOperand data ? data : default;
            if (Convert.ToUInt64(d.Value) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /**
     * CMP X21, X0 比较指令
     */
    public void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode,
        ulong branchTarget,
        ulong addr)
    {
        switch (conditionCode)
        {
            case Arm64ConditionCode.LE: // 有符号 <=
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.JumpIfLessOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.GE: // 有符号 >=
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.JumpIfGreaterOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.GT: // 有符号 >
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.JumpIfGreater(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.MI:
            {
                //比较结果是否是负数
                // FCMP            S8, #0.0
                // il2cpp:00000000010A0070 64 02 00 54                   B.MI            loc_10A00BC

                if (IsZeroImmValue(state.Src2)) //因为一个数-0 是没有意义的
                {
                    builder.Compare(state.Address, state.Src1, InstructionSetIndependentOperand.MakeImmediate(0));
                    builder.JumpIfLess(addr, branchTarget);
                    break;
                }

                //检查是否是负数
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");

                builder.Subtract(state.Address, temp, state.Src1, state.Src2);
                builder.Compare(state.Address, temp, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfLess(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.EQ: // ==
            {
                builder.Compare(state.Address, state.Src1, state.Src2);

                builder.JumpIfEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.NE: //!=
            {
                builder.Compare(state.Address, state.Src1, state.Src2);

                builder.JumpIfNotEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.CC:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                //无符号小于
                builder.JumpIfLess(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.LT: // 有符号 < (N≠V)
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.JumpIfLess(addr, branchTarget);
                break;
            case Arm64ConditionCode.LS: // 无符号 <=
            {
                builder.Compare(state.Address, state.Src1, state.Src2);

                builder.JumpIfLessOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.CS:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);

                builder.JumpIfGreaterOrEqual(addr, branchTarget);
                break;
            }
            default:
                throw new Exception(" Unsupported condition code for compare and jump : " + conditionCode
                    + " Compare Addr " + state.Address.ToString("X") + " branch target " + branchTarget.ToString("X"));
        }
    }

    /**
     * CSET  X0, EQ
     * CSEL X0, X1, X2, EQ
     */
    public void GenerateConditionalSelect(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue, InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode)
    {
        switch (conditionCode)
        {
            case Arm64ConditionCode.NE:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.AssignIfNotEqual(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.EQ:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.AssignIfEqual(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.LT:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.AssignIfLessThan(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.GT:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.AssignIfGreaterThan(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.GE:
            {
                builder.Compare(state.Address, state.Src1, state.Src2);
                builder.AssignIfGreaterOrEqual(addr, dest, trueValue, falseValue);
                break;
            }
            default:
                throw new Exception(" Unsupported condition code for conditional select : " + conditionCode
                    + " Compare Addr " + state.Address.ToString("X") + " dest " + dest.ToString() + " trueValue " +
                    trueValue.ToString() + " falseValue " + falseValue.ToString());
        }
    }
}
