using System;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Disarm;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// 数据处理指令处理器，负责处理算术和逻辑运算指令
/// </summary>
public class DataProcessingHandler : BaseArm64InstructionHandler
{
    public DataProcessingHandler(FlagsStateManager flagsManager, BetterArmV8InstructionSet set) : base(flagsManager,
        set)
    {
    }


    public override bool CanHandle(Arm64Instruction instruction)
    {
        return instruction.Mnemonic switch
        {
            // 算术运算指令
            Arm64Mnemonic.ADD or Arm64Mnemonic.ADDS or Arm64Mnemonic.UDIV or Arm64Mnemonic.MSUB or
                Arm64Mnemonic.SUB or Arm64Mnemonic.SUBS or
                Arm64Mnemonic.MUL or Arm64Mnemonic.MADD or
                Arm64Mnemonic.FADD or Arm64Mnemonic.FSUB or
                Arm64Mnemonic.FABD or Arm64Mnemonic.FSQRT or Arm64Mnemonic.FMIN or
                Arm64Mnemonic.FNEG or
                Arm64Mnemonic.FMUL or Arm64Mnemonic.FDIV => true,

            // 逻辑运算指令 
            Arm64Mnemonic.AND or Arm64Mnemonic.ANDS or
                Arm64Mnemonic.ORR or Arm64Mnemonic.EOR or
                Arm64Mnemonic.BIC or Arm64Mnemonic.ORN => true,

            //Mov
            Arm64Mnemonic.FMOV or Arm64Mnemonic.MOV or Arm64Mnemonic.MOVN or Arm64Mnemonic.MOVI
                or Arm64Mnemonic.MOVK => true,
            Arm64Mnemonic.ADRP or Arm64Mnemonic.ADR => true,
            _ => false
        };
    }

    public override bool Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        switch (instruction.Mnemonic)
        {
            // 加法指令
            case Arm64Mnemonic.ADD:
            case Arm64Mnemonic.FADD:
                ProcessAdd(instruction, builder);
                break;

            case Arm64Mnemonic.ANDS:
                ProcessAnds(instruction, builder);
                break;
            case Arm64Mnemonic.ADDS:
                ProcessAdds(instruction, builder);
                break;

            // 减法指令
            case Arm64Mnemonic.SUB:
            case Arm64Mnemonic.FSUB:
                ProcessSubtract(instruction, builder);
                break;
            case Arm64Mnemonic.MOVK:
                ProcessMOVK(instruction, builder);
                break;
            case Arm64Mnemonic.SUBS:
                ProcessSubs(instruction, builder);
                break;
            case Arm64Mnemonic.FSQRT:
            {
                // 处理平方根指令
                ProcessFSQRT(instruction, builder);
                break;
            }
            case Arm64Mnemonic.FNEG:
            {
                // 处理浮点数取反指令 dest = 0 - src
                var dest = ConvertOperand(instruction, 0);
                var src = ConvertOperand(instruction, 1);
                builder.Subtract(instruction.Address, dest,
                    InstructionSetIndependentOperand.MakeImmediate(0), src);
                break;
            }
            case Arm64Mnemonic.FMIN:
            {
                // 处理浮点最小值指令
                var dest = ConvertOperand(instruction, 0);
                var src1 = ConvertOperand(instruction, 1);
                var src2 = ConvertOperand(instruction, 2);
                builder.FMIN(instruction.Address, dest, src1, src2);
                break;
            }
            // 乘法指令
            case Arm64Mnemonic.MUL:
            case Arm64Mnemonic.FMUL:
                ProcessMultiply(instruction, builder);
                break;

            // 乘加指令
            // case Arm64Mnemonic.MADD:
            //     ProcessMultiplyAdd(instruction, builder);
            //     break;

            // 除法指令
            case Arm64Mnemonic.UDIV:
            case Arm64Mnemonic.FDIV:
                ProcessDivide(instruction, builder);
                break;

            // 逻辑与指令
            case Arm64Mnemonic.AND:
                ProcessAnd(instruction, builder);
                break;
            //
            // case Arm64Mnemonic.ANDS:
            //     ProcessAnds(instruction, builder);
            //     break;

            // // 逻辑或指令
            case Arm64Mnemonic.ORR:
                ProcessOr(instruction, builder);
                break;

            // 逻辑异或指令
            case Arm64Mnemonic.EOR:
                ProcessXor(instruction, builder);
                break;
            //
            // // 位清除指令
            case Arm64Mnemonic.BIC:
                ProcessBitClear(instruction, builder);
                break;
            //
            // // 逻辑或非指令
            case Arm64Mnemonic.ORN:
                ProcessOrNot(instruction, builder);

                break;
            case Arm64Mnemonic.MOVI:
            {
                ProcessMOVI(instruction, builder);
                break;
            }
            case Arm64Mnemonic.MOVN:
            {
                // MOVN rd, #imm
                ProcessMovN(instruction, builder);
                break;
            }
            case Arm64Mnemonic.FMOV:
            case Arm64Mnemonic.MOV:
            {
                ProcessMov(instruction, builder);
                break;
            }
            case Arm64Mnemonic.FABD:
            {
                ProcessFABD(instruction, builder);
                break;
            }
            case Arm64Mnemonic.MSUB:
            {
                ProcessMSUB(instruction, builder);
                break;
            }
            case Arm64Mnemonic.ADR:
            case Arm64Mnemonic.ADRP:
            {
                ProcessAdrp(instruction, builder);
                break;
            }
            default:
                throw new NotImplementedException($"数据处理指令 {instruction.Mnemonic} 尚未实现 : " + instruction);
        }

        //当处理完的时候 需要检测是否设置了标志位

        if (FlagsManager.IsArithmeticInstruction(instruction))
        {
            return true;
        }

        return false;
        // FlagsManager.IsArithmeticInstruction()
    }

    private void ProcessFSQRT(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 处理平方根指令
        var dest = ConvertOperand(instruction, 0);
        var src = ConvertOperand(instruction, 1);
        builder.FSQRT(instruction.Address, dest, src);
    }

    private void ProcessMOVK(Arm64Instruction instruction, IsilBuilder builder)
    {
        //获取位移
        var operands = PreInstructionData(instruction, builder);
        var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
        var imm = ConvertOperand(instruction, 1);
        builder.Move(instruction.Address, temp, imm);
        builder.Not(instruction.Address, temp);
        builder.And(instruction.Address, temp, operands[0], temp);
        builder.Or(instruction.Address, operands[0], temp, imm);
    }

    private void ProcessMOVI(Arm64Instruction instruction, IsilBuilder builder)
    {
        if (instruction.Op0Reg.ToString().StartsWith("V"))
        {
            var arrangement = instruction.Op0Arrangement;
            if (arrangement == Arm64ArrangementSpecifier.TwoD)
            {
                //it's mean use 128 bit
                //MOV V0, #<imm>
                builder.Move(instruction.Address, ConvertOperand(instruction, 0),
                    ConvertOperand(instruction, 1).FixZero(true));
            }
        }
    }

    //MSUB W9, W26, W19, W21    ; W9 = W21 - (W26 × W19)
    private void ProcessMSUB(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 这里的减法是 W21 - (W26 × W19)
        var dest = ConvertOperand(instruction, 0);
        var src1 = ConvertOperand(instruction, 1);
        var src2 = ConvertOperand(instruction, 2);
        var src3 = ConvertOperand(instruction, 3);
        var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
        // 先计算乘法部分
        builder.Multiply(instruction.Address, temp, src1, src2);
        // 再进行减法
        builder.Subtract(instruction.Address, dest, src3, temp);
    }

    private void ProcessFABD(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.FABD(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }

    private void ProcessMovN(Arm64Instruction instruction, IsilBuilder builder)
    {
        // dest = ~src
        var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
        builder.Move(instruction.Address, temp, ConvertOperand(instruction, 1));
        builder.Not(instruction.Address, temp);
        builder.Move(instruction.Address, ConvertOperand(instruction, 0), temp);
    }

    private void ProcessAdrp(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Move(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
    }

    private void ProcessMov(Arm64Instruction instruction, IsilBuilder builder)
    {
        //是否是向量操作？
        
         var ops=PreInstructionData(instruction, builder);
         if (instruction.IsVectorOperand()) //MOV V0.S[1], V1.S[0]
         {
             builder.VectorElementLoad( instruction.Address, ConvertOperand(instruction, 0),
                 ConvertOperand(instruction, 1));
         }
         else
         {
             builder.Move(instruction.Address, ops[0],
                 IsUseZeroReg(instruction, out var zeroName)
                     ? InstructionSetIndependentOperand.MakeImmediate(0)
                     : ops[1]);
         }
       
    }

    private InstructionSetIndependentOperand[] ProcessExtendedOrShift(Arm64Instruction instruction, IsilBuilder builder)
    {
        //
        // 判断最终操作数是否有移位或扩展

        bool hasFinalOpShiftOrExtend = instruction.FinalOpShiftType != Arm64ShiftType.NONE ||
                                       instruction.FinalOpExtendType != Arm64ExtendType.NONE;


// 判断任何操作数是否有移位
        bool hasOperandShift = instruction.Op0ShiftType != Arm64ShiftType.NONE ||
                               instruction.Op1ShiftType != Arm64ShiftType.NONE ||
                               instruction.Op2ShiftType != Arm64ShiftType.NONE ||
                               instruction.Op3ShiftType != Arm64ShiftType.NONE ||
                               instruction.Op4ShiftType != Arm64ShiftType.NONE;
        if (!hasFinalOpShiftOrExtend && !hasOperandShift)
        {
            // 没有移位或扩展，直接返回操作数
            return new[]
            {
                ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2)
            };
        }

        // 最终操作数移位类型 (FinalOpShiftType)： ADD X0, X1, X2, LSL #4 
        if (instruction.FinalOpShiftType != Arm64ShiftType.NONE)
        {
            var src = ConvertOperand(instruction, 2);
            var shiftValue = ConvertOperand(instruction, 3).Data is IsilImmediateOperand
                ? (IsilImmediateOperand)ConvertOperand(instruction, 3).Data
                : default;
            var d = GetShiftTypeValue(instruction.FinalOpShiftType, Convert.ToInt32(shiftValue.Value));
            if (d == 0)
            {
                return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), src };
            }

            var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
            builder.Multiply(instruction.Address, temp, src, InstructionSetIndependentOperand.MakeImmediate(d));
            if (hasOperandShift)
            {
                throw new Exception(" error ??");
            }

            return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
        }

        // 最终操作数扩展类型 (FinalOpExtendType)： ADD X0, X1, W2, SXTW #3 符号拓展并位移
        if (instruction.FinalOpExtendType != Arm64ExtendType.NONE)
        {
            if (instruction.FinalOpExtendType == Arm64ExtendType.SXTW)
            {
                //转换
                var src = ConvertOperand(instruction, 2);
                var shiftValue = ConvertOperand(instruction, 3).Data is IsilImmediateOperand
                    ? (IsilImmediateOperand)ConvertOperand(instruction, 3).Data
                    : default;
                var d = GetExtendTypeValue(instruction.FinalOpExtendType, Convert.ToInt32(shiftValue.Value));
                if (d == 0)
                {
                    return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), src };
                }

                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");

                builder.Multiply(instruction.Address, temp, src, InstructionSetIndependentOperand.MakeImmediate(d));
                return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
            }
        }

        throw new Exception("未实现的移位/扩展类型: " + instruction.FinalOpShiftType + "/" +
                            instruction.FinalOpExtendType
                            + " kind ? " + instruction.Op3Kind);
    }

    private InstructionSetIndependentOperand[] PreInstructionData(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 判断最终操作数是否有移位或扩展
        Logger.InfoNewline(" PreInstructionData " + instruction +" is ? "+instruction.IsVectorOperand());
        if (instruction.IsVectorOperand())
        {
            // 如果是向量操作，直接返回操作数
            return new[]
            {
                ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2)
            }; 
        }
        else
        {
            var operands = ProcessExtendedOrShift(instruction, builder);
            return operands;
        }
    }

    private double GetExtendTypeValue(Arm64ExtendType extendType, int extendAmount)
    {
        if (extendType == Arm64ExtendType.SXTW)
        {
            var result = Math.Pow(2, Convert.ToInt64(extendAmount));
            return result;
        }

        throw new Exception(" not support GetExtendTypeValue " + extendType);
    }

    private double GetShiftTypeValue(Arm64ShiftType shiftType, int shiftAmount)
    {
        if (shiftType == Arm64ShiftType.LSL)
        {
            var result = Math.Pow(2, Convert.ToInt64(shiftAmount));
            return result;
        }

        throw new Exception(" not support GetShiftTypeValue " + shiftType);
    }

    /// <summary>
    /// 处理加法指令 (ADD/FADD)
    /// </summary>
    private void ProcessAdd(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);
        // 标准加法
        builder.Add(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理带标志位的加法指令 (ADDS)
    /// </summary>
    private void ProcessAdds(Arm64Instruction instruction, IsilBuilder builder)
    {
        var dest = ConvertOperand(instruction, 0);
        if (IsZeroReg(dest, out var name))
        {
            // 如果目标寄存器是零寄存器，直接将源操作数1赋值给目标 ADDS 的操作仅仅是为了设置标志位  但是我们需要用一个临时变量来过渡
            var zoperands = PreInstructionData(instruction, builder);
            // 标准加法，但会设置标志位
            builder.Add(instruction.Address,
                InstructionSetIndependentOperand.MakeRegister("TEMP"),
                zoperands[1],
                zoperands[2]);
            return;
        }

        var operands = PreInstructionData(instruction, builder);
        // 标准加法，但会设置标志位
        builder.Add(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }


    /// <summary>
    /// 处理减法指令 (SUB/FSUB)
    /// </summary>
    private void ProcessSubtract(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);
        var arg0 = operands[0];
        if (IsZeroReg(arg0, out _)) //栈操作
        {
            // 标准减法
            builder.Subtract(instruction.Address,
                operands[0],
                operands[1],
                operands[2]);
        }
        else
        {
            builder.Subtract(instruction.Address,
                operands[0],
                operands[1].FixZero(true),
                operands[2]);
        }
    }

    /// <summary>
    /// 处理带标志位的减法指令 (SUBS)
    /// </summary>
    private void ProcessSubs(Arm64Instruction instruction, IsilBuilder builder)
    {
        var dest = ConvertOperand(instruction, 0);
        if (IsZeroReg(dest, out var name))
        {
            // 如果目标寄存器是零寄存器，直接将源操作数1赋值给目标 ADDS 的操作仅仅是为了设置标志位  但是我们需要用一个临时变量来过渡
            var zoperands = PreInstructionData(instruction, builder);
            // 标准加法，但会设置标志位
            builder.Subtract(instruction.Address,
                InstructionSetIndependentOperand.MakeRegister("TEMP"),
                zoperands[1],
                zoperands[2]);
            return;
        }

        var operands = PreInstructionData(instruction, builder);

        builder.Subtract(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
        // 标准减法，但会设置标志位
        Logger.InfoNewline(" Call ProcessSubs !!!");
    }

    /// <summary>
    /// 处理乘法指令 (MUL/FMUL)
    /// </summary>
    private void ProcessMultiply(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);
        builder.Multiply(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理乘加指令 (MADD)
    /// </summary>
    private void ProcessMultiplyAdd(Arm64Instruction instruction, IsilBuilder builder)
    {
        // MADD rd, rn, rm, ra  =>  rd = (rn * rm) + ra
        var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");

        // 先计算乘法部分
        builder.Multiply(instruction.Address, tempReg,
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));

        // 再进行加法
        builder.Add(instruction.Address,
            ConvertOperand(instruction, 0),
            tempReg,
            ConvertOperand(instruction, 3));
    }

    /// <summary>
    /// 处理除法指令 (FDIV\UDIV)
    /// </summary>
    private void ProcessDivide(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);

        builder.Divide(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理逻辑与指令 (AND)
    /// </summary>
    private void ProcessAnd(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);
        builder.And(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理带标志位的逻辑与指令 (ANDS)
    /// </summary>
    private void ProcessAnds(Arm64Instruction instruction, IsilBuilder builder)
    {
        var dest = ConvertOperand(instruction, 0);
        if (IsZeroReg(dest, out var name))
        {
            // 如果目标寄存器是零寄存器，直接将源操作数1赋值给目标 ANDS 的操作仅仅是为了设置标志位  但是我们需要用一个临时变量来过渡
            var zoperands = PreInstructionData(instruction, builder);
            // 标准加法，但会设置标志位
            builder.And(instruction.Address,
                InstructionSetIndependentOperand.MakeRegister("TEMP"),
                zoperands[1],
                zoperands[2]);
            return;
        }

        var operands = PreInstructionData(instruction, builder);

        builder.And(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理逻辑或指令 (ORR)
    /// </summary>
    private void ProcessOr(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);
        // 处理逻辑或
        builder.Or(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理逻辑异或指令 (EOR)
    /// </summary>
    private void ProcessXor(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);
        // 处理逻辑异或
        builder.Xor(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理位清除指令 (BIC) - 相当于AND NOT
    /// </summary>
    private void ProcessBitClear(Arm64Instruction instruction, IsilBuilder builder)
    {
        var operands = PreInstructionData(instruction, builder);

        // // BIC rd, rn, rm  =>  rd = rn & ~rm
        // var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");
        //
        // // 首先对第二个操作数取反
        builder.Not(instruction.Address, operands[2]);
        //
        // // 然后执行与操作
        builder.And(instruction.Address,
            operands[0],
            operands[1],
            operands[2]);
    }

    /// <summary>
    /// 处理逻辑或非指令 (ORN) - 相当于OR NOT
    /// </summary>
    private void ProcessOrNot(Arm64Instruction instruction, IsilBuilder builder)
    {
        // ORN rd, rn, rm  =>  rd = rn | ~rm
        // 处理有移位的情况
        var lsl = ConvertOperand(instruction, 3);
        if (lsl.Type == InstructionSetIndependentOperand.OperandType.Immediate)
        {
            var imm = GetShiftAmount(lsl);
            if (imm == 0) // 没有移位
            {
                // 创建临时寄存器存储取反后的值
                var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");
                var rmReg = ConvertOperand(instruction, 2);
                
                // 复制第二个操作数
                builder.Move(instruction.Address, tempReg, rmReg);

                // 对复制的值取反
                builder.Not(instruction.Address, tempReg);

                // 检查第一个操作数是否为零寄存器 ORN W8, WZR, W0   // W8 = ~W0（等同于 MVN W8, W0）
                if (IsZeroReg(ConvertOperand(instruction, 1), out var _))
                {
                    // 如果第一个操作数是零寄存器，直接将取反结果赋值给目标
                    builder.Move(instruction.Address, ConvertOperand(instruction, 0), tempReg);
                }
                else
                {
                    // 否则执行或操作
                    builder.Or(instruction.Address,
                        ConvertOperand(instruction, 0),
                        ConvertOperand(instruction, 1),
                        tempReg);
                }

                return;
            }
        }

        // 对于其他情况，生成未实现指令
        Logger.WarnNewline($"未处理的ORN指令: {instruction}");
        builder.NotImplemented(instruction.Address, $"未处理的ORN指令: {instruction}");
    }

    /// <summary>
    /// 获取移位量
    /// </summary>
    private int GetShiftAmount(InstructionSetIndependentOperand operand)
    {
        if (operand.Type == InstructionSetIndependentOperand.OperandType.Immediate)
        {
            if (operand.Data is IsilImmediateOperand immediateOperand)
            {
                return Convert.ToInt32(immediateOperand.Value);
            }
        }

        return 0; // 默认无移位
    }
}
