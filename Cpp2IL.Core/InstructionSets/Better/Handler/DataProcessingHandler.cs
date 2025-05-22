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
    public DataProcessingHandler(FlagsStateManager flagsManager) : base(flagsManager)
    { }
    
    public override bool CanHandle(Arm64Instruction instruction)
    {
        return instruction.Mnemonic switch
        {
            // 算术运算指令
            Arm64Mnemonic.ADD or Arm64Mnemonic.ADDS or
            Arm64Mnemonic.SUB or Arm64Mnemonic.SUBS or
            Arm64Mnemonic.MUL or Arm64Mnemonic.MADD or
            Arm64Mnemonic.FADD or Arm64Mnemonic.FSUB or
            Arm64Mnemonic.FMUL or Arm64Mnemonic.FDIV => true,
            
            // 逻辑运算指令
            Arm64Mnemonic.AND or Arm64Mnemonic.ANDS or
            Arm64Mnemonic.ORR or Arm64Mnemonic.EOR or
            Arm64Mnemonic.BIC or Arm64Mnemonic.ORN => true,
            
            //Mov
            Arm64Mnemonic.MOV =>true,
            Arm64Mnemonic.ADRP=>true,
            _ => false
        };
    }
    
    public override void Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        // 检查并记录设置标志位的指令
        HandleFlagsIfNeeded(instruction, builder);
        
        switch (instruction.Mnemonic)
        {
            // 加法指令
            case Arm64Mnemonic.ADD:
            case Arm64Mnemonic.FADD:
                ProcessAdd(instruction, builder);
                break;
                
            case Arm64Mnemonic.ADDS:
                ProcessAdds(instruction, builder);
                break;
                
            // 减法指令
            case Arm64Mnemonic.SUB:
            case Arm64Mnemonic.FSUB:
                ProcessSubtract(instruction, builder);
                break;
                
            case Arm64Mnemonic.SUBS:
                ProcessSubs(instruction, builder);
                break;
                
            // 乘法指令
            case Arm64Mnemonic.MUL:
            case Arm64Mnemonic.FMUL:
                ProcessMultiply(instruction, builder);
                break;
                
            // 乘加指令
            case Arm64Mnemonic.MADD:
                ProcessMultiplyAdd(instruction, builder);
                break;
                
            // 除法指令
            case Arm64Mnemonic.FDIV:
                ProcessDivide(instruction, builder);
                break;
                
            // 逻辑与指令
            case Arm64Mnemonic.AND:
                ProcessAnd(instruction, builder);
                break;
                
            case Arm64Mnemonic.ANDS:
                ProcessAnds(instruction, builder);
                break;
                
            // 逻辑或指令
            case Arm64Mnemonic.ORR:
                ProcessOr(instruction, builder);
                break;
                
            // 逻辑异或指令
            case Arm64Mnemonic.EOR:
                ProcessXor(instruction, builder);
                break;
                
            // 位清除指令
            case Arm64Mnemonic.BIC:
                ProcessBitClear(instruction, builder);
                break;
                
            // 逻辑或非指令
            case Arm64Mnemonic.ORN:
                ProcessOrNot(instruction, builder);
                break;
            case Arm64Mnemonic.MOV:
            {
                ProcessMov(instruction, builder);
                break;
            }
            case Arm64Mnemonic.ADRP:
            {
                ProcessAdrp(instruction, builder);
                break;
            }
            default:
                throw new NotImplementedException($"数据处理指令 {instruction.Mnemonic} 尚未实现");
        }
    }

    private void ProcessAdrp(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Move(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));

    }

    private void ProcessMov(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Move(instruction.Address, ConvertOperand(instruction, 0),
            IsUseZeroReg(instruction, out var zeroName)
                ? InstructionSetIndependentOperand.MakeImmediate(0)
                : ConvertOperand(instruction, 1));
    }

    /// <summary>
    /// 处理加法指令 (ADD/FADD)
    /// </summary>
    private void ProcessAdd(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 处理特殊的移位或扩展类型
        if (instruction.FinalOpShiftType != Arm64ShiftType.NONE ||
            instruction.FinalOpExtendType != Arm64ExtendType.NONE)
        {
            ProcessExtendedAdd(instruction, builder);
            return;
        }
        
        // 标准加法
        builder.Add(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }
    
    /// <summary>
    /// 处理带标志位的加法指令 (ADDS)
    /// </summary>
    private void ProcessAdds(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 标准加法，但会设置标志位
        builder.Add(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
        
        // 标志位已在Process方法中通过HandleFlagsIfNeeded处理
    }
    
    /// <summary>
    /// 处理带移位或扩展的加法指令
    /// </summary>
    private void ProcessExtendedAdd(Arm64Instruction instruction, IsilBuilder builder)
    {
        if (instruction.FinalOpShiftType == Arm64ShiftType.LSL)
        {
            // 逻辑左移处理
            var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
            var src = ConvertOperand(instruction, 2);
            var shiftAmount = ConvertOperand(instruction, 3);
            
            // 如果是立即数移位，可以优化为乘法
            if (shiftAmount.Type == InstructionSetIndependentOperand.OperandType.Immediate)
            {
                var shiftValue = Math.Pow(2, Convert.ToInt64(((IsilImmediateOperand)shiftAmount.Data).Value));
                builder.Multiply(instruction.Address, temp, src,
                    InstructionSetIndependentOperand.MakeImmediate(shiftValue));
            }
            else
            {
                // 如果不是立即数，使用左移操作
                builder.Move(instruction.Address, temp, src);
                builder.ShiftLeft(instruction.Address, temp, shiftAmount);
            }
            
            // 执行加法
            builder.Add(instruction.Address, 
                ConvertOperand(instruction, 0),
                ConvertOperand(instruction, 1),
                temp);
        }
        else if (instruction.FinalOpExtendType == Arm64ExtendType.SXTW)
        {
            // 符号扩展处理
            // 通常用于将32位寄存器扩展为64位寄存器后再相加
            // 在ISIL中只需直接加法，因为它会处理类型转换
            builder.Add(instruction.Address,
                ConvertOperand(instruction, 0),
                ConvertOperand(instruction, 1),
                ConvertOperand(instruction, 2));
        }
        else
        {
            Logger.WarnNewline($"未处理的移位/扩展类型: {instruction.FinalOpShiftType}/{instruction.FinalOpExtendType}");
            // 对于其他不支持的移位或扩展类型，回退到标准加法
            builder.Add(instruction.Address,
                ConvertOperand(instruction, 0),
                ConvertOperand(instruction, 1),
                ConvertOperand(instruction, 2));
        }
    }
    
    /// <summary>
    /// 处理减法指令 (SUB/FSUB)
    /// </summary>
    private void ProcessSubtract(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Subtract(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }
    
    /// <summary>
    /// 处理带标志位的减法指令 (SUBS)
    /// </summary>
    private void ProcessSubs(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Subtract(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
        
        // 标志位已在Process方法中通过HandleFlagsIfNeeded处理
    }
    
    /// <summary>
    /// 处理乘法指令 (MUL/FMUL)
    /// </summary>
    private void ProcessMultiply(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Multiply(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
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
    /// 处理除法指令 (FDIV)
    /// </summary>
    private void ProcessDivide(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Divide(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }
    
    /// <summary>
    /// 处理逻辑与指令 (AND)
    /// </summary>
    private void ProcessAnd(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.And(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }
    
    /// <summary>
    /// 处理带标志位的逻辑与指令 (ANDS)
    /// </summary>
    private void ProcessAnds(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.And(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
        
        // 标志位已在Process方法中通过HandleFlagsIfNeeded处理
    }
    
    /// <summary>
    /// 处理逻辑或指令 (ORR)
    /// </summary>
    private void ProcessOr(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Or(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }
    
    /// <summary>
    /// 处理逻辑异或指令 (EOR)
    /// </summary>
    private void ProcessXor(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.Xor(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
    }
    
    /// <summary>
    /// 处理位清除指令 (BIC) - 相当于AND NOT
    /// </summary>
    private void ProcessBitClear(Arm64Instruction instruction, IsilBuilder builder)
    {
        // BIC rd, rn, rm  =>  rd = rn & ~rm
        var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");
        
        // 首先对第二个操作数取反
        builder.Not(instruction.Address, ConvertOperand(instruction, 2));
        
        // 然后执行与操作
        builder.And(instruction.Address,
            ConvertOperand(instruction, 0),
            ConvertOperand(instruction, 1),
            ConvertOperand(instruction, 2));
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
            if (imm == 0)  // 没有移位
            {
                // 创建临时寄存器存储取反后的值
                var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");
                var rmReg = ConvertOperand(instruction, 2);
                
                // 复制第二个操作数
                builder.Move(instruction.Address, tempReg, rmReg);
                
                // 对复制的值取反
                builder.Not(instruction.Address, tempReg);
                
                // 检查第一个操作数是否为零寄存器
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
