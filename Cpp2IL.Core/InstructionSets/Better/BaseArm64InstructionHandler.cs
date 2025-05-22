using System;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Disarm;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Disarm.InternalDisassembly;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// ARM64指令处理器的基类，实现共用功能
/// </summary>
public abstract class BaseArm64InstructionHandler : IArm64InstructionHandler
{
    /// <summary>
    /// 标志位状态管理器
    /// </summary>
    protected FlagsStateManager FlagsManager { get; }
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="flagsManager">标志位状态管理器</param>
    protected BaseArm64InstructionHandler(FlagsStateManager flagsManager)
    {
        FlagsManager = flagsManager;
    }
    
    /// <summary>
    /// 判断处理器是否能处理指定指令
    /// </summary>
    /// <param name="instruction">ARM64指令</param>
    /// <returns>能否处理</returns>
    public abstract bool CanHandle(Arm64Instruction instruction);
    
    /// <summary>
    /// 处理指令并生成ISIL代码
    /// </summary>
    /// <param name="instruction">ARM64指令</param>
    /// <param name="builder">ISIL构建器</param>
    /// <param name="context">方法分析上下文</param>
    public abstract void Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context);
    
    /// <summary>
    /// 转换ARM64操作数为ISIL操作数
    /// </summary>
    /// <param name="instruction">ARM64指令</param>
    /// <param name="operandIndex">操作数索引</param>
    /// <returns>ISIL操作数</returns>
    protected InstructionSetIndependentOperand ConvertOperand(Arm64Instruction instruction, int operandIndex)
    {
        var kind = operandIndex switch
        {
            0 => instruction.Op0Kind,
            1 => instruction.Op1Kind,
            2 => instruction.Op2Kind,
            3 => instruction.Op3Kind,
            _ => throw new ArgumentOutOfRangeException(nameof(operandIndex),
                $"操作数索引必须在0到3之间。得到：{operandIndex}")
        };

        if (kind is Arm64OperandKind.Immediate or Arm64OperandKind.ImmediatePcRelative)
        {
            var imm = operandIndex switch
            {
                0 => instruction.Op0Imm,
                1 => instruction.Op1Imm,
                2 => instruction.Op2Imm,
                3 => instruction.Op3Imm,
                _ => throw new ArgumentOutOfRangeException(nameof(operandIndex),
                    $"操作数索引必须在0到3之间。得到：{operandIndex}")
            };

            if (kind == Arm64OperandKind.ImmediatePcRelative)
                imm += (long)instruction.Address + 4; // PC相对寻址是相对于下一条指令的地址

            return InstructionSetIndependentOperand.MakeImmediate(imm);
        }

        if (kind == Arm64OperandKind.FloatingPointImmediate)
        {
            var imm = operandIndex switch
            {
                0 => instruction.Op0FpImm,
                1 => instruction.Op1FpImm,
                2 => instruction.Op2FpImm,
                3 => instruction.Op3FpImm,
                _ => throw new ArgumentOutOfRangeException(nameof(operandIndex),
                    $"操作数索引必须在0到3之间。得到：{operandIndex}")
            };

            return InstructionSetIndependentOperand.MakeImmediate(imm);
        }

        if (kind == Arm64OperandKind.Register)
        {
            var reg = operandIndex switch
            {
                0 => instruction.Op0Reg,
                1 => instruction.Op1Reg,
                2 => instruction.Op2Reg,
                3 => instruction.Op3Reg,
                _ => throw new ArgumentOutOfRangeException(nameof(operandIndex),
                    $"操作数索引必须在0到3之间。得到：{operandIndex}")
            };

            return InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToUpperInvariant());
        }

        if (kind == Arm64OperandKind.Memory)
        {
            return CreateMemoryOperand(instruction);
        }

        if (kind == Arm64OperandKind.VectorRegisterElement)
        {
            var reg = operandIndex switch
            {
                0 => instruction.Op0Reg,
                1 => instruction.Op1Reg,
                2 => instruction.Op2Reg,
                3 => instruction.Op3Reg,
                _ => throw new ArgumentOutOfRangeException(nameof(operandIndex),
                    $"操作数索引必须在0到3之间。得到：{operandIndex}")
            };

            var vectorElement = operandIndex switch
            {
                0 => instruction.Op0VectorElement,
                1 => instruction.Op1VectorElement,
                2 => instruction.Op2VectorElement,
                3 => instruction.Op3VectorElement,
                _ => throw new ArgumentOutOfRangeException(nameof(operandIndex),
                    $"操作数索引必须在0到3之间。得到：{operandIndex}")
            };

            var width = vectorElement.Width switch
            {
                Arm64VectorElementWidth.B => IsilVectorRegisterElementOperand.VectorElementWidth.B,
                Arm64VectorElementWidth.H => IsilVectorRegisterElementOperand.VectorElementWidth.H,
                Arm64VectorElementWidth.S => IsilVectorRegisterElementOperand.VectorElementWidth.S,
                Arm64VectorElementWidth.D => IsilVectorRegisterElementOperand.VectorElementWidth.D,
                _ => throw new ArgumentOutOfRangeException(nameof(vectorElement.Width),
                    $"未知的向量元素宽度：{vectorElement.Width}")
            };

            return InstructionSetIndependentOperand.MakeVectorElement(reg.ToString().ToUpperInvariant(), width,
                vectorElement.Index);
        }

        return InstructionSetIndependentOperand.MakeImmediate($"<未实现的操作数类型 {kind}>");
    }
    
    /// <summary>
    /// 创建内存操作数
    /// </summary>
    protected InstructionSetIndependentOperand CreateMemoryOperand(Arm64Instruction instruction)
    {
        // var baseReg = instruction.MemBase;
        //
        // // 如果是无效寄存器，则只有偏移量
        // if (baseReg == Arm64Register.INVALID)
        // {
        //     return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(instruction.MemOffset));
        // }
        //
        // // 创建基础寄存器操作数
        // var baseRegOperand = InstructionSetIndependentOperand.MakeRegister(baseReg.ToString().ToUpperInvariant());
        //
        // // 处理带位移的寻址模式
        // if (instruction.MemShiftType != Arm64ShiftType.NONE && instruction.MemExtendOrShiftAmount != 0)
        // {
        //     if (instruction.MemAddendReg != Arm64Register.INVALID)
        //     {
        //         // 使用附加寄存器的移位寻址，如 [X0, X1, LSL #2]
        //         var addendReg = InstructionSetIndependentOperand.MakeRegister(
        //             instruction.MemAddendReg.ToString().ToUpperInvariant());
        //         
        //         // 计算移位值
        //         double shiftValue = 1;
        //         if (instruction.MemShiftType == Arm64ShiftType.LSL)
        //             shiftValue = Math.Pow(2, instruction.MemExtendOrShiftAmount);
        //         
        //         // 创建临时寄存器，用于存储移位后的地址
        //         var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");
        //         
        //         return InstructionSetIndependentOperand.MakeMemory(
        //             new IsilMemoryOperand(baseRegOperand, addendReg, (int)shiftValue));
        //     }
        // }
        //
        // // 标准的基址+偏移寻址，如 [X0, #8]
        // return InstructionSetIndependentOperand.MakeMemory(
        //     new IsilMemoryOperand(baseRegOperand, instruction.MemOffset));
        throw new Exception(" not support here");
    }
    
    /// <summary>
    /// 处理内存索引模式
    /// </summary>
    protected void ProcessIndexModes(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 处理预索引模式 - 先更新基址寄存器，再访问内存
        if (instruction.MemIsPreIndexed)
        {
            var baseReg = InstructionSetIndependentOperand.MakeRegister(
                instruction.MemBase.ToString().ToUpperInvariant());
            builder.Add(instruction.Address, baseReg, baseReg, 
                InstructionSetIndependentOperand.MakeImmediate(instruction.MemOffset));
        }
        
        // 处理后索引模式 - 先访问内存，再更新基址寄存器
        if (instruction.MemIndexMode == Arm64MemoryIndexMode.PostIndex)
        {
            var baseReg = InstructionSetIndependentOperand.MakeRegister(
                instruction.MemBase.ToString().ToUpperInvariant());
            builder.Add(instruction.Address, baseReg, baseReg, 
                InstructionSetIndependentOperand.MakeImmediate(instruction.MemOffset));
        }
    }
    protected bool IsUseZeroReg(Arm64Instruction instruction, out string zeroName)
    {
        zeroName = string.Empty;
        var left = ConvertOperand(instruction, 0);
        var right = ConvertOperand(instruction, 1);
        if (left.Type == InstructionSetIndependentOperand.OperandType.Register && right is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, Data: IsilRegisterOperand registerOperand
            })
        {
            if (registerOperand.IsZeroAlias)
            {
                zeroName = registerOperand.GetZeroRegName();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查操作数是否为零寄存器
    /// </summary>
    protected bool IsZeroReg(InstructionSetIndependentOperand operand, out string zeroName)
    {
        if (operand is
            {
                Type: InstructionSetIndependentOperand.OperandType.Register, 
                Data: IsilRegisterOperand registerOperand
            } && registerOperand.IsZeroAlias)
        {
            zeroName = registerOperand.GetZeroRegName();
            return true;
        }

        zeroName = "";
        return false;
    }
    
    /// <summary>
    /// 获取寄存器大小（字节）
    /// </summary>
    protected int GetRegisterSize(string reg)
    {
        if (reg.StartsWith("W")) return 4;  // 32位通用寄存器
        if (reg.StartsWith("X")) return 8;  // 64位通用寄存器
        if (reg.StartsWith("D")) return 8;  // 64位浮点寄存器
        if (reg.StartsWith("S")) return 4;  // 32位浮点寄存器
        if (reg.StartsWith("V")) return 16; // 128位向量寄存器
        if (reg.StartsWith("H")) return 2;  // 16位半精度浮点寄存器
        if (reg.StartsWith("B")) return 1;  // 8位字节寄存器
        
        throw new Exception($"不支持的寄存器类型：{reg}");
    }
    
    /// <summary>
    /// 创建标志位状态
    /// </summary>
    protected Flags.FlagsState CreateFlagsState(Arm64Instruction instruction)
    {
        var state = new Flags.FlagsState { Address = instruction.Address, SourceMnemonic = instruction.Mnemonic };
        
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.CMP:
            case Arm64Mnemonic.FCMP:
                // state.Src1= ConvertOperand(instruction, 0);
                // state.Src2 = ConvertOperand(instruction, 1);
                // break;
                
            // case Arm64Mnemonic.SUBS:
            //     state.Arg0 = ConvertOperand(instruction, 1); // 源操作数1
            //     state.Arg1 = ConvertOperand(instruction, 2); // 源操作数2
            //     break;
            //     
            // case Arm64Mnemonic.ADDS:
            //     state.Arg0 = ConvertOperand(instruction, 1); // 源操作数1
            //     state.Arg1 = ConvertOperand(instruction, 2); // 源操作数2
            //     break;
            //     
            // case Arm64Mnemonic.ANDS:
            //     state.Arg0 = ConvertOperand(instruction, 1); // 源操作数1
            //     state.Arg1 = ConvertOperand(instruction, 2); // 源操作数2
            //     break;
            //     
            default:
                throw new Exception($"未支持为指令 {instruction.Mnemonic} 创建标志位状态");
        }
        
        return state;
    }
    
    /// <summary>
    /// 检查指令是否设置标志位
    /// </summary>
    protected bool IsSetsFlagsInstruction(Arm64Instruction instruction)
    {
        return FlagsManager.IsSetsFlagsInstruction(instruction);
    }
    
    /// <summary>
    /// 处理标志位相关操作
    /// </summary>
    protected void HandleFlagsIfNeeded(Arm64Instruction instruction, IsilBuilder builder)
    {
        if (IsSetsFlagsInstruction(instruction))
        {
            var flagsState = CreateFlagsState(instruction);
            FlagsManager.RecordFlagsState(instruction, flagsState);
        }
    }
} 
