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
    
    /// <summary>
    /// 记录指令的标志位状态
    /// </summary>
    /// <param name="instruction">设置标志位的指令</param>
    /// <param name="state">标志位状态</param>
    public void RecordFlagsState(Arm64Instruction instruction, Flags.FlagsState state)
    {
        _latestFlagsState = state;
        _flagsStateByType[instruction.Mnemonic] = state;
        Logger.InfoNewline($"记录标志位状态：{state}");
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
    
    /// <summary>
    /// 为条件码构建比较逻辑
    /// </summary>
    /// <param name="builder">ISIL构建器</param>
    /// <param name="address">指令地址</param>
    /// <param name="conditionCode">条件码</param>
    /// <param name="branchTarget">分支目标地址</param>
    public void BuildCompareForCondition(IsilBuilder builder, ulong address, Arm64ConditionCode conditionCode, ulong branchTarget)
    {
        if (_latestFlagsState == null)
        {
            throw new Exception($"无法找到用于地址0x{address:X}的标志位状态");
        }
        
        // // 根据源指令类型和条件码调整比较操作数
        // var arg0 = _latestFlagsState.Arg0;
        // var arg1 = _latestFlagsState.Arg1;
        //
        // // 对特定条件码进行特殊处理
        // switch (conditionCode)
        // {
        //     case Arm64ConditionCode.EQ:
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.NE:
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfNotEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.CS: // 无符号大于等于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfGreaterOrEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.CC: // 无符号小于
        //         // 对于进位标志的特殊处理
        //         var specialArg1 = GetSpecialCompareArg(_latestFlagsState.SourceMnemonic, conditionCode);
        //         builder.Compare(address, arg0, specialArg1 ?? arg1);
        //         builder.JumpIfLess(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.MI: // 负数
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfLess(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.PL: // 正数或零
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfGreaterOrEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.VS: // 溢出
        //         // 溢出标志特殊处理
        //         throw new NotImplementedException("溢出标志比较尚未实现");
        //         
        //     case Arm64ConditionCode.VC: // 无溢出
        //         // 无溢出标志特殊处理
        //         throw new NotImplementedException("无溢出标志比较尚未实现");
        //         
        //     case Arm64ConditionCode.HI: // 无符号大于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfGreater(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.LS: // 无符号小于等于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfLessOrEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.GE: // 有符号大于等于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfGreaterOrEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.LT: // 有符号小于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfLess(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.GT: // 有符号大于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfGreater(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.LE: // 有符号小于等于
        //         builder.Compare(address, arg0, arg1);
        //         builder.JumpIfLessOrEqual(address, branchTarget);
        //         break;
        //         
        //     case Arm64ConditionCode.AL: // 总是执行
        //         builder.Goto(address, branchTarget);
        //         break;
        //         
        //     default:
        //         throw new Exception($"未支持的条件码：{conditionCode}");
        // }
        throw   new NotImplementedException("条件码比较逻辑尚未实现");
    }
    
    /// <summary>
    /// 获取特定条件码的特殊比较参数
    /// </summary>
    private InstructionSetIndependentOperand? GetSpecialCompareArg(Arm64Mnemonic sourceMnemonic, Arm64ConditionCode conditionCode)
    {
        // 针对不同源指令和条件码，可能需要特殊的比较参数
        if (conditionCode == Arm64ConditionCode.CC)
        {
            if (sourceMnemonic == Arm64Mnemonic.SUBS)
            {
                return InstructionSetIndependentOperand.MakeImmediate(int.MaxValue);
            }
        }
        
        return null;
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
            case Arm64Mnemonic.SUBS:
            case Arm64Mnemonic.ADDS:
            case Arm64Mnemonic.ANDS:
                return true;
            default:
                // 查看指令是否以'S'结尾，ARM64中通常表示设置标志位
                string mnemonicStr = instruction.Mnemonic.ToString();
                return mnemonicStr.EndsWith("S") &&
                       mnemonicStr.Length > 1 &&
                       char.IsLower(mnemonicStr[mnemonicStr.Length - 2]);
        }
    }

    public Flags.FlagsState CreateFlagsState(Arm64Instruction instruction)
    {
        Flags.FlagsState state = new Flags.FlagsState { Address = instruction.Address, SourceMnemonic = instruction.Mnemonic };
        
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.CMP:
            case Arm64Mnemonic.FCMP:
             
                // state.Arg0 = ConvertOperand(instruction, 0);
                // state.Arg1 = ConvertOperand(instruction, 1);
                break;
                
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
} 
