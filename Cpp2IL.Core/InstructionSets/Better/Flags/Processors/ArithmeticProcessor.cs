using System;
using Cpp2IL.Core.ISIL;
using Disarm;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets.Better.Flags.Processors;

public class ArithmeticProcessor : BaseProcessor
{
    public override void GenerateCompareAndJump(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode, ulong branchTarget,
        ulong addr)
    {
        
        ArithmeticCastTypeIfNeed(builder, state, conditionCode);
        switch (conditionCode)
        {

            case Arm64ConditionCode.CC:
            {
                //这里比较特殊需要强转成uint处理
              
                builder.Compare(state.Address, state.DestArg!.Value, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfEqual( addr , branchTarget); 
                break;
            }
            case Arm64ConditionCode.EQ: // 结果等于0
            {
                // 处理等于条件
                builder.Compare(state.Address, state.DestArg!.Value, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfEqual(addr , branchTarget);
                break;
            }
            case Arm64ConditionCode.NE: // 结果不等于0
            {
                // 处理不等于条件
                builder.Compare(state.Address, state.DestArg!.Value, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfNotEqual(addr , branchTarget);
                break;
            }
            case Arm64ConditionCode.PL:
            {
                // 处理大于等于条件
                builder.Compare(state.Address, state.DestArg!.Value, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfGreaterOrEqual(addr , branchTarget);
                break;
            }
            case Arm64ConditionCode.MI:
            {
               builder.Compare(state.Address, state.DestArg!.Value, InstructionSetIndependentOperand.MakeImmediate(0));
               builder.JumpIfLess( addr , branchTarget);
                break;
            }
            default:
                    throw   new Exception($"不支持的条件码 {conditionCode} 用于地址0x{addr:X} :   state "+state);
        }
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


    private bool IsXOrXRegister(InstructionSetIndependentOperand operand)
    {
        if (operand.IsWRegister()|| operand.IsXRegister())
        {
            return true;
        }

        return false;
    }
    private void ArithmeticCastTypeIfNeed(IsilBuilder builder, FlagsState state, Arm64ConditionCode conditionCode)
    {
        if ( conditionCode != Arm64ConditionCode.EQ && conditionCode != Arm64ConditionCode.NE
            && IsXOrXRegister(state.OriDest!.Value))
        {
            //只有运算结果是 X 或者W 寄存器才需要
            var castType = GetCastType(state.OriDest.Value, IsSignedConditionCode(conditionCode));
            var temp = InstructionSetIndependentOperand.MakeRegister("ArithmeticCompareTemp");
            builder.CastType(state.Address, temp, state.Dest!.Value,
                InstructionSetIndependentOperand.MakeCastType(castType));
            state.OverrideDest = temp;

        }
    }

    

    public override void GenerateConditionalSelect(IsilBuilder builder, ulong addr, FlagsState state, InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue, InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode)
    {
        switch (conditionCode)
        {
            case Arm64ConditionCode.NE:
            {
               builder.Compare( state.Address, state.DestArg!.Value, InstructionSetIndependentOperand.MakeImmediate(0));
                builder.AssignIfNotEqual( addr,dest, trueValue, falseValue);
                break;
            }
            default:
                throw new Exception(" 不支持的条件码 " + conditionCode + " 用于地址0x" + addr.ToString("X") + " :   state " + state);
        }
    }
}
