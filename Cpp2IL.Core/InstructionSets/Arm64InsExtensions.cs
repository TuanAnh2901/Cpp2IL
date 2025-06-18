using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cpp2IL.Core.ISIL;
using Disarm;
using Disarm.InternalDisassembly;

namespace Cpp2IL.Core.InstructionSets;

public static class Arm64InsExtensions
{
    
    public static bool IsCMP(this Arm64Instruction instruction)
    {
        // 列出所有比较指令
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.CMP:
            case Arm64Mnemonic.FCMP:
                return true;
            
            default:
                return false;
        }
    }
    public static bool IsArithmeticInstruction(this Arm64Instruction instruction)
    {
        // 列出所有算术指令
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.ANDS:
            case Arm64Mnemonic.ADDS:
            case Arm64Mnemonic.SUBS:
                return true;
            
            default:
                return false;
        }
    }
    public static bool IsUpdateFlagMnemonic(this Arm64Instruction instruction)
    {
        if (instruction.Mnemonic==Arm64Mnemonic.CMP
            || instruction.Mnemonic==Arm64Mnemonic.FCMP)
        {
            return true;
        }

        return instruction.IsArithmeticInstruction();
    }

    public static Dictionary<ulong,Arm64Instruction> GetUpdateFlagInstructions(this List<Arm64Instruction> arm64s)
    {
        var result = new Dictionary<ulong, Arm64Instruction>();
        foreach (var arm64 in arm64s)
        {
            if (arm64.IsUpdateFlagMnemonic())
            {
                result.Add(arm64.Address, arm64);
            }
        }

        return result;
    }

    private static int GetOperandRegisterSize(Arm64Instruction instruction)
    {
        if (instruction.Op1Kind == Arm64OperandKind.Register)
        {
            var reg = instruction.Op1Reg.ToString().ToUpperInvariant();
            if (reg.StartsWith("X"))
                return 8; //64 bit
            if (reg.StartsWith("W"))
            {
                return 4; //32 bit
            }

            if (reg.StartsWith("S"))
            {
                return 4; //32 bit
            }

            if (reg.StartsWith("D"))
            {
                return 8; //64 bit
            }

            if (reg.StartsWith("V"))
            {
                return 16; //128 bit
            }
        }

        throw new Exception("GetOperandRegisterSize not support for " + instruction.Op1Kind);
    }

    // public static InstructionSetIndependentOperand GetMemoryOperandSize(this Arm64Instruction instruction)
    // {
    //     switch (instruction.Mnemonic)
    //     {
    //         case Arm64Mnemonic.STR:
    //         {
    //             return InstructionSetIndependentOperand.MakeMemoryOperandSize(GetOperandRegisterSize(instruction));
    //         }
    //         default:
    //             throw new Exception("GetMemoryOperandSize not support for " + instruction.Mnemonic);
    //     }
    // }

    // private static InstructionSetIndependentOperand[] HandeAddTwoS(this Arm64Instruction instruction,
    //     IsilBuilder builder)
    // {
    //         
    // }
    public static InstructionSetIndependentOperand[] BuilderTempVectorArrangement(this Arm64Instruction instruction,
        IsilBuilder builder)
    {
        switch (instruction.Mnemonic)
        {
          
            //FADD            V0.2S, V0.2S, V4.2S FADD 只支持这个格式
            case Arm64Mnemonic.FADD:
            {
                var dest = InstructionSetIndependentOperand.MakeRegister("TempArrangementResult");
                var dest1 = InstructionSetIndependentOperand.MakeRegister("TempArrangement1");
                var dest2 = InstructionSetIndependentOperand.MakeRegister("TempArrangement2");

                builder.VectorElementLoad(instruction.Address, dest1,
                    InstructionSetIndependentOperand.MakeVectorElement(
                        instruction.Op1Reg.ToString(), IsilVectorRegisterElementOperand.VectorElementWidth.S, 1));
                builder.VectorElementLoad(instruction.Address, dest2,
                    InstructionSetIndependentOperand.MakeVectorElement(
                        instruction.Op2Reg.ToString(), IsilVectorRegisterElementOperand.VectorElementWidth.S, 1));

                builder.Add(instruction.Address, dest, dest1, dest2);
                //set to Vector 
                builder.VectorElementStore(instruction.Address, InstructionSetIndependentOperand.MakeVectorElement(
                    instruction.Op0Reg.ToString(), IsilVectorRegisterElementOperand.VectorElementWidth.S, 1), dest);

                builder.VectorElementLoad(instruction.Address, dest1,
                    InstructionSetIndependentOperand.MakeVectorElement(
                        instruction.Op1Reg.ToString(), IsilVectorRegisterElementOperand.VectorElementWidth.S, 0));
                builder.VectorElementLoad(instruction.Address, dest2,
                    InstructionSetIndependentOperand.MakeVectorElement(
                        instruction.Op2Reg.ToString(), IsilVectorRegisterElementOperand.VectorElementWidth.S, 0));
                builder.Add(instruction.Address, dest, dest1, dest2);
                builder.VectorElementStore(instruction.Address, InstructionSetIndependentOperand.MakeVectorElement(
                    instruction.Op0Reg.ToString(), IsilVectorRegisterElementOperand.VectorElementWidth.S, 0), dest);
                return Array.Empty<InstructionSetIndependentOperand>();
            }
            
        }

        throw new Exception("BuilderTempVectorArrangement not support ! " + instruction.Mnemonic);
    }

    public static bool IsSupportArrangementMath(this Arm64Instruction instruction)
    {
        return instruction.Mnemonic == Arm64Mnemonic.FADD || instruction.Mnemonic==Arm64Mnemonic.FMUL ;
    }

    public static int GetShiftTypeValue(this Arm64Instruction instruction,Arm64ShiftType shiftType, int shiftAmount, int imm)
    {
        switch (shiftType)
        {
            case Arm64ShiftType.LSL:
                return imm << shiftAmount;
            case Arm64ShiftType.LSR:
                return imm >> shiftAmount;
            case Arm64ShiftType.ASR:
                return imm >> shiftAmount;
            case Arm64ShiftType.ROR:
                return (imm >> shiftAmount) | (imm << (32 - shiftAmount));
            default:
                throw new ArgumentOutOfRangeException(nameof(shiftType), shiftType, null);
        }
    }

    public static bool IsMoveVector16B(this Arm64Instruction instruction)
    {
        if (instruction is { Op0Arrangement: Arm64ArrangementSpecifier.SixteenB, Op1Arrangement: Arm64ArrangementSpecifier.SixteenB })
        {
            return true;
        }

        return false;
    }
    public static bool IsMoveVectorElementToVectorArrangement(this Arm64Instruction instruction)
    {
        if (instruction.Op1Kind==Arm64OperandKind.VectorRegisterElement && instruction.Op0Arrangement!=Arm64ArrangementSpecifier.None)
        {

            return true;
        } 

        return false;
    }
    public static bool IsVectorWithArrangement(this Arm64Instruction instruction)
    {
        return instruction.Op0Arrangement != Arm64ArrangementSpecifier.None ||
               instruction.Op1Arrangement != Arm64ArrangementSpecifier.None ||
               instruction.Op2Arrangement != Arm64ArrangementSpecifier.None ||
               instruction.Op3Arrangement != Arm64ArrangementSpecifier.None;
    }

    public static bool IsVectorOperand(this Arm64Instruction instruction)
    {
        return instruction.Op0Kind == Arm64OperandKind.VectorRegisterElement ||
               instruction.Op1Kind == Arm64OperandKind.VectorRegisterElement ||
               instruction.Op2Kind == Arm64OperandKind.VectorRegisterElement ||
               instruction.Op3Kind == Arm64OperandKind.VectorRegisterElement;
    }

    private static string FixReg(this string reg)
    {
        if (reg.StartsWith("v"))
        {
            return reg.Replace("v", "s");
        }

        return reg;
    }

    private static void AppendMemory(Arm64Instruction instruction, StringBuilder sb)
    {
        sb.Append('[').Append(instruction.MemBase.ToString().ToLowerInvariant());

        if (instruction.MemAddendReg != Arm64Register.INVALID)
            sb.Append(", ").Append(instruction.MemAddendReg.ToString().ToLowerInvariant());

        if (instruction.MemOffset != 0)
        {
            if (instruction.MemOffset < 0)
            {
                if (instruction.MemOffset > -0x10)
                {
                    sb
                        .Append("#")
                        .Append(Math.Abs(instruction.MemOffset).ToString("X").ToLowerInvariant());
                }
                else
                {
                    sb
                        .Append("#0x")
                        .Append(Math.Abs(instruction.MemOffset).ToString("X").ToLowerInvariant());
                }
            }
            else
            {
                if (instruction.MemOffset >= 0x10)
                {
                    sb.Append(instruction.MemOffset < 0 ? ", #-" : ", #")
                        .Append("0x")
                        .Append(Math.Abs(instruction.MemOffset).ToString("X").ToLowerInvariant());
                }
                else
                {
                    sb.Append(", #").Append(instruction.MemOffset.ToString("X").ToLowerInvariant());
                }
            }
        }

        if (instruction.MemExtendType != Arm64ExtendType.NONE)
            sb.Append(", ").Append(instruction.MemExtendType.ToString().ToLowerInvariant());
        else if (instruction.MemShiftType != Arm64ShiftType.NONE)
            sb.Append(", ").Append(instruction.MemShiftType.ToString().ToLowerInvariant());

        if (instruction.MemExtendOrShiftAmount != 0)
            sb.Append(" #").Append(instruction.MemExtendOrShiftAmount.ToString().ToLowerInvariant());

        sb.Append(']');

        if (instruction.MemIsPreIndexed)
            sb.Append('!');
    }

    private static bool AppendOperand(Arm64Instruction instruction, StringBuilder sb, Arm64OperandKind kind,
        Arm64Register reg, Arm64VectorElement vectorElement, Arm64ArrangementSpecifier regArrangement,
        Arm64ShiftType shiftType, long imm, double fpImm, bool comma = false)
    {
        if (kind == Arm64OperandKind.None)
            return false;

        if (comma)
            sb.Append(", ");

        if (kind == Arm64OperandKind.Register)
        {
            sb.Append(reg.ToString().ToLowerInvariant().FixReg());

            if (regArrangement != Arm64ArrangementSpecifier.None)
                sb.Append('.').Append(regArrangement.ToDisassemblyString());
        }
        else if (kind == Arm64OperandKind.VectorRegisterElement)
        {
            sb.Append(reg)
                .Append('.')
                .Append(vectorElement);
        }
        else if (kind == Arm64OperandKind.Immediate)
        {
            if (shiftType != Arm64ShiftType.NONE)
                sb.Append(shiftType).Append(' ');
            sb.Append("0x").Append(imm.ToString("X").ToLowerInvariant());
        }
        else if (kind == Arm64OperandKind.FloatingPointImmediate)
        {
            sb.Append(fpImm.ToString(CultureInfo.InvariantCulture));
        }
        else if (kind == Arm64OperandKind.ImmediatePcRelative)
            sb.Append("0x").Append(((long)instruction.Address + imm).ToString("X").ToLowerInvariant());
        else if (kind == Arm64OperandKind.Memory)
            AppendMemory(instruction, sb);

        return true;
    }

    public static string FixString(Arm64Instruction instruction)
    {
        var sb = new StringBuilder();


        sb.Append(instruction.Mnemonic.ToString().ToLowerInvariant());

        if (instruction.MnemonicConditionCode != Arm64ConditionCode.NONE)
            sb.Append('.').Append(instruction.MnemonicConditionCode.ToString().ToLowerInvariant());

        sb.Append(' ');

        //Ew yes I'm using goto.
        if (!AppendOperand(instruction, sb, instruction.Op0Kind, instruction.Op0Reg, instruction.Op0VectorElement,
                instruction.Op0Arrangement, instruction.Op1ShiftType, instruction.Op0Imm, instruction.Op0FpImm))
            goto doneops;
        if (!AppendOperand(instruction, sb, instruction.Op1Kind, instruction.Op1Reg, instruction.Op1VectorElement,
                instruction.Op1Arrangement,
                instruction.Op1ShiftType, instruction.Op1Imm, instruction.Op1FpImm, true))
            goto doneops;
        if (!AppendOperand(instruction, sb, instruction.Op2Kind, instruction.Op2Reg, instruction.Op2VectorElement,
                instruction.Op2Arrangement,
                instruction.Op1ShiftType, instruction.Op2Imm, instruction.Op2FpImm, true))
            goto doneops;
        if (!AppendOperand(instruction, sb, instruction.Op3Kind, instruction.Op3Reg, instruction.Op3VectorElement,
                instruction.Op3Arrangement,
                instruction.Op1ShiftType, instruction.Op3Imm, instruction.Op3FpImm, true))
            goto doneops;

        doneops:
        if (instruction.FinalOpExtendType != Arm64ExtendType.NONE)
            sb.Append(", ").Append(instruction.FinalOpExtendType);
        else if (instruction.FinalOpShiftType != Arm64ShiftType.NONE)
            sb.Append(", ").Append(instruction.FinalOpShiftType);
        else if (instruction.FinalOpConditionCode != Arm64ConditionCode.NONE)
            sb.Append(", ").Append(instruction.FinalOpConditionCode);

        return sb.ToString();
    }
}
