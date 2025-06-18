using System;
using System.Collections.Generic;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Flags;

/// <summary>
/// 标志位状态管理器，负责记录和管理ARM64指令的标志位状态
/// </summary>
public class FlagsStateManager
{
    private Flags.FlagsState? _latestFlagsState;
    private readonly Dictionary<Arm64Mnemonic, Flags.FlagsState> _flagsStateByType = new();
    private readonly FlagsProcessorFactory _processorFactory = new();

    /// <summary>
    /// 记录指令的标志位状态
    /// </summary>
    /// <param name="instruction">设置标志位的指令</param>
    /// <param name="state">标志位状态</param>
    public void RecordFlagsState(Arm64Instruction instruction, Flags.FlagsState state)
    {
        _latestFlagsState = state;
        _flagsStateByType[instruction.Mnemonic] = state;
        Logger.InfoNewline($"RecordFlagsState：{state}");
    }

    /// <summary>
    /// 获取最新的标志位状态
    /// </summary>
    /// <returns>标志位状态</returns>
    public Flags.FlagsState? GetLatestFlagsState() => _latestFlagsState;

    /// <summary>
    /// 获取特定类型指令的标志位状态
    /// </summary>
    /// <param name="mnemonic">指令助记符</param>
    /// <returns>标志位状态</returns>
    public Flags.FlagsState? GetFlagsStateByType(Arm64Mnemonic mnemonic)
    {
        return _flagsStateByType.TryGetValue(mnemonic, out var state) ? state : null;
    }
    public void BuildConditionalNegate(
        IsilBuilder builder,
        Arm64Instruction instruction,
        InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand source,
        Arm64ConditionCode conditionCode)
    {
        if (_latestFlagsState == null)
        {
            throw new Exception($"无法找到用于地址0x{instruction.Address:X}的标志位状态");
        }

        var processor = _processorFactory.GetProcessor(_latestFlagsState);
      
        processor.GenerateConditionalNegate(builder, instruction.Address, _latestFlagsState, dest, source, conditionCode);
    }
    public void BuildConditionalIncrement2Args(
        IsilBuilder builder,
        Arm64Instruction instruction,
        InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand arg1,
        InstructionSetIndependentOperand arg2,
        Arm64ConditionCode conditionCode)
    {
        if (_latestFlagsState == null)
        {
            throw new Exception($"无法找到用于地址0x{instruction.Address:X}的标志位状态");
        }

        var processor = _processorFactory.GetProcessor(_latestFlagsState);
      
        processor.GenerateConditionalIncrement2Args(builder, instruction.Address, _latestFlagsState, dest, arg1, arg2, conditionCode);
    }
    public void BuildConditionalIncrement(
        IsilBuilder builder,
        Arm64Instruction instruction,
        InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand source,
        Arm64ConditionCode conditionCode)
    {
        if (_latestFlagsState == null)
        {
            throw new Exception($"无法找到用于地址0x{instruction.Address:X}的标志位状态");
        }

        var processor = _processorFactory.GetProcessor(_latestFlagsState);
      
        processor.GenerateConditionalIncrement(builder, instruction.Address, _latestFlagsState, dest, source, conditionCode);
    }
    public void BuildConditionalSelect(
        IsilBuilder builder,
        Arm64Instruction instruction,
        InstructionSetIndependentOperand dest,
        InstructionSetIndependentOperand trueValue,
        InstructionSetIndependentOperand falseValue,
        Arm64ConditionCode conditionCode)
    {
        if (_latestFlagsState == null)
        {
            throw new Exception($"无法找到用于地址0x{instruction.Address:X}的标志位状态");
        }

        var processor = _processorFactory.GetProcessor(_latestFlagsState);
      
        processor.GenerateConditionalSelect(builder, instruction.Address, _latestFlagsState, dest, trueValue,
            falseValue, conditionCode);
    }

    /// <summary>
    /// 为条件码构建比较逻辑
    /// </summary>
    /// <param name="builder">ISIL构建器</param>
    /// <param name="address">指令地址</param>
    /// <param name="conditionCode">条件码</param>
    /// <param name="branchTarget">分支目标地址</param>
    public void BuildCompareForCondition(IsilBuilder builder, ulong address, Arm64ConditionCode conditionCode,
        ulong branchTarget)
    {
        if (_latestFlagsState == null)
        {
            throw new Exception($"无法找到用于地址0x{address:X}的标志位状态");
        }

        var processor = _processorFactory.GetProcessor(_latestFlagsState);
        processor.GenerateCompareAndJump(builder, _latestFlagsState, conditionCode, branchTarget, address);
    }

    
    public bool IsArithmeticInstruction(Arm64Instruction instruction)
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
    /// <summary>
    /// 检查指令是否设置标志位
    /// </summary>
    /// <param name="instruction">ARM64指令</param>
    /// <returns>是否设置标志位</returns>
    public bool IsSetsFlagsInstruction(Arm64Instruction instruction)
    {
        // 列出所有设置标志位的指令
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.CMP:
            case Arm64Mnemonic.FCMP:

                return true;
            default:
                return false;
        }
    }
}
