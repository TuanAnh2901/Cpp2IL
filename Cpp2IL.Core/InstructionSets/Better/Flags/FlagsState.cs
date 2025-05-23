using Cpp2IL.Core.ISIL;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags;

/// <summary>
/// 标志位处理器类型
/// </summary>
public enum FlagsProcessorType
{
    Compare, // 比较指令 (CMP, FCMP)
    Arithmetic, // 算术运算 (ADD, SUB, MUL)
    Logical, // 逻辑运算 (ANDS)
    BitTest // 位测试 (TST)
}

/// <summary>
/// 标志位状态类，用于跟踪更新标志位的指令
/// </summary>
public class FlagsState
{
    /// <summary>
    /// 指令地址
    /// </summary>
    public ulong Address { get; set; }
    
    public InstructionSetIndependentOperand? OriDest { get; set; }
    public InstructionSetIndependentOperand? Dest
    {
        get
        {
            if (OverrideDest != null)
            {
                return OverrideDest;
            }

            if (OriDest != null)
            {
                return OriDest;
            }

            return null;
        }
    }

    public InstructionSetIndependentOperand? OverrideDest { get; set; }
    // 保存原始操作数
    // public InstructionSetIndependentOperand Arg1 =>
    public InstructionSetIndependentOperand? Arg1
    {
        get
        {
            if (OverrideSrc1 != null)
            {
                return OverrideSrc1;
            }

            if (Src1 != null)
            {
                return Src1;
            }

            return null;
        }
    }
    public InstructionSetIndependentOperand? Arg2
    {
        get
        {
            if (OverrideSrc2 != null)
            {
                return OverrideSrc2;
            }

            if (Src2 != null)
            {
                return Src2;
            }

            return null;
        }
    }

    public InstructionSetIndependentOperand? Src1 { get; set; }
    public InstructionSetIndependentOperand? Src2 { get; set; }
    
    public InstructionSetIndependentOperand?OverrideSrc1 { get; set; }
    public InstructionSetIndependentOperand?OverrideSrc2 { get; set; }
    // 记录处理器类型
    public FlagsProcessorType ProcessorType { get; set; }

    /// <summary>
    /// 源指令助记符
    /// </summary>
    public Arm64Mnemonic SourceMnemonic { get; set; }

    public override string ToString()
    {
        return  $"FlagsState: {SourceMnemonic} at {Address:X} " +
               $"Src1: {Src1}, Src2: {Src2}, ProcessorType: {ProcessorType}";
    }
}
