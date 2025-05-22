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

    public InstructionSetIndependentOperand Dest { get; set; }

    // 保存原始操作数
    public InstructionSetIndependentOperand Src1 { get; set; }
    public InstructionSetIndependentOperand Src2 { get; set; }

    // 记录处理器类型
    public FlagsProcessorType ProcessorType { get; set; }

    /// <summary>
    /// 源指令助记符
    /// </summary>
    public Arm64Mnemonic SourceMnemonic { get; set; }
}
