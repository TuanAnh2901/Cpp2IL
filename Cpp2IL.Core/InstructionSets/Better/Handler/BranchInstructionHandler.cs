using System;
using System.Collections.Generic;
using System.Linq;
using Disarm;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.InstructionSets.Better.Flags;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using Disarm.InternalDisassembly;

namespace Cpp2IL.Core.InstructionSets.Better;

/// <summary>
/// 分支指令处理器，负责处理各种跳转和调用指令
/// </summary>
public class BranchInstructionHandler : BaseArm64InstructionHandler
{
    public BranchInstructionHandler(FlagsStateManager flagsManager, BetterArmV8InstructionSet set) : base(flagsManager,
        set)
    {
    }

    public override bool CanHandle(Arm64Instruction instruction)
    {
        return instruction.Mnemonic switch
        {
            // 无条件分支指令
            Arm64Mnemonic.B or Arm64Mnemonic.BL or
                Arm64Mnemonic.BR or Arm64Mnemonic.BLR or
                Arm64Mnemonic.RET => true,

            // 条件分支指令
            Arm64Mnemonic.CBZ or Arm64Mnemonic.CBNZ or
                Arm64Mnemonic.TBZ or Arm64Mnemonic.TBNZ => true,

            //CEST //
            Arm64Mnemonic.CSET or Arm64Mnemonic.CSEL => true,
            _ => false
        };
    }

    public override bool Process(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        switch (instruction.Mnemonic)
        {
            // 无条件分支指令
            case Arm64Mnemonic.B:
                ProcessBranch(instruction, builder, context);
                break;

            case Arm64Mnemonic.BL:
                ProcessBranchLink(instruction, builder, context);
                break;

            case Arm64Mnemonic.BR:
                ProcessBranchRegister(instruction, builder);
                break;

            case Arm64Mnemonic.BLR:
                ProcessBranchLinkRegister(instruction, builder);
                break;

            case Arm64Mnemonic.RET:
                ProcessReturn(instruction, builder, context);
                break;

            // 条件分支指令
            case Arm64Mnemonic.CBZ:
            case Arm64Mnemonic.CBNZ:
                ProcessCompareAndBranch(instruction, builder, context);
                break;

            case Arm64Mnemonic.TBZ:
            case Arm64Mnemonic.TBNZ:
                ProcessTestBitAndBranch(instruction, builder);
                break;
            case Arm64Mnemonic.CSEL:
            case Arm64Mnemonic.CSET:
            {
                ProcessConditionalSelect(instruction, builder);
                break;
            }
            default:
                throw new NotImplementedException($"分支指令 {instruction.Mnemonic} 尚未实现");
        }

        return false;
    }

    private bool IsManagerCall(ulong target, MethodAnalysisContext context,out MethodAnalysisContext? method)
    {
        // 目标地址在方法范围内，直接调用
        if (Cpp2IlApi.CurrentAppContext!.MethodsByAddress.TryGetValue(target, out var list))
        {
            method = list.FirstOrDefault();
            return true;
        }

        method = null;
        return false;
    }
    

    /// <summary>
    /// 处理无条件分支指令 (B)
    /// </summary>
    private void ProcessBranch(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        if (instruction.MnemonicConditionCode != Arm64ConditionCode.NONE)
        {
            ProcessConditionalBranch(instruction, builder); //跳转指令
            return;
        }

        var target = instruction.BranchTarget;
        var methodBytesLen = (context.UnderlyingPointer + (ulong)context.RawBytes.Length) - context.UnderlyingPointer;
        if (context.RawBytes.Length == 4) //it's inline call
        {
            //如果是inline指令 大概率是管理的跳转
            if (IsManagerCall(instruction.BranchTarget, context, out _))
            {
                // 获取调用参数
                var args = GetArgumentOperandsForCall(context, target).ToArray();
                // 生成调用
                builder.Call(instruction.Address, target, args);
                builder.Return(instruction.Address, GetReturnRegisterForContext(context));
            }
            else
            {
                builder.Goto(instruction.Address, instruction.BranchTarget);
            }

            return;
        }


        Logger.InfoNewline("target :" + target.ToString("X") + " ins " + instruction + " MethodStart 0x"+context.UnderlyingPointer.ToString("X")+ " methodEnd 0x" +
                           (context.UnderlyingPointer + (ulong)context.RawBytes.Length).ToString("X")
                           + "   method Len " + methodBytesLen );

        if (IsManagerCall(target, context, out var curBMethod))
        {
            // 获取调用参数
            var args = GetArgumentOperandsForCall(context, target).ToArray();

            // 生成调用
            builder.Call(instruction.Address, target, args);
            
            //当前的B 是否是最后一条指令？
            if (instruction.Address+4 == context.UnderlyingPointer + (ulong)context.RawBytes.Length)
            {
                //是最后一条指令
                builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                Logger.InfoNewline(" B 是最后一条指令");
                return;
            }
            //下面一条指令是否是NullCheck? 这里有个特殊的情况 如果下一条指令是NullCheck 那么大概率这个b跳转是个函数结束的标识  为了使isil 能创建CFG图 这里手动结束 增加一个return
            var nextIns = BranchHelper.GetArm64Ins(instruction.Address + 4);
            if (nextIns is { Mnemonic: Arm64Mnemonic.BL } blIns)
            {
                if (ArmV8InstructionSet.CreateKeyFunctionAddressesInstance() is NewArm64KeyFunctionAddresses keyfun &&
                    keyfun.IsNullCheck(blIns.BranchTarget))
                {
                    Logger.InfoNewline("是函数结束");
                    //是函数结束
                    builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                }
            }
            //当前指令是B => System.Object.ctor //那么需要判断下个指令是否还是跳转并且是某个函数的开头 因为这里有个特殊的情况 Len的长度获取的是错误的
            if (curBMethod!=null&& IsSystemObjectCtor(curBMethod))
            {
                if (nextIns is {Mnemonic: Arm64Mnemonic.B} bins)
                {
                    //如果下个指令是B 并且是跳转到一个函数的开头说明这个函数的结束标识 大概率是因为inline的原因
                    if (IsManagerCall(bins.BranchTarget, context,out _)) 
                    {
                        Logger.InfoNewline("是函数结束");
                        //是函数结束
                        builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                    }
                   
                }
            }
            //还需要特殊处理 判断下个指令是否是某个函数的开始 这样也能判断是否是结束标识 //因为一些特殊原因 Len的获取是有错误的
           return;
        }

        //是否在函数范围内?
        if (target >= context.UnderlyingPointer &&
            target <= context.UnderlyingPointer + (ulong)context.RawBytes.Length)
        {
            // 在函数范围内，直接跳转
            builder.Goto(instruction.Address, target);
        }
        else
        {
            // 不在函数范围内，可能是尾调用或其他情况
            ProcessTailCall(instruction, builder, context);
        }


        // 是否在函数范围内
        // if (target < context.UnderlyingPointer ||
        //     target > context.UnderlyingPointer + (ulong)context.RawBytes.Length)
        // {
        //     if (Cpp2IlApi.CurrentAppContext!.MethodsByAddress.TryGetValue(instruction.BranchTarget,
        //             out var list))
        //     {
        //         builder.Call(instruction.Address, instruction.BranchTarget,
        //             GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
        //         builder.Return(instruction.Address, GetReturnRegisterForContext(context));
        //     }
        //     else
        //     {
        //         BranchHelper.GetRealBranch(instruction, out var
        //             ins, out var jump);
        //         
        //         builder.Goto(instruction.Address, instruction.BranchTarget);
        //         if (jump >= context.UnderlyingPointer && jump <= (context.UnderlyingPointer +
        //                                                           (ulong)context.RawBytes.Length)
        //                                               && jump != 0)
        //         {
        //             foreach (var VARIABLE in ins)
        //             {
        //                 Logger.InfoNewline("Conver other " + VARIABLE);
        //                ArmV8InstructionSet.ProcessInstruction( VARIABLE, builder, context);
        //             }
        //             return;
        //         }
        //     }
        //
        //     //Unconditional branch to outside the method, treat as call (tail-call, specifically) followed by return
        // }
        // else
        // {
        //     if (instruction.MnemonicConditionCode != Arm64ConditionCode.NONE)
        //     {
        //         ProcessConditionalBranch(instruction, builder);
        //     }
        //     else
        //     {
        //         //is call in method addr range just go to 
        //         if (context.RawBytes.Length == 4) //it's inline call
        //         {
        //             //we need parser this call method
        //             builder.Call(instruction.Address, instruction.BranchTarget,
        //                 GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
        //            return;
        //         }
        //
        //         //it's goto manager method?
        //         if (Cpp2IlApi.CurrentAppContext!.MethodsByAddress.TryGetValue(instruction.BranchTarget,
        //                 out var list))
        //         {
        //             builder.Call(instruction.Address, instruction.BranchTarget,
        //                 GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
        //             if (target == context.UnderlyingPointer + (ulong)context.RawBytes.Length)
        //             {
        //                 //it's mean return
        //                 builder.Return(instruction.Address, GetReturnRegisterForContext(context)); //跳转的地址是下一个函数
        //             }
        //         }
        //         else
        //         {
        //             builder.Goto(instruction.Address, instruction.BranchTarget);
        //         }
        //     }
        // }
    }

    /// <summary>
    /// 处理带链接的分支指令 (BL)
    /// </summary>
    private void ProcessBranchLink(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        var target = instruction.BranchTarget;

        // 获取调用参数
        var args = GetArgumentOperandsForCall(context, target).ToArray();

        // 生成调用
        builder.Call(instruction.Address, target, args);
    }

    /// <summary>
    /// 处理寄存器分支指令 (BR)
    /// </summary>
    private void ProcessBranchRegister(Arm64Instruction instruction, IsilBuilder builder)
    {
        // BR指令跳转到寄存器中的地址，通常用于间接跳转
        var targetReg = ConvertOperand(instruction, 0);

        // 生成寄存器调用，不返回
        builder.CallRegister(instruction.Address, targetReg, noReturn: true);
    }

    /// <summary>
    /// 处理带链接的寄存器分支指令 (BLR)
    /// </summary>
    private void ProcessBranchLinkRegister(Arm64Instruction instruction, IsilBuilder builder)
    {
        builder.VirtualCall(instruction.Address, ConvertOperand(instruction, 0));
    }

    /// <summary>
    /// 处理返回指令 (RET)
    /// </summary>
    private void ProcessReturn(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        // 获取返回值寄存器
        var returnReg = GetReturnRegisterForContext(context);

        // 生成返回语句
        builder.Return(instruction.Address, returnReg);
    }

    private bool IsSystemObjectCtor(MethodAnalysisContext context)
    {
        if (context.DeclaringType!.FullName=="System.Object" && context.Name == ".ctor")
        {
            return true;
        }

        return false;
    }
    /// <summary>
    /// 处理比较并分支指令 (CBZ/CBNZ)
    /// </summary>
    private void ProcessCompareAndBranch(Arm64Instruction instruction, IsilBuilder builder,
        MethodAnalysisContext context)
    {
        // 计算目标地址
        var targetAddr = (ulong)((long)instruction.Address + instruction.Op1Imm);

        // 检查目标是否在方法范围内
        if (!IsInMethodRange(targetAddr, context))
        {
            // 超出范围，可能是一些特殊情况，如空检查
            if (instruction.Mnemonic == Arm64Mnemonic.CBZ)
            {
                // 零检查通常用于空引用检查，可以忽略
                Logger.InfoNewline($"忽略对参数的空检查，跳转目标 0x{targetAddr:X} 超出方法范围");
                return;
            }

            throw new Exception($"跳转目标 0x{targetAddr:X} 超出方法范围，且不是常见的空检查");
        }

        // 与零比较
        builder.Compare(instruction.Address, ConvertOperand(instruction, 0),
            InstructionSetIndependentOperand.MakeImmediate(0));

        // 生成条件跳转
        if (instruction.Mnemonic == Arm64Mnemonic.CBZ)
            builder.JumpIfEqual(instruction.Address, targetAddr);
        else
            builder.JumpIfNotEqual(instruction.Address, targetAddr);
    }

    /// <summary>
    /// an处理测试位并分支指令 (TBZ/TBNZ)
    /// </summary>
    private void ProcessTestBitAndBranch(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 计算目标地址
        var targetAddr = (ulong)((long)instruction.Address + instruction.Op2Imm);

        // 计算位掩码，将要测试的bit位设为1
        var bitMask = InstructionSetIndependentOperand.MakeImmediate(1 << (int)instruction.Op1Imm);

        // 创建临时寄存器
        var tempReg = InstructionSetIndependentOperand.MakeRegister("TEMP");

        // 获取源寄存器
        var srcReg = ConvertOperand(instruction, 0);

        // 将源寄存器复制到临时寄存器 // temp = src
        builder.Move(instruction.Address, tempReg, srcReg);

        // 对临时寄存器进行与运算，保留要测试的位 //Temp = temp & bitMask
        builder.And(instruction.Address, tempReg, tempReg, bitMask);

        // 比较结果是否等于位掩码（测试位是否为1） // temp == bitMask
        builder.Compare(instruction.Address, tempReg, bitMask);

        // 根据指令类型生成条件跳转
        if (instruction.Mnemonic == Arm64Mnemonic.TBNZ)
            // 位为1时跳转
            builder.JumpIfEqual(instruction.Address, targetAddr);
        else
            // 位为0时跳转
            builder.JumpIfNotEqual(instruction.Address, targetAddr);
    }

    private void ProcessConditionalSelect(Arm64Instruction instruction, IsilBuilder builder)
    {
        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.CSET:
            {
                FlagsManager.BuildConditionalSelect(
                    builder,
                    instruction,
                    ConvertOperand(instruction, 0),
                    InstructionSetIndependentOperand.MakeImmediate(1),
                    InstructionSetIndependentOperand.MakeImmediate(0),
                    instruction.FinalOpConditionCode);
                break;
            }
            case Arm64Mnemonic.CSEL:
            {
                FlagsManager.BuildConditionalSelect(
                    builder,
                    instruction,
                    ConvertOperand(instruction, 0),
                    ConvertOperand(instruction, 1).FixZero(),
                    ConvertOperand(instruction, 2).FixZero(),
                    instruction.FinalOpConditionCode);
                break;
            }
        }
    }

    /// <summary>
    /// 处理条件分支指令
    /// </summary>
    private void ProcessConditionalBranch(Arm64Instruction instruction, IsilBuilder builder)
    {
        // 使用标志位管理器处理条件码
        FlagsManager.BuildCompareForCondition(
            builder,
            instruction.Address,
            instruction.MnemonicConditionCode,
            instruction.BranchTarget);
    }

    /// <summary>
    /// 处理尾调用
    /// </summary>
    private void ProcessTailCall(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        var target = instruction.BranchTarget;


        BranchHelper.GetRealBranch(instruction, out var inlinedInstructions, out var jump);
        builder.Goto(instruction.Address, instruction.BranchTarget);
        if (jump >= context.UnderlyingPointer && jump <= (context.UnderlyingPointer +
                                                          (ulong)context.RawBytes.Length)
                                              && jump != 0)
        {
            Logger.InfoNewline("inline count " + inlinedInstructions.Count);
            foreach (var VARIABLE in inlinedInstructions)
            {
                ArmV8InstructionSet.ProcessInstruction(VARIABLE, builder, context);
            }
        }
        else
        {
            // 没有找到对应方法，当作普通跳转处理
            builder.Goto(instruction.Address, target);
        }
    }

    /// <summary>
    /// 判断是否为方法外部的分支
    /// </summary>
    private bool IsExternalBranch(ulong targetAddress, MethodAnalysisContext context)
    {
        return targetAddress < context.UnderlyingPointer ||
               targetAddress > (context.UnderlyingPointer + (ulong)context.RawBytes.Length);
    }

    /// <summary>
    /// 判断地址是否在方法范围内
    /// </summary>
    private bool IsInMethodRange(ulong address, MethodAnalysisContext context)
    {
        return address >= context.UnderlyingPointer &&
               address <= context.UnderlyingPointer + (ulong)context.RawBytes.Length;
    }

    /// <summary>
    /// 获取调用参数
    /// </summary>
    private List<InstructionSetIndependentOperand> GetArgumentOperandsForCall(
        MethodAnalysisContext contextBeingAnalyzed, ulong callAddr)
    {
        if (!contextBeingAnalyzed.AppContext.MethodsByAddress.TryGetValue(callAddr, out var methodsAtAddress))
            //TODO
            return new List<InstructionSetIndependentOperand>();

        //For the sake of arguments, all we care about is the first method at the address, because they'll only be shared if they have the same signature.
        var contextBeingCalled = methodsAtAddress.First();

        var vectorCount = 0;
        var nonVectorCount = 0;

        var ret = new List<InstructionSetIndependentOperand>();

        //Handle 'this' if it's an instance method
        if (!contextBeingCalled.IsStatic)
        {
            ret.Add(InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0)));
            nonVectorCount++;
        }

        foreach (var parameter in contextBeingCalled.Parameters)
        {
            var paramType = parameter.ParameterTypeContext;
            if (paramType.Namespace == nameof(System))
            {
                switch (paramType.Name)
                {
                    case "Single":
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.S0 + vectorCount++)
                            .ToString().ToUpperInvariant()));
                        break;
                    case "Double":
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.D0 + vectorCount++)
                            .ToString().ToUpperInvariant()));
                        break;
                    default:
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.X0 + nonVectorCount++)
                            .ToString().ToUpperInvariant()));
                        break;
                }
            }
            else
            {
                ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.X0 + nonVectorCount++).ToString()
                    .ToUpperInvariant()));
            }
        }

        return ret;
    }

    /// <summary>
    /// 获取返回值寄存器
    /// </summary>
    private InstructionSetIndependentOperand? GetReturnRegisterForContext(MethodAnalysisContext context)
    {
        var returnType = context.ReturnTypeContext;
        if (returnType.Namespace == nameof(System))
        {
            return returnType.Name switch
            {
                "Void" => null, // Void无返回值
                "Double" => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.D0)), // Double返回在D0
                "Single" => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.S0)), // Single返回在S0
                _ => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0)), // 其他系统类型返回在X0
            };
        }

        // 用户类型返回在X0
        return InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0));
    }
}
