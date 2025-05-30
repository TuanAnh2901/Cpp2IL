using System;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Disarm;

namespace Cpp2IL.Core.InstructionSets.Better.Handler;

public class FlagsProcessHandler(FlagsStateManager flagsManager, BetterArmV8InstructionSet set)
    : BaseArm64InstructionHandler(flagsManager, set)
{
    public override bool CanHandle(Arm64Instruction instruction)
    {
        return FlagsManager.IsSetsFlagsInstruction(instruction);
    }

    public override bool Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
       HandleFlagsIfNeeded(instruction, builder);
       return false;
    }
    
     
    /// <summary>
    /// 创建标志位状态
    /// </summary>
    private Flags.FlagsState CreateFlagsState(Arm64Instruction instruction)
    {
        var state = new Flags.FlagsState { Address = instruction.Address, SourceMnemonic = instruction.Mnemonic };
        
        switch (instruction.Mnemonic)
        {   
            case Arm64Mnemonic.CMP:
            case Arm64Mnemonic.FCMP:
                state.Src1= ConvertOperand(instruction, 0);
                state.Src2 = ConvertOperand(instruction, 1);
                state.ProcessorType= FlagsProcessorType.Compare;
                //是否有拓展符号？
                
                break;
            case Arm64Mnemonic.ANDS:
            case Arm64Mnemonic.ADDS:
            case Arm64Mnemonic.SUBS:
                var dest = ConvertOperand(instruction, 0); // 目标寄存器
                if (IsZeroReg( dest, out _ ))
                {
                   //如果是0寄存器 说明是不影响结果 只是需要标志位 使用临时寄存器来存储结果方便后续的比较
                   state.OriDest=InstructionSetIndependentOperand.MakeRegister("TEMP"); 
                }
                else
                {
                    state.OriDest= ConvertOperand(instruction, 0); // 目标寄存器
                }
                state.Src1= ConvertOperand(instruction, 1); // 源操作数1
                state.Src2 = ConvertOperand(instruction, 2); // 源操作数2
                state.ProcessorType= FlagsProcessorType.Arithmetic;
                Logger.InfoNewline("CreateFlagsState  by ADDS/SUBS ");
                break;
            
            //     
            // case Arm64Mnemonic.ANDS:
            //     state.Arg0 = ConvertOperand(instruction, 1); // 源操作数1
            //     state.Arg1 = ConvertOperand(instruction, 2); // 源操作数2
            //     break;
            //     
            default:
                throw new Exception($"未支持为指令 {instruction.Mnemonic} 创建标志位状态  Ins ==> {instruction}");
        }
        
        return state;
    }
    
    /// <summary>
    /// 检查指令是否设置标志位
    /// </summary>
    private bool IsSetsFlagsInstruction(Arm64Instruction instruction)
    {
        return FlagsManager.IsSetsFlagsInstruction(instruction);
    }
    
    public void UpdateFlagsState(Arm64Instruction instruction, IsilBuilder builder)
    {
        var flagsState = CreateFlagsState(instruction);
        FlagsManager.RecordFlagsState(instruction, flagsState);
    }
    /// <summary>
    /// 处理标志位相关操作
    /// </summary>
    private void HandleFlagsIfNeeded(Arm64Instruction instruction, IsilBuilder builder)
    {
        if (IsSetsFlagsInstruction(instruction))
        {
            var flagsState = CreateFlagsState(instruction);
            FlagsManager.RecordFlagsState(instruction, flagsState);
        }
    }
}
