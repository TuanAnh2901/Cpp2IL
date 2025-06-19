using System;
using Cpp2IL.Core.ISIL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets;

public static class InstructionSetIndependentOperandExtensions
{
    
    public static bool IsImmediate(this InstructionSetIndependentOperand operand)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Immediate, Data: IsilImmediateOperand immediateOperand
            })
        {
            return true;
        }

        return false;
    }

    public static Il2CppTypeEnum GetDefaultIl2CppType(this InstructionSetIndependentOperand operand)
    {
        if (operand.IsXRegister())
        {
            return Il2CppTypeEnum.IL2CPP_TYPE_I8;
        }

        if (operand.IsWRegister())
        {
            return Il2CppTypeEnum.IL2CPP_TYPE_I4;
        }

        throw new Exception("not support");
    }
    public static bool IsXRegister(this InstructionSetIndependentOperand operand)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.RegisterName.StartsWith("X"))
            {
                return true;
            }
           
        }

        return false;
    }
    public static bool IsZeroRegister(this InstructionSetIndependentOperand operand)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.IsZeroAlias)
            {
                return true;
            }
           
        }

        return false;
    }
    public static bool IsWRegister(this InstructionSetIndependentOperand operand)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.RegisterName.StartsWith("W"))
            {
                return true;
            }
           
        }

        return false;
    }
    public static InstructionSetIndependentOperand FixZero(this InstructionSetIndependentOperand operand,bool useZeroRegister=false)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.IsZeroAlias)
            {
                if (useZeroRegister)
                {
                    return InstructionSetIndependentOperand.MakeRegister(registerOperand.GetZeroRegName());
                }
                return InstructionSetIndependentOperand.MakeImmediate(0);
            }
        }

        return operand;
    }
}
