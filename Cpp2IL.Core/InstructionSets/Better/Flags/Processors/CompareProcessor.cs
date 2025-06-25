using System;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Disarm;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public class CompareProcessor : BaseProcessor
{
    public readonly string ConditionalSelectTemp = "ConditionalSelectTemp";
    public readonly string CompareTemp = "CompareTemp";
    public readonly string CompareTemp1 = "CompareTemp1";
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

    private Il2CppTypeEnum GetCastType(InstructionSetIndependentOperand operand, bool isSigned)
    {
        if (operand.IsXRegister())
        {
            return isSigned ? Il2CppTypeEnum.IL2CPP_TYPE_I8 : Il2CppTypeEnum.IL2CPP_TYPE_U8;
        }

        if (operand.IsWRegister())
        {
            return isSigned ? Il2CppTypeEnum.IL2CPP_TYPE_I4 : Il2CppTypeEnum.IL2CPP_TYPE_U4;
        }

        throw new Exception("不支持的寄存器类型 " + operand.ToString());
    }

    
    /**
     * 符号转换预处理 
     */
    private void CMPCastTypeIfNeed(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode)
    {
        if (state.SourceMnemonic == Arm64Mnemonic.CMP && conditionCode != Arm64ConditionCode.EQ
                                                      && conditionCode != Arm64ConditionCode.NE)
        {
            //只有CMP指令才需要
            
            var castType = GetCastType(state.OriSrc1, IsSignedConditionCode(conditionCode));
            var temp1 = InstructionSetIndependentOperand.MakeRegister(CompareTemp);
            builder.CastType(state.Address, temp1, state.Src1!.Value,
                InstructionSetIndependentOperand.MakeCastType(castType));
            state.OverrideSrc1= temp1;
            if (!state.OriSrc2!.IsImmediate() && !state.OriSrc2.IsZeroRegister())
            {
                var castType2= GetCastType(state.OriSrc2, IsSignedConditionCode(conditionCode));
                var temp2=InstructionSetIndependentOperand.MakeRegister(CompareTemp1);
                builder.CastType(state.Address, temp2, state.Src2.Value,
                    InstructionSetIndependentOperand.MakeCastType(castType2));
                state.OverrideSrc2= temp2;
            }
        }
    }

    /**
     * CMP X21, X0 比较指令
     */
    public override void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode,
        ulong branchTarget,
        ulong addr)
    {
        CMPCastTypeIfNeed(builder, state, conditionCode);
        switch (conditionCode)
        {
            case Arm64ConditionCode.LE: // 有符号 <=
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.JumpIfLessOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.GE: // 有符号 >=
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.JumpIfGreaterOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.GT: // 有符号 >
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.JumpIfGreater(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.HI:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                //无符号大于
                builder.JumpIfGreater(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.MI:
            {
                //比较结果是否是负数
                // FCMP            S8, #0.0
                // il2cpp:00000000010A0070 64 02 00 54                   B.MI            loc_10A00BC

                if (IsZeroImmValue(state.Arg2.Value)) //因为一个数-0 是没有意义的
                {
                    builder.Compare(state.Address, state.Arg1.Value, InstructionSetIndependentOperand.MakeImmediate(0));
                    builder.JumpIfLess(addr, branchTarget);
                    break;
                }

                //检查是否是负数
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");

                builder.Subtract(state.Address, temp, state.Arg1!.Value, state.Arg2.Value);
                builder.Compare(state.Address, temp, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfLess(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.EQ: // ==
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);

                builder.JumpIfEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.NE: //!=
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);

                builder.JumpIfNotEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.CC:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                //无符号小于
                builder.JumpIfLess(addr, branchTarget);
               
                break;
            }
            case Arm64ConditionCode.LT: // 有符号 < (N≠V)
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.JumpIfLess(addr, branchTarget);
                break;
            case Arm64ConditionCode.LS: // 无符号 <=
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);

                builder.JumpIfLessOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.PL:
            {
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");

                builder.Subtract(state.Address, temp, state.Arg1!.Value, state.Arg2!.Value);
                builder.Compare(state.Address, temp, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfGreaterOrEqual(addr, branchTarget);
                break;
            }
            case Arm64ConditionCode.CS:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);

                builder.JumpIfGreaterOrEqual(addr, branchTarget);
                break;
            }
            default:
                throw new Exception(" Unsupported condition code for compare and jump : " + conditionCode
                    + " Compare Addr " + state.Address.ToString("X") + " branch target " + branchTarget.ToString("X"));
        }
    }

    public override void GenerateConditionalIncrement(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest, InstructionSetIndependentOperand source, Arm64ConditionCode conditionCode)
    {
        throw new Exception("not impl !!");
    }

    public override void GenerateConditionalIncrement2Args(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest, InstructionSetIndependentOperand arg1, InstructionSetIndependentOperand arg2,
        Arm64ConditionCode conditionCode)
    {
        throw new NotImplementedException();
    }

    public override void GenerateConditionalNegate(IsilBuilder builder, ulong addr, FlagsState state, InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand source, Arm64ConditionCode conditionCode)
    {
        switch (conditionCode)
        {

            case Arm64ConditionCode.NE:
            {
                
                break;
            }
        }
    }

    /**
     * CSET  X0, EQ
     * CSEL X0, X1, X2, EQ
     */
    public override void GenerateConditionalSelect(IsilBuilder builder, ulong addr, FlagsState state,
        InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue, InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode)
    {
        switch (conditionCode)
        {
            case Arm64ConditionCode.NE:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfNotEqual(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.EQ:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfEqual(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.PL:
            {   
                var temp= InstructionSetIndependentOperand.MakeRegister(ConditionalSelectTemp);
                builder.Subtract(state.Address, temp, state.Arg1.Value, state.Arg2.Value);
                builder.Compare(state.Address, temp, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.AssignIfGreaterOrEqual(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.LT:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfLessThan(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.GT:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfGreaterThan(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.HI:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                //无符号大于
                builder.AssignIfGreaterThan(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.MI:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfLessThan(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.GE:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfGreaterOrEqual(addr, dest, trueValue, falseValue);
                break;
            }
            case Arm64ConditionCode.LE:
            {
                builder.Compare(state.Address, state.Arg1.Value, state.Arg2.Value);
                builder.AssignIfLessOrEqual(addr, dest, trueValue, falseValue);
                break;
            }
            default:
                throw new Exception(" Unsupported condition code for conditional select : " + conditionCode
                    + " Compare Addr " + state.Address.ToString("X") + " dest " + dest.ToString() + " trueValue " +
                    trueValue.ToString() + " falseValue " + falseValue.ToString());
        }
    }
}
