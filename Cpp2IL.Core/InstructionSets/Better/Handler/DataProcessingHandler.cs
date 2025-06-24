using System;
using System.Globalization;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Disarm;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// 数据处理指令处理器，负责处理算术和逻辑运算指令
/// </summary>
public class DataProcessingHandler : BaseArm64InstructionHandler
{
    private readonly string TEMP_DEST = "TEMP_DEST";

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
                Arm64Mnemonic.FNEG or Arm64Mnemonic.FMAX or
                Arm64Mnemonic.FMUL or Arm64Mnemonic.FDIV => true,

            // 逻辑运算指令 
            Arm64Mnemonic.AND or Arm64Mnemonic.ANDS or
                Arm64Mnemonic.ORR or Arm64Mnemonic.EOR or
                Arm64Mnemonic.BIC or Arm64Mnemonic.ORN => true,
            Arm64Mnemonic.LSR or Arm64Mnemonic.LSL => true,
            //Mov
            Arm64Mnemonic.FMOV or Arm64Mnemonic.MOV or Arm64Mnemonic.MOVN or Arm64Mnemonic.MOVI
                or Arm64Mnemonic.MOVK => true,
            Arm64Mnemonic.ADRP or Arm64Mnemonic.ADR => true,
            Arm64Mnemonic.BFM => true,
            // Arm64Mnemonic.UBFM => true,
            Arm64Mnemonic.INS => true,
            Arm64Mnemonic.DUP => true,
            //SMID
            Arm64Mnemonic.REV64 => true,
            Arm64Mnemonic.UZP1 => true,
            Arm64Mnemonic.FRINTP => true,
            Arm64Mnemonic.FRINTM => true,
            _ => false
        };
    }

    public override bool Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.LSL:
            {
                builder.LSL(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction, 1), // 源寄存器
                    ConvertOperand(instruction, 2) // 移位量
                );
                break;
            }
            case Arm64Mnemonic.LSR:
            {
                builder.LSR(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction, 1), // 源寄存器
                    ConvertOperand(instruction, 2) // 移位量
                );
                break;
            }
            case Arm64Mnemonic.FRINTM:
            {
                // 处理向下取整指令
                builder.FloorVector(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction, 1) // 源寄存器
                );
                break;
            }
            case Arm64Mnemonic.FRINTP:
            {
                builder.CeilingVector(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction, 1) // 源寄存器
                );
                break;
            }
            case Arm64Mnemonic.UZP1:
            {
                builder.UZP1(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction, 1), // 第一个源寄存器
                    ConvertOperand(instruction, 2) // 第二个源寄存器
                );
                break;
            }
            case Arm64Mnemonic.REV64:
            {
                builder.REV64(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction, 1) // 源寄存器
                );
                break;
            }
            case Arm64Mnemonic.MADD:
            {
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                // 处理乘加指令
                builder.Multiply( instruction.Address, temp,
                    ConvertOperand(instruction, 1), // 第一个源寄存器
                    ConvertOperand(instruction, 2) // 第二个源寄存器
                );
                builder.Add(instruction.Address,
                    ConvertOperand(instruction, 0), // 目标寄存器
                    ConvertOperand(instruction,3), // 临时寄存器，存储乘法结果
                    temp // 第三个源寄存器
                );
                // builder.MADD(instruction.Address,
                //     ConvertOperand(instruction, 0), // 目标寄存器
                //     ConvertOperand(instruction, 1), // 第一个源寄存器
                //     ConvertOperand(instruction, 2), // 第二个源寄存器
                //     ConvertOperand(instruction, 3) // 第三个源寄存器
                // );

                break;
            }
            case Arm64Mnemonic.FMAX:
            {
                // 处理浮点最大值指令
                var dest = ConvertOperand(instruction, 0);
                var src1 = ConvertOperand(instruction, 1);
                var src2 = ConvertOperand(instruction, 2);
                builder.FMAX(instruction.Address, dest, src1, src2);
                break;
            }
            case Arm64Mnemonic.INS:
            {
                ProcessINS(instruction, builder);
                break;
            }
            // case Arm64Mnemonic.BFM:
            // {
            //     ProcessBFM(instruction, builder);
            //     break;
            // }
            // 加法指令
            case Arm64Mnemonic.UBFM:
            {
                ProcessUBFM(instruction, builder);
                break;
            }
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
                    InstructionSetIndependentOperand.MakeRegister("WZR"), src);
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
            case Arm64Mnemonic.DUP:
            {
                ProcessDUP(instruction, builder);

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

        if (instruction.IsArithmeticInstruction())
        {
            return true;
        }

        return false;
        // FlagsManager.IsArithmeticInstruction()
    }

    private void ProcessDUP(Arm64Instruction instruction, IsilBuilder builder)

    {
        if (instruction.IsMoveVectorElementToRegister()) //  DUP             S1, V0.S[1]  
        {
            builder.VectorElementLoad(instruction.Address,
                ConvertOperand(instruction, 0), // 目标寄存器
                ConvertOperand(instruction, 1) // 源寄存器
            );
            return;
        }

        // DUP V0.2D, W9 
        if (instruction.IsMoveRegisterToVectorArrangement())
        {
            builder.LoadRegisterToVector(instruction.Address,
                ConvertOperand(instruction, 0), // 目标寄存器
                ConvertOperand(instruction, 1) // 源寄存器
            );
            return;
        }

        throw new Exception("not support ins " + instruction);
        // DUP V0.2D, W9 
        // 将 W9 的值复制到 V0 的 2D 向量寄存器中
        // var arg1 = ConvertOperand(instruction, 1);
        // if (arg1.IsImmediate())
        // {
        //     builder.LoadImmToVector(instruction.Address,
        //         ConvertOperand(instruction, 0), arg1);
        // }
        //     
        // builder.LoadRegisterToVector(instruction.Address,
        //     ConvertOperand(instruction, 0), // 目标寄存器
        //     ConvertOperand(instruction, 1) // 源寄存器
        // );
    }

    private void ProcessINS(Arm64Instruction instruction, IsilBuilder builder)
    {
        if (instruction.IsMoveVectorElementToVectorElement()) //INS V0.S[1], V1.S[0]    
        {
            builder.VectorElementLoad(instruction.Address,
                ConvertOperand(instruction, 0), // 目标寄存器
                ConvertOperand(instruction, 1) // 源寄存器
            );
            return;
        }

        if (instruction.IsMoveRegisterToVectorElement())
        {
            //INS V0.S[1], W3

            // 将 W3 的值插入到 V0 的 S[1] 元素中
            builder.VectorElementLoad(instruction.Address,
                ConvertOperand(instruction, 0), // 目标寄存器
                ConvertOperand(instruction, 1) // 源寄存器
            );
            return;
        }


        throw new Exception("not support ! " + instruction + " Element? " + instruction.Op0VectorElement +
                            " Element? " + instruction.Op1VectorElement);
    }

    private void ProcessBFM(Arm64Instruction instruction, IsilBuilder builder)
    {
        // BFM Rd, Rn, #immr, #imms
        // 位域移动指令：将Rn中的位域插入到Rd的指定位置
        // 例如：BFM W9, W8, 0x10, 0x1F
        var dest = ConvertOperand(instruction, 0); // 目标寄存器Rd
        var src = ConvertOperand(instruction, 1); // 源寄存器Rn
        var immr = (int)instruction.Op2Imm; // 起始位位置（右旋转量）
        var imms = (int)instruction.Op3Imm; // 结束位位置

        builder.BFM(instruction.Address, dest, src,
            InstructionSetIndependentOperand.MakeImmediate(immr),
            InstructionSetIndependentOperand.MakeImmediate(imms));
    }

    private void ProcessUBFM(Arm64Instruction instruction, IsilBuilder builder)
    {
        // UBFM Wd, Wn, #immr, #imms
        // 无符号位域移动：从Wn提取位域[imms:immr]，放到Wd的低位，高位清零
        // 例如：UBFM W27, W8, 0x2, 0x11 从W8提取[17:2]共16位，放到W27[15:0]
        var dest = ConvertOperand(instruction, 0);
        var src = ConvertOperand(instruction, 1);
        var immr = (int)instruction.Op2Imm; // 起始位
        var imms = (int)instruction.Op3Imm; // 结束位

        // 计算位域宽度
        int width = imms - immr + 1;

        if (width <= 0 || width > 32)
        {
            builder.NotImplemented(instruction.Address, $"无效的UBFM位域宽度: {width}");
            return;
        }

        var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");

        // 第一步：转换为无符号类型
        builder.CastType(instruction.Address, temp, src,
            InstructionSetIndependentOperand.MakeCastType(Il2CppTypeEnum.IL2CPP_TYPE_U4));

        // 第二步：右移immr位，将目标位域移动到低位
        // 例如：W8 >> 2，将[17:2]移动到[15:0]
        if (immr > 0)
        {
            builder.ShiftRight(instruction.Address, temp, InstructionSetIndependentOperand.MakeImmediate(immr));
        }

        // 第三步：使用掩码提取width位，清除高位
        // 例如：result & 0xFFFF (提取16位)
        uint mask = (1u << width) - 1;
        builder.And(instruction.Address, dest, temp,
            InstructionSetIndependentOperand.MakeImmediate(mask));
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
        // MOVK Wd, #imm, LSL #shift
        // 将16位立即数插入到目标寄存器的指定16位段中，保持其他位不变
        // 例如：MOVK W26, #0x9E5D,LSL#16 将0x9E5D插入到W26[31:16]，保持W26[15:0]不变

        var dest = ConvertOperand(instruction, 0);
        var imm = ConvertOperand(instruction, 1);
        //MOVK 专门用一个指令处理MOVK吧  要不然很复杂！
        int shiftAmount = 0;
        if (instruction.Op1ShiftType == Arm64ShiftType.LSL)
        {
            shiftAmount = (int)instruction.MemExtendOrShiftAmount;
        }
        else
        {
            throw new Exception("not support MOVK shift type: " + instruction.Op1ShiftType);
        }

        builder.MOVK(instruction.Address, dest, imm,
            InstructionSetIndependentOperand.MakeImmediate(shiftAmount));
    }

    private void ProcessMOVI(Arm64Instruction instruction, IsilBuilder builder)
    {
        var immData = ConvertOperand(instruction, 1).FixZero(true);
        if (immData.IsZeroRegister())
        {
            //如果是立即数是0 无需 进行位移 
            builder.LoadImmToVector(instruction.Address,
                ConvertOperand(instruction, 0), immData);
        }
        else
        {
            //计算立即数
            if (immData.IsImmediate())
            {
                var imm = immData.Data is IsilImmediateOperand data ? data : default;
                var int32 = imm.Value!.ToInt32(CultureInfo.InvariantCulture);
                if (instruction.Op1ShiftType != Arm64ShiftType.NONE)
                {
                    var result = instruction.GetShiftTypeValue(instruction.Op1ShiftType,
                        instruction.MemExtendOrShiftAmount, int32);
                    builder.LoadImmToVector(instruction.Address,
                        ConvertOperand(instruction, 0),
                        InstructionSetIndependentOperand.MakeImmediate(result));
                }
                else
                {
                    builder.LoadImmToVector(instruction.Address,
                        ConvertOperand(instruction, 0),
                        InstructionSetIndependentOperand.MakeImmediate(int32));
                }
            }
            else
            {
                throw new Exception(" arg 1 it's not immediate " + immData);
            }
        }
        // if (instruction.Op0Reg.ToString().StartsWith("V"))
        // {
        //     var arrangement = instruction.Op0Arrangement;
        //     if (arrangement == Arm64ArrangementSpecifier.TwoD)
        //     {
        //         //it's mean use 128 bit
        //         //MOV V0, #<imm>
        //         builder.Move(instruction.Address, ConvertOperand(instruction, 0),
        //             ConvertOperand(instruction, 1).FixZero(true));
        //     }
        // }
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
        if (!instruction.IsMoveVector16B())
        {
            if (instruction.IsLoadImmDataToVector()) //  FMOV            V1.4S, #1.0 强转！
            {
                var arg0 = ConvertOperand(instruction, 0);
                var arg1 = ConvertOperand(instruction, 1);
                if (arg1.IsImmediate())
                {
                    var imm = arg1.Data is IsilImmediateOperand data ? data : default;

                    if (instruction.Op0Arrangement == Arm64ArrangementSpecifier.FourS ||
                        instruction.Op0Arrangement == Arm64ArrangementSpecifier.TwoS)
                    {
                        if (imm.Value!.GetTypeCode() == TypeCode.Double)
                        {
                            var f = imm.Value!.ToSingle(CultureInfo.InvariantCulture);
                            //it's float ?
                            var bytes = BitConverter.GetBytes(f);
                            var i = BitConverter.ToInt32(bytes, 0);
                            // Logger.InfoNewline("MOV imm to vector: " + i);
                            builder.LoadImmToVector(instruction.Address,
                                ConvertOperand(instruction, 0),
                                InstructionSetIndependentOperand.MakeImmediate(i));
                            return;
                        }
                    }
                    throw new Exception("not support MOV arrangement: " +
                                        instruction.Op1Arrangement);
                }
            }
        }

        // //是否是向量操作？
        // if (instruction.IsMoveVector16B())
        // {
        //     // MOV             V8.16B, V0.16B
        //     builder.Move(instruction.Address, ConvertOperand(instruction, 0),
        //         ConvertOperand(instruction, 1)); // 直接将源寄存器的值加载到目标寄存器
        //     return;
        // }
        // if (instruction.IsMoveVectorElementToVectorArrangement()) // MOV             V4.2S, V4.S[0]
        // {
        //     builder.VectorElementLoad(instruction.Address, ConvertOperand(instruction, 0),
        //         ConvertOperand(instruction, 1));
        //     return;
        // }
        // if (instruction.IsVectorWithArrangement()) //V1.4S # 0.0 or V1.4S ,W9
        // {
        //     var arg1 = ConvertOperand(instruction, 1);
        //     var arg0 = ConvertOperand(instruction, 0);
        //     if (arg1.IsImmediate())
        //     {
        //         var imm = arg1.Data is IsilImmediateOperand data ? data : default;
        //
        //         if (instruction.Op0Arrangement == Arm64ArrangementSpecifier.FourS ||
        //             instruction.Op0Arrangement == Arm64ArrangementSpecifier.TwoS)
        //         {
        //             if (imm.Value!.GetTypeCode() == TypeCode.Double)
        //             {
        //                 var f = imm.Value!.ToSingle(CultureInfo.InvariantCulture);
        //                 //it's float ?
        //                 var bytes = BitConverter.GetBytes(f);
        //                 var i = BitConverter.ToInt32(bytes, 0);
        //                 // Logger.InfoNewline("MOV imm to vector: " + i);
        //                 builder.LoadImmToVector(instruction.Address,
        //                     ConvertOperand(instruction, 0),
        //                     InstructionSetIndependentOperand.MakeImmediate(i));
        //             }
        //             else
        //             {
        //                 throw new Exception("not support case");
        //             }
        //         }
        //         else
        //         {
        //             throw new Exception("not support MOV arrangement: " +
        //                                 instruction.Op1Arrangement);
        //         }
        //         // builder.LoadImmToVector(instruction.Address,
        //         //     ConvertOperand(instruction, 0), arg1);
        //     }
        //     else
        //     {
        //         //如果是向量操作，直接将寄存器的值加载到目标寄存器
        //         builder.LoadRegisterToVector(instruction.Address,
        //             ConvertOperand(instruction, 0), arg1.FixZero(true));
        //     }
        //
        //     return;
        // }
        //
        // if (instruction.IsVectorOperand()) //MOV V0.S[1], V1.S[0]
        // {
        //     Logger.InfoNewline("index ? " + instruction.Op0VectorElement.Index);
        //     builder.VectorElementLoad(instruction.Address, ConvertOperand(instruction, 0),
        //         ConvertOperand(instruction, 1));
        // }
        // else
        // {
        var ops = PreInstructionData(instruction, builder);
        builder.Move(instruction.Address, ops[0],
            IsUseZeroReg(instruction, out var zeroName)
                ? InstructionSetIndependentOperand.MakeImmediate(0)
                : ops[1]);
        // }
    }

    private InstructionSetIndependentOperand[] ProcessExtendedOrShift(Arm64Instruction instruction, IsilBuilder builder)
    {
        if (instruction.Mnemonic == Arm64Mnemonic.MOVZ || instruction.Mnemonic == Arm64Mnemonic.MOVN)
        {
            throw new Exception("未实现的移位/扩展类型: " + instruction.FinalOpShiftType + "/" +
                                instruction.FinalOpExtendType
                                + " kind ? " + instruction.Op3Kind);
        }

        //
        // 判断最终操作数是否有移位或扩展
        if (instruction.Mnemonic == Arm64Mnemonic.MOVK)
        {
            return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1) };
        }

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
            if (instruction.FinalOpShiftType==Arm64ShiftType.LSL)
            {
                var d = GetShiftTypeValue(instruction.FinalOpShiftType, Convert.ToInt32(shiftValue.Value));
                if (d == 0)
                {
                    return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), src };
                }
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                //优化下 当 shift 小于5的时候使用乘法
                if (Convert.ToInt32(shiftValue.Value)<=5) //使用乘法  通常是为了更方便的寻址
                {
                    builder.Multiply(instruction.Address, temp, src, InstructionSetIndependentOperand.MakeImmediate(d));
                    return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
                }
                if (Convert.ToInt32(shiftValue.Value)==32)
                {
                    //32的情况下 需要强制转换一下先
                    builder.CastType(instruction.Address, temp, src,
                        InstructionSetIndependentOperand.MakeCastType(Il2CppTypeEnum.IL2CPP_TYPE_I8));
                }
                else
                {
                    builder.Move(instruction.Address, temp, src);
                }
                builder.ShiftLeft(instruction.Address, temp,  InstructionSetIndependentOperand.MakeImmediate(Convert.ToInt32(shiftValue.Value)));
               
                return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
            }
            if (instruction.FinalOpShiftType==Arm64ShiftType.LSR)
            {
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.CastType( instruction.Address, temp, src,
                    InstructionSetIndependentOperand.MakeCastType(src.GetDefaultIl2CppType(true)));
                builder.ShiftRight(instruction.Address, temp,InstructionSetIndependentOperand.MakeImmediate(Convert.ToInt32(shiftValue.Value)));
                return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
            }

            if (instruction.FinalOpShiftType==Arm64ShiftType.ASR)
            {
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.CastType( instruction.Address, temp, src,
                    InstructionSetIndependentOperand.MakeCastType(src.GetDefaultIl2CppType(false)));
                builder.ShiftRight(instruction.Address, temp,InstructionSetIndependentOperand.MakeImmediate(Convert.ToInt32(shiftValue.Value)));
                return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
            }
            
        }

        // 最终操作数扩展类型 (FinalOpExtendType)： ADD X0, X1, W2, SXTW #3 符号拓展并位移
        if (instruction.FinalOpExtendType != Arm64ExtendType.NONE)
        {
            if (instruction.FinalOpExtendType == Arm64ExtendType.SXTW)
            {
                var src = ConvertOperand(instruction, 2);

                if (instruction.Op3Kind == Arm64OperandKind.None)
                {
                    var tempCastI64 = InstructionSetIndependentOperand.MakeRegister("TEMP");
                    // 扩展类型为 SXTW，表示符号拓展到 64 位
                    builder.CastType(instruction.Address, tempCastI64, src,
                        InstructionSetIndependentOperand.MakeCastType(Il2CppTypeEnum.IL2CPP_TYPE_I8));
                    return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), tempCastI64 };
                }

                //转换 
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

            if (instruction.FinalOpExtendType == Arm64ExtendType.UXTH)
            {
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                // 扩展类型为 UXTH，表示无符号拓展到 16 位
                builder.CastType(instruction.Address, temp, ConvertOperand(instruction, 2),
                    InstructionSetIndependentOperand.MakeCastType(Il2CppTypeEnum.IL2CPP_TYPE_U2));
                return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), temp };
            }
        }

        throw new Exception("未实现的移位/扩展类型: " + instruction.FinalOpShiftType + "/" +
                            instruction.FinalOpExtendType
                            + " kind ? " + instruction.Op3Kind);
    }

    private InstructionSetIndependentOperand[] HandleVectorArrangement(Arm64Instruction instruction,
        IsilBuilder builder)
    {
        return new[] { ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2) };
    }

    private InstructionSetIndependentOperand[] PreInstructionData(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 判断最终操作数是否有移位或扩展
        //
        if (instruction.IsVectorOperand()) //V0.S[1] V1.S[0]
        {
            // 如果是向量操作，直接返回操作数
            return new[]
            {
                ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2)
            };
        }

        var operands = ProcessExtendedOrShift(instruction, builder);
        return operands;
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
        if (instruction.IsVectorWithArrangement())
        {
            builder.SIMDMath(instruction.Address,
                ConvertOperand(instruction, 0),
                ConvertOperand(instruction, 1),
                ConvertOperand(instruction, 2),
                IsilMnemonic.Add);
            return;
        }

        var operands = PreInstructionData(instruction, builder);
        // 标准加法
        if (operands.Length == 0)
        {
            return;
        }

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
            if (instruction.IsVectorWithArrangement())
            {
                builder.SIMDMath(instruction.Address,
                    ConvertOperand(instruction, 0),
                    ConvertOperand(instruction, 1),
                    ConvertOperand(instruction, 2),
                    IsilMnemonic.Subtract);
                return;
            }

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
        if (instruction.IsVectorWithArrangement())
        {
            builder.SIMDMath(instruction.Address,
                ConvertOperand(instruction, 0),
                ConvertOperand(instruction, 1),
                ConvertOperand(instruction, 2),
                IsilMnemonic.Multiply);
            return;
        }

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
        if (instruction.IsVectorWithArrangement())
        {
            builder.SIMDMath(instruction.Address,
                ConvertOperand(instruction, 0),
                ConvertOperand(instruction, 1),
                ConvertOperand(instruction, 2),
                IsilMnemonic.Divide);
            return;
        }

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
